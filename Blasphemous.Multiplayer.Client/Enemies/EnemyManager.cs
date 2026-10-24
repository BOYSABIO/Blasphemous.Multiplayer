using Blasphemous.ModdingAPI;
using Framework.FrameworkCore;
using Gameplay.GameControllers.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Tools.Level.Layout;
using UnityEngine;

namespace Blasphemous.Multiplayer.Client.Enemies
{
    /// <summary>
    /// Host-authoritative room-enemy sync.
    /// Canonical IDs come from the scene host's snapshots. Damage always carries
    /// hit position + enemy type so the host can remap when client hashes disagree.
    /// </summary>
    public class EnemyManager
    {
        private readonly Dictionary<int, SyncedEnemy> _enemies = new();
        private readonly float _positionCutoff = 0.04f;
        private readonly float _lifeCutoff = 0.25f;
        private readonly float _scanInterval = 0.25f;
        private readonly float _snapshotInterval = 0.6f;
        private readonly float[] _deferredScanAt = { 0.25f, 0.75f, 1.5f, 3.0f };
        private int _deferredScanIndex;
        private float _levelAge;
        private float _scanTimer;
        private float _snapshotTimer;
        private float _requestCooldown;
        private bool _isSceneHost;
        private string _currentScene = string.Empty;
        private string _sceneHostName = string.Empty;
        private bool _applyingRemoteDamage;
        private int _snapshotLogCooldown;

        public bool IsSceneHost => _isSceneHost;
        public bool ApplyingRemoteDamage
        {
            get => _applyingRemoteDamage;
            set => _applyingRemoteDamage = value;
        }

        public void LevelLoaded(string scene)
        {
            Clear();
            _currentScene = scene ?? string.Empty;
            _sceneHostName = string.Empty;
            _levelAge = 0f;
            _deferredScanIndex = 0;

            if (!Main.Multiplayer.CurrentlyInLevel || !Main.Multiplayer.NetworkManager.IsConnected)
                return;

            ClaimOrFollowExistingHost();
            ScanAndRegisterEnemies(forceSnapshot: _isSceneHost);
            if (!_isSceneHost)
                RequestSyncFromHost();

            ModLog.Info($"[EnemySync] Level {scene} — host={_isSceneHost} ({_sceneHostName}), tracked={_enemies.Count}");
        }

        public void LevelUnloaded()
        {
            Clear();
            _currentScene = string.Empty;
            _sceneHostName = string.Empty;
            _isSceneHost = false;
        }

        public void OnDisconnect()
        {
            foreach (var synced in _enemies.Values.Distinct())
            {
                if (synced.IsValid && synced.IsPuppet)
                    synced.SetPuppet(false);
            }
            Clear();
            _isSceneHost = false;
            _sceneHostName = string.Empty;
        }

        public void OnPlayersInSceneChanged()
        {
            if (!Main.Multiplayer.CurrentlyInLevel || !Main.Multiplayer.NetworkManager.IsConnected)
                return;

            bool wasHost = _isSceneHost;
            RecalculateHostAfterRosterChange();
            ApplyAuthorityToAll();

            if (wasHost != _isSceneHost)
                ModLog.Info($"[EnemySync] Host → {_isSceneHost} ({_sceneHostName})");

            if (_isSceneHost)
                ScanAndRegisterEnemies(forceSnapshot: true);
            else
            {
                ApplyAuthorityToAll();
                SuppressUntrackedEnemyAi();
                RequestSyncFromHost();
            }
        }

        public void Update()
        {
            if (!Main.Multiplayer.CurrentlyInLevel || !Main.Multiplayer.NetworkManager.IsConnected)
                return;

            _levelAge += Time.deltaTime;
            if (_requestCooldown > 0f)
                _requestCooldown -= Time.deltaTime;

            while (_deferredScanIndex < _deferredScanAt.Length && _levelAge >= _deferredScanAt[_deferredScanIndex])
            {
                _deferredScanIndex++;
                ScanAndRegisterEnemies(forceSnapshot: _isSceneHost);
                if (!_isSceneHost)
                    RequestSyncFromHost();
            }

            _scanTimer -= Time.deltaTime;
            if (_scanTimer <= 0f)
            {
                _scanTimer = _scanInterval;
                ScanAndRegisterEnemies(forceSnapshot: false);
                if (!_isSceneHost)
                {
                    ApplyAuthorityToAll();
                    SuppressUntrackedEnemyAi();
                }
            }

            if (!_isSceneHost)
                return;

            _snapshotTimer -= Time.deltaTime;
            if (_snapshotTimer <= 0f)
            {
                _snapshotTimer = _snapshotInterval;
                SendAllSnapshots();
            }

            foreach (var synced in UniqueEnemies())
            {
                if (!synced.IsValid)
                    continue;

                if (synced.Enemy.Dead)
                {
                    if (synced.LastSentAlive)
                    {
                        synced.LastSentAlive = false;
                        ModLog.Info($"[EnemySync] Death {synced.NetId} ({synced.EnemyTypeId})");
                        Main.Multiplayer.NetworkManager.SendEnemyDeath(synced.NetId);
                    }
                    continue;
                }

                Vector2 pos = synced.Enemy.transform.position;
                float life = synced.Enemy.CurrentLife;
                bool facingRight = IsFacingRight(synced.Enemy);

                bool moved = Mathf.Abs(pos.x - synced.LastSentPosition.x) > _positionCutoff
                    || Mathf.Abs(pos.y - synced.LastSentPosition.y) > _positionCutoff;
                bool lifeChanged = Mathf.Abs(life - synced.LastSentLife) > _lifeCutoff;
                bool facingChanged = facingRight != synced.LastSentFacingRight;

                if (!moved && !lifeChanged && !facingChanged)
                    continue;

                synced.LastSentPosition = pos;
                synced.LastSentLife = life;
                synced.LastSentAlive = true;
                synced.LastSentFacingRight = facingRight;
                Main.Multiplayer.NetworkManager.SendEnemyState(synced.NetId, pos, life, alive: true, facingRight);
            }
        }

        public bool TryInterceptDamage(Enemy enemy, float amount)
        {
            if (_applyingRemoteDamage || enemy == null)
                return false;
            if (!IsSyncableEnemy(enemy))
                return false;
            if (!Main.Multiplayer.NetworkManager.IsConnected)
                return false;
            if (_isSceneHost)
                return false;

            int netId = GetTrackedNetId(enemy) ?? ComputeStableNetId(enemy);
            Vector2 pos = enemy.transform.position;
            string typeId = enemy.Id ?? string.Empty;
            Main.Multiplayer.NetworkManager.SendEnemyDamage(netId, amount, pos, typeId);
            return true;
        }

        public void ReceiveSnapshot(string senderName, int netId, string enemyTypeId, Vector2 position, float life, bool alive, bool facingRight)
        {
            YieldHostIfPeerIsBroadcasting(senderName);
            if (_isSceneHost)
                return;

            SyncedEnemy synced = BindRemoteEnemy(netId, enemyTypeId, position);
            if (synced == null)
            {
                ModLog.Warn($"[EnemySync] Snapshot miss {netId}/{enemyTypeId} @ {position}");
                RequestSyncFromHost();
                return;
            }

            ApplyRemoteState(synced, position, life, alive, facingRight);
        }

        public void ReceiveState(string senderName, int netId, Vector2 position, float life, bool alive, bool facingRight)
        {
            YieldHostIfPeerIsBroadcasting(senderName);
            if (_isSceneHost)
                return;

            SyncedEnemy synced = BindRemoteEnemy(netId, null, position);
            if (synced == null)
                return;

            ApplyRemoteState(synced, position, life, alive, facingRight);
        }

        public void ReceiveDamage(int netId, float amount, string attackerName, Vector2 hitPos, string enemyTypeId)
        {
            if (!_isSceneHost)
                return;

            SyncedEnemy synced = ResolveDamageTarget(netId, hitPos, enemyTypeId);
            if (synced == null || !synced.IsValid)
            {
                ModLog.Warn($"[EnemySync] Damage miss id={netId} type={enemyTypeId} pos={hitPos} from {attackerName}");
                return;
            }

            // Remember attacker's id as an alias so the next hit resolves immediately
            if (netId != 0 && netId != synced.NetId)
                AliasNetId(netId, synced);

            if (synced.Enemy.Dead)
                return;

            _applyingRemoteDamage = true;
            try
            {
                synced.Enemy.Damage(amount, string.Empty);
            }
            finally
            {
                _applyingRemoteDamage = false;
            }

            BroadcastEnemyAfterHit(synced);
        }

        public void ReceiveDeath(string senderName, int netId)
        {
            YieldHostIfPeerIsBroadcasting(senderName);
            if (_isSceneHost)
                return;

            SyncedEnemy synced = null;
            if (_enemies.TryGetValue(netId, out synced) && synced.IsValid)
            {
                // ok
            }
            else
            {
                synced = BindRemoteEnemy(netId, null, Vector2.zero);
            }

            if (synced == null || !synced.IsValid)
            {
                // Last resort: kill nothing tracked under that id
                ModLog.Warn($"[EnemySync] Death miss {netId}");
                return;
            }

            if (!synced.Enemy.Dead)
            {
                _applyingRemoteDamage = true;
                try { synced.Enemy.Kill(); }
                finally { _applyingRemoteDamage = false; }
            }
            synced.LastSentAlive = false;
        }

        public void ReceiveSyncRequest(string requesterName)
        {
            if (!_isSceneHost)
                return;

            ModLog.Info($"[EnemySync] Sync request from {requesterName}");
            ScanAndRegisterEnemies(forceSnapshot: false);
            SendAllSnapshots();
        }

        public void ForceRescan()
        {
            if (!Main.Multiplayer.CurrentlyInLevel || !Main.Multiplayer.NetworkManager.IsConnected)
                return;
            ScanAndRegisterEnemies(forceSnapshot: _isSceneHost);
            if (!_isSceneHost)
                SuppressUntrackedEnemyAi();
        }

        public void SendAllSnapshots()
        {
            if (!_isSceneHost)
                return;

            int sent = 0;
            foreach (var synced in UniqueEnemies())
            {
                if (!synced.IsValid || synced.Enemy.Dead)
                    continue;

                Vector2 pos = synced.Enemy.transform.position;
                float life = synced.Enemy.CurrentLife;
                bool facingRight = IsFacingRight(synced.Enemy);
                synced.LastSentPosition = pos;
                synced.LastSentLife = life;
                synced.LastSentAlive = true;
                synced.LastSentFacingRight = facingRight;
                Main.Multiplayer.NetworkManager.SendEnemySnapshot(
                    synced.NetId, synced.EnemyTypeId, pos, life, true, facingRight);
                sent++;
            }

            _snapshotLogCooldown++;
            if (sent > 0 && _snapshotLogCooldown % 5 == 1)
                ModLog.Info($"[EnemySync] Snapshots x{sent} (host={_sceneHostName})");
        }

        private SyncedEnemy ResolveDamageTarget(int netId, Vector2 hitPos, string enemyTypeId)
        {
            if (netId != 0 && _enemies.TryGetValue(netId, out SyncedEnemy byId) && byId.IsValid && !byId.Enemy.Dead)
                return byId;

            Enemy found = null;
            if (hitPos.sqrMagnitude > 0.0001f || !string.IsNullOrEmpty(enemyTypeId))
            {
                found = FindNearestLivingEnemy(hitPos, 8f, enemyTypeId);
                if (found == null && !string.IsNullOrEmpty(enemyTypeId))
                    found = FindNearestLivingEnemy(hitPos, 12f, null);
            }

            if (found == null && netId != 0)
                found = FindEnemyByNetId(netId, enemyTypeId, hitPos);

            if (found == null)
                return null;

            int canonical = GetTrackedNetId(found) ?? ComputeStableNetId(found);
            return RegisterEnemy(found, canonical);
        }

        private void AliasNetId(int aliasId, SyncedEnemy synced)
        {
            if (aliasId == 0 || synced == null)
                return;
            if (_enemies.TryGetValue(aliasId, out SyncedEnemy existing) && existing == synced)
                return;

            _enemies[aliasId] = synced;
            ModLog.Info($"[EnemySync] Alias {aliasId} → canonical {synced.NetId} ({synced.EnemyTypeId})");
        }

        private void RequestSyncFromHost()
        {
            if (_isSceneHost || _requestCooldown > 0f)
                return;
            _requestCooldown = 1.0f;
            Main.Multiplayer.NetworkManager.SendEnemySyncRequest();
        }

        private void YieldHostIfPeerIsBroadcasting(string senderName)
        {
            if (!_isSceneHost || string.IsNullOrEmpty(senderName))
                return;
            if (senderName == Main.Multiplayer.PlayerName)
                return;

            ModLog.Info($"[EnemySync] Yielding host to {senderName}");
            _sceneHostName = senderName;
            _isSceneHost = false;
            ApplyAuthorityToAll();
            SuppressUntrackedEnemyAi();
        }

        private void BroadcastEnemyAfterHit(SyncedEnemy synced)
        {
            Vector2 pos = synced.Enemy.transform.position;
            float life = synced.Enemy.CurrentLife;
            bool facingRight = IsFacingRight(synced.Enemy);
            bool alive = !synced.Enemy.Dead;

            synced.LastSentPosition = pos;
            synced.LastSentLife = life;
            synced.LastSentAlive = alive;
            synced.LastSentFacingRight = facingRight;

            if (!alive)
                Main.Multiplayer.NetworkManager.SendEnemyDeath(synced.NetId);
            else
                Main.Multiplayer.NetworkManager.SendEnemyState(synced.NetId, pos, life, true, facingRight);
        }

        private void ClaimOrFollowExistingHost()
        {
            string local = Main.Multiplayer.PlayerName ?? string.Empty;
            var present = GetPlayersInScene();
            bool othersPresent = present.Any(p => p != local);

            if (othersPresent)
            {
                string other = present.Where(p => p != local).OrderBy(p => p, StringComparer.Ordinal).First();
                _sceneHostName = other;
                _isSceneHost = false;
            }
            else
            {
                _sceneHostName = local;
                _isSceneHost = true;
            }
        }

        private void RecalculateHostAfterRosterChange()
        {
            string local = Main.Multiplayer.PlayerName ?? string.Empty;
            var present = GetPlayersInScene();

            if (!string.IsNullOrEmpty(_sceneHostName) && present.Contains(_sceneHostName))
            {
                _isSceneHost = _sceneHostName == local;
                return;
            }

            _sceneHostName = present.OrderBy(p => p, StringComparer.Ordinal).FirstOrDefault() ?? local;
            _isSceneHost = _sceneHostName == local;
        }

        private List<string> GetPlayersInScene()
        {
            var names = new List<string>();
            string local = Main.Multiplayer.PlayerName;
            if (!string.IsNullOrEmpty(local))
                names.Add(local);

            foreach (var other in Main.Multiplayer.OtherPlayerManager.AllActivePlayers)
            {
                if (other == null || string.IsNullOrEmpty(other.name))
                    continue;
                string name = other.name.StartsWith("_") ? other.name.Substring(1) : other.name;
                if (!names.Contains(name))
                    names.Add(name);
            }
            return names;
        }

        private void ApplyAuthorityToAll()
        {
            foreach (var synced in UniqueEnemies())
            {
                if (!synced.IsValid)
                    continue;
                synced.SetPuppet(!_isSceneHost);
            }
        }

        /// <summary>
        /// Non-host: stop AI on enemies we have not received a host id for yet,
        /// without registering a provisional local hash (that caused Damage miss).
        /// </summary>
        private void SuppressUntrackedEnemyAi()
        {
            if (_isSceneHost)
                return;

            Enemy[] found = UnityEngine.Object.FindObjectsOfType<Enemy>();
            foreach (Enemy enemy in found)
            {
                if (!IsSyncableEnemy(enemy) || enemy.Dead)
                    continue;
                if (GetTrackedNetId(enemy).HasValue)
                    continue;

                var behaviour = enemy.EnemyBehaviour;
                if (behaviour == null)
                    continue;
                behaviour.StopBehaviour();
                behaviour.StopMovement();
                behaviour.enabled = false;
            }
        }

        private void ScanAndRegisterEnemies(bool forceSnapshot)
        {
            Enemy[] found = UnityEngine.Object.FindObjectsOfType<Enemy>();
            foreach (Enemy enemy in found)
            {
                if (!IsSyncableEnemy(enemy))
                    continue;

                // Non-host: only refresh already-tracked enemies. New ones wait for host snapshots.
                if (!_isSceneHost)
                {
                    int? tracked = GetTrackedNetId(enemy);
                    if (tracked.HasValue && _enemies.TryGetValue(tracked.Value, out SyncedEnemy existing))
                    {
                        if (existing.Enemy != enemy)
                            existing.Bind(enemy);
                        existing.SetPuppet(true);
                    }
                    continue;
                }

                int netId = GetTrackedNetId(enemy) ?? ComputeStableNetId(enemy);
                if (_enemies.TryGetValue(netId, out SyncedEnemy hostExisting))
                {
                    if (hostExisting.Enemy != enemy)
                        hostExisting.Bind(enemy);
                    hostExisting.SetPuppet(false);
                    continue;
                }

                RegisterEnemy(enemy, netId);
            }

            var stale = new List<int>();
            foreach (var kv in _enemies)
            {
                if (!kv.Value.IsValid)
                    stale.Add(kv.Key);
            }
            foreach (int id in stale)
                _enemies.Remove(id);

            if (!_isSceneHost)
                SuppressUntrackedEnemyAi();

            if (forceSnapshot && _isSceneHost)
                SendAllSnapshots();
        }

        private SyncedEnemy RegisterEnemy(Enemy enemy, int netId)
        {
            var orphanKeys = new List<int>();
            foreach (var kv in _enemies)
            {
                if (kv.Key != netId && kv.Value.IsValid && kv.Value.Enemy == enemy)
                    orphanKeys.Add(kv.Key);
            }
            foreach (int key in orphanKeys)
            {
                ModLog.Info($"[EnemySync] Re-key remove {key} → {netId}");
                _enemies.Remove(key);
            }

            if (_enemies.TryGetValue(netId, out SyncedEnemy existing))
            {
                existing.Bind(enemy);
                existing.SetPuppet(!_isSceneHost);
                return existing;
            }

            var synced = new SyncedEnemy(netId, enemy.Id ?? string.Empty, enemy);
            synced.SetPuppet(!_isSceneHost);
            _enemies[netId] = synced;
            ModLog.Info($"[EnemySync] Register {netId} {synced.EnemyTypeId} puppet={synced.IsPuppet}");
            return synced;
        }

        private SyncedEnemy BindRemoteEnemy(int netId, string enemyTypeId, Vector2 position)
        {
            if (_enemies.TryGetValue(netId, out SyncedEnemy synced) && synced.IsValid)
            {
                synced.SetPuppet(true);
                return synced;
            }

            Enemy found = FindEnemyByNetId(netId, enemyTypeId, position);
            if (found == null)
                found = FindNearestLivingEnemy(position, 10f, enemyTypeId);
            if (found == null && !string.IsNullOrEmpty(enemyTypeId))
                found = FindNearestLivingEnemy(position, 14f, null);

            if (found == null)
                return null;

            return RegisterEnemy(found, netId);
        }

        private Enemy FindNearestLivingEnemy(Vector2 hintPos, float maxDist, string enemyTypeId)
        {
            Enemy[] found = UnityEngine.Object.FindObjectsOfType<Enemy>();
            Enemy best = null;
            float bestDist = maxDist;

            foreach (Enemy enemy in found)
            {
                if (!IsSyncableEnemy(enemy) || enemy.Dead)
                    continue;
                if (!string.IsNullOrEmpty(enemyTypeId) && enemy.Id != enemyTypeId)
                    continue;

                float d = Vector2.Distance(enemy.transform.position, hintPos);
                if (d <= bestDist)
                {
                    bestDist = d;
                    best = enemy;
                }
            }

            return best;
        }

        private void ApplyRemoteState(SyncedEnemy synced, Vector2 position, float life, bool alive, bool facingRight)
        {
            if (!synced.IsValid)
                return;

            synced.SetPuppet(true);
            synced.Enemy.transform.position = new Vector3(position.x, position.y, synced.Enemy.transform.position.z);

            if (synced.Enemy.Status != null)
                synced.Enemy.Status.Orientation = facingRight ? EntityOrientation.Right : EntityOrientation.Left;

            if (synced.Enemy.SpriteRenderer != null)
                synced.Enemy.SpriteRenderer.flipX = !facingRight;

            if (!alive)
            {
                if (!synced.Enemy.Dead)
                {
                    _applyingRemoteDamage = true;
                    try { synced.Enemy.Kill(); }
                    finally { _applyingRemoteDamage = false; }
                }
                synced.LastSentAlive = false;
                return;
            }

            if (Mathf.Abs(synced.Enemy.CurrentLife - life) > 0.1f)
                synced.Enemy.CurrentLife = life;

            synced.LastSentPosition = position;
            synced.LastSentLife = life;
            synced.LastSentAlive = true;
            synced.LastSentFacingRight = facingRight;
        }

        private Enemy FindEnemyByNetId(int netId, string enemyTypeId, Vector2 hintPos)
        {
            Enemy[] found = UnityEngine.Object.FindObjectsOfType<Enemy>();
            Enemy best = null;
            float bestDist = float.MaxValue;
            bool hasHint = hintPos.sqrMagnitude > 0.0001f || !string.IsNullOrEmpty(enemyTypeId);

            foreach (Enemy enemy in found)
            {
                if (!IsSyncableEnemy(enemy))
                    continue;

                if (ComputeStableNetId(enemy) == netId)
                    return enemy;

                foreach (var s in UniqueEnemies())
                {
                    if (s.Enemy == enemy && s.NetId == netId)
                        return enemy;
                }

                if (!hasHint)
                    continue;
                if (!string.IsNullOrEmpty(enemyTypeId) && enemy.Id != enemyTypeId)
                    continue;

                float d = Vector2.Distance(enemy.transform.position, hintPos);
                if (d < bestDist)
                {
                    bestDist = d;
                    best = enemy;
                }
            }

            return bestDist < 12f ? best : null;
        }

        private int? GetTrackedNetId(Enemy enemy)
        {
            foreach (var kv in _enemies)
            {
                if (kv.Value.IsValid && kv.Value.Enemy == enemy)
                    return kv.Value.NetId; // canonical id on the SyncedEnemy, not the alias key
            }
            return null;
        }

        private IEnumerable<SyncedEnemy> UniqueEnemies()
        {
            return _enemies.Values.Distinct();
        }

        public static int ComputeStableNetId(Enemy enemy)
        {
            if (enemy == null)
                return 0;

            if (enemy.SpawningId != 0)
                return enemy.SpawningId;

            var spawn = enemy.GetComponentInParent<EnemySpawnPoint>();
            if (spawn != null)
            {
                if (spawn.SpawningId != 0)
                    return spawn.SpawningId;

                Vector3 p = spawn.Position;
                return StableHash($"{enemy.Id}|sp|{Round(p.x)}|{Round(p.y)}");
            }

            string goName = enemy.gameObject != null ? enemy.gameObject.name : "unk";
            // Strip Unity's "(Clone)" / instance suffixes for better cross-client match
            int cloneIdx = goName.IndexOf("(Clone)", StringComparison.Ordinal);
            if (cloneIdx >= 0)
                goName = goName.Substring(0, cloneIdx).Trim();

            return StableHash($"{enemy.Id}|go|{goName}");
        }

        public static int ResolveNetId(Enemy enemy) => ComputeStableNetId(enemy);

        private static float Round(float v) => Mathf.Round(v * 2f) / 2f;

        private void Clear()
        {
            _enemies.Clear();
            _scanTimer = 0f;
            _snapshotTimer = 0f;
            _requestCooldown = 0f;
            _snapshotLogCooldown = 0;
            _levelAge = 0f;
            _deferredScanIndex = 0;
        }

        private static bool IsFacingRight(Enemy enemy)
        {
            return enemy.Status == null || enemy.Status.Orientation == EntityOrientation.Right;
        }

        public static bool IsSyncableEnemy(Enemy enemy)
        {
            if (enemy == null || enemy.gameObject == null)
                return false;
            if (string.IsNullOrEmpty(enemy.Id))
                return false;
            if (enemy.Id == "EV09")
                return false;

            Type t = enemy.GetType();
            string ns = t.Namespace ?? string.Empty;
            if (ns.StartsWith("Gameplay.GameControllers.Bosses", StringComparison.Ordinal))
                return false;
            if (t.Name.IndexOf("Boss", StringComparison.OrdinalIgnoreCase) >= 0)
                return false;

            return true;
        }

        private static int StableHash(string value)
        {
            unchecked
            {
                int hash = 23;
                byte[] bytes = Encoding.UTF8.GetBytes(value);
                for (int i = 0; i < bytes.Length; i++)
                    hash = hash * 31 + bytes[i];
                return hash == 0 ? 1 : hash;
            }
        }
    }
}

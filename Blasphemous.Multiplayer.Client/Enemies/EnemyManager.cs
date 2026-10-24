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
    /// Host-authoritative room-enemy sync for the current scene.
    /// Scene host = first player who entered this scene (sticky). They run AI; others puppet.
    /// </summary>
    public class EnemyManager
    {
        private readonly Dictionary<int, SyncedEnemy> _enemies = new();
        private readonly float _positionCutoff = 0.05f;
        private readonly float _lifeCutoff = 0.5f;
        private readonly float _scanInterval = 0.35f;
        private readonly float _snapshotInterval = 1.0f;
        private float _scanTimer;
        private float _snapshotTimer;
        private bool _isSceneHost;
        private string _currentScene = string.Empty;
        private string _sceneHostName = string.Empty;
        private bool _applyingRemoteDamage;

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

            if (!Main.Multiplayer.CurrentlyInLevel || !Main.Multiplayer.NetworkManager.IsConnected)
                return;

            // Sticky host: if someone is already in this scene, they keep host; else we claim it.
            ClaimOrFollowExistingHost();
            ScanAndRegisterEnemies(forceSnapshot: _isSceneHost);
            ModLog.Info($"[EnemySync] Level {scene} — sceneHost={_isSceneHost} (host={_sceneHostName}), tracked={_enemies.Count}");
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
            foreach (var synced in _enemies.Values)
            {
                if (synced.IsValid && synced.IsPuppet)
                    synced.SetPuppet(false);
            }
            Clear();
            _isSceneHost = false;
            _sceneHostName = string.Empty;
        }

        /// <summary>Called when another player enters/leaves this scene.</summary>
        public void OnPlayersInSceneChanged()
        {
            if (!Main.Multiplayer.CurrentlyInLevel || !Main.Multiplayer.NetworkManager.IsConnected)
                return;

            bool wasHost = _isSceneHost;
            RecalculateHostAfterRosterChange();
            ApplyAuthorityToAll();

            if (wasHost != _isSceneHost)
                ModLog.Info($"[EnemySync] Host changed → {_isSceneHost} (host={_sceneHostName})");

            if (_isSceneHost)
            {
                ScanAndRegisterEnemies(forceSnapshot: true);
                SendAllSnapshots();
            }
        }

        public void Update()
        {
            if (!Main.Multiplayer.CurrentlyInLevel || !Main.Multiplayer.NetworkManager.IsConnected)
                return;

            _scanTimer -= Time.deltaTime;
            if (_scanTimer <= 0f)
            {
                _scanTimer = _scanInterval;
                ScanAndRegisterEnemies(forceSnapshot: false);
            }

            if (!_isSceneHost)
                return;

            _snapshotTimer -= Time.deltaTime;
            if (_snapshotTimer <= 0f)
            {
                _snapshotTimer = _snapshotInterval;
                SendAllSnapshots();
            }

            foreach (var synced in _enemies.Values)
            {
                if (!synced.IsValid)
                    continue;

                if (synced.Enemy.Dead)
                {
                    if (synced.LastSentAlive)
                    {
                        synced.LastSentAlive = false;
                        ModLog.Info($"[EnemySync] Send death {synced.NetId}");
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

            // Host applies locally; Update/death broadcast will sync
            if (_isSceneHost)
                return false;

            int netId = ResolveNetId(enemy);
            if (!_enemies.ContainsKey(netId))
                RegisterEnemy(enemy, sendSnapshot: false);

            ModLog.Info($"[EnemySync] Forward damage {amount} → host for {netId}");
            Main.Multiplayer.NetworkManager.SendEnemyDamage(netId, amount);
            return true;
        }

        public void ReceiveSnapshot(int netId, string enemyTypeId, Vector2 position, float life, bool alive, bool facingRight)
        {
            if (_isSceneHost)
                return;

            SyncedEnemy synced = BindRemoteEnemy(netId, enemyTypeId, position);
            if (synced == null)
            {
                ModLog.Warn($"[EnemySync] Snapshot miss id={netId} type={enemyTypeId} at {position}");
                return;
            }

            ApplyRemoteState(synced, position, life, alive, facingRight);
        }

        public void ReceiveState(int netId, Vector2 position, float life, bool alive, bool facingRight)
        {
            if (_isSceneHost)
                return;

            SyncedEnemy synced = BindRemoteEnemy(netId, null, position);
            if (synced == null)
                return;

            ApplyRemoteState(synced, position, life, alive, facingRight);
        }

        public void ReceiveDamage(int netId, float amount, string attackerName)
        {
            if (!_isSceneHost)
                return;
            if (!_enemies.TryGetValue(netId, out SyncedEnemy synced) || !synced.IsValid || synced.Enemy.Dead)
            {
                ModLog.Warn($"[EnemySync] Damage for unknown/dead {netId} from {attackerName}");
                return;
            }

            ModLog.Info($"[EnemySync] Apply remote damage {amount} on {netId} from {attackerName}");
            _applyingRemoteDamage = true;
            try
            {
                synced.Enemy.Damage(amount, string.Empty);
            }
            finally
            {
                _applyingRemoteDamage = false;
            }

            Vector2 pos = synced.Enemy.transform.position;
            float life = synced.Enemy.CurrentLife;
            bool facingRight = IsFacingRight(synced.Enemy);
            bool alive = !synced.Enemy.Dead;

            synced.LastSentPosition = pos;
            synced.LastSentLife = life;
            synced.LastSentAlive = alive;
            synced.LastSentFacingRight = facingRight;

            if (!alive)
            {
                ModLog.Info($"[EnemySync] Send death {netId}");
                Main.Multiplayer.NetworkManager.SendEnemyDeath(netId);
            }
            else
            {
                Main.Multiplayer.NetworkManager.SendEnemyState(netId, pos, life, true, facingRight);
            }
        }

        public void ReceiveDeath(int netId)
        {
            if (_isSceneHost)
                return;
            if (!_enemies.TryGetValue(netId, out SyncedEnemy synced) || !synced.IsValid)
            {
                // Try bind by id alone among living
                Enemy found = FindEnemyByNetId(netId, null, Vector2.zero);
                if (found == null)
                    return;
                synced = RegisterEnemy(found, sendSnapshot: false);
            }

            if (!synced.Enemy.Dead)
            {
                ModLog.Info($"[EnemySync] Apply remote death {netId}");
                _applyingRemoteDamage = true;
                try { synced.Enemy.Kill(); }
                finally { _applyingRemoteDamage = false; }
            }
            synced.LastSentAlive = false;
        }

        public void ForceRescan()
        {
            if (!Main.Multiplayer.CurrentlyInLevel || !Main.Multiplayer.NetworkManager.IsConnected)
                return;
            ScanAndRegisterEnemies(forceSnapshot: _isSceneHost);
        }

        public void SendAllSnapshots()
        {
            if (!_isSceneHost)
                return;

            int sent = 0;
            foreach (var synced in _enemies.Values)
            {
                if (!synced.IsValid || synced.Enemy.Dead)
                    continue;

                Vector2 pos = synced.Enemy.transform.position;
                float life = synced.Enemy.CurrentLife;
                bool facingRight = IsFacingRight(synced.Enemy);
                Main.Multiplayer.NetworkManager.SendEnemySnapshot(
                    synced.NetId, synced.EnemyTypeId, pos, life, true, facingRight);
                sent++;
            }
            if (sent > 0)
                ModLog.Info($"[EnemySync] Sent {sent} snapshots");
        }

        private void ClaimOrFollowExistingHost()
        {
            // If another active player is already here, they are host (first one we know about by name for stability among present).
            // Sticky: the lexicographically first player *currently in the scene* including us — BUT we prefer
            // keeping an already-assigned host if they are still present.
            string local = Main.Multiplayer.PlayerName ?? string.Empty;
            var present = GetPlayersInScene();
            if (present.Count == 0)
            {
                _sceneHostName = local;
                _isSceneHost = true;
                return;
            }

            // Prefer existing host if still present
            if (!string.IsNullOrEmpty(_sceneHostName) && present.Contains(_sceneHostName))
            {
                _isSceneHost = _sceneHostName == local;
                return;
            }

            // First entrant wins: among present, pick the one who has been "scene host" — we approximate
            // by choosing lexicographically first ONLY when claiming fresh. When we just entered and others
            // are present, we are NOT host (they were here first / or we defer to alphabetical among them only).
            bool othersPresent = present.Any(p => p != local);
            if (othersPresent)
            {
                // Someone else is already in the scene — they own it. Pick deterministic host among them (not us).
                string otherHost = present.Where(p => p != local).OrderBy(p => p, StringComparer.Ordinal).First();
                _sceneHostName = otherHost;
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

            // If current host left, elect sticky replacement: first alphabetical among remaining (including us)
            if (string.IsNullOrEmpty(_sceneHostName) || !present.Contains(_sceneHostName))
            {
                _sceneHostName = present.OrderBy(p => p, StringComparer.Ordinal).FirstOrDefault() ?? local;
            }

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
            foreach (var synced in _enemies.Values)
            {
                if (!synced.IsValid)
                    continue;
                synced.SetPuppet(!_isSceneHost);
            }
        }

        private void ScanAndRegisterEnemies(bool forceSnapshot)
        {
            Enemy[] found = UnityEngine.Object.FindObjectsOfType<Enemy>();
            foreach (Enemy enemy in found)
            {
                if (!IsSyncableEnemy(enemy))
                    continue;

                int netId = ResolveNetId(enemy);
                if (_enemies.ContainsKey(netId))
                {
                    if (_enemies[netId].Enemy != enemy)
                        _enemies[netId].Bind(enemy);
                    _enemies[netId].SetPuppet(!_isSceneHost);
                    continue;
                }

                RegisterEnemy(enemy, sendSnapshot: false);
            }

            var stale = new List<int>();
            foreach (var kv in _enemies)
            {
                if (!kv.Value.IsValid)
                    stale.Add(kv.Key);
            }
            foreach (int id in stale)
                _enemies.Remove(id);

            if (forceSnapshot && _isSceneHost)
                SendAllSnapshots();
        }

        private SyncedEnemy RegisterEnemy(Enemy enemy, bool sendSnapshot)
        {
            int netId = ResolveNetId(enemy);
            if (_enemies.TryGetValue(netId, out SyncedEnemy existing))
            {
                existing.Bind(enemy);
                existing.SetPuppet(!_isSceneHost);
                return existing;
            }

            var synced = new SyncedEnemy(netId, enemy.Id ?? string.Empty, enemy);
            synced.SetPuppet(!_isSceneHost);
            _enemies[netId] = synced;
            ModLog.Info($"[EnemySync] Register {netId} type={synced.EnemyTypeId} puppet={synced.IsPuppet}");

            if (sendSnapshot && _isSceneHost && !enemy.Dead)
            {
                Main.Multiplayer.NetworkManager.SendEnemySnapshot(
                    netId, synced.EnemyTypeId, enemy.transform.position, enemy.CurrentLife, true, IsFacingRight(enemy));
            }

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
                return null;

            return RegisterEnemy(found, sendSnapshot: false);
        }

        private void ApplyRemoteState(SyncedEnemy synced, Vector2 position, float life, bool alive, bool facingRight)
        {
            if (!synced.IsValid)
                return;

            if (!synced.IsPuppet)
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

            foreach (Enemy enemy in found)
            {
                if (!IsSyncableEnemy(enemy))
                    continue;

                if (ResolveNetId(enemy) == netId)
                    return enemy;

                if (!string.IsNullOrEmpty(enemyTypeId) && enemy.Id == enemyTypeId)
                {
                    float d = Vector2.Distance(enemy.transform.position, hintPos);
                    if (d < bestDist)
                    {
                        bestDist = d;
                        best = enemy;
                    }
                }
                else if (string.IsNullOrEmpty(enemyTypeId))
                {
                    float d = Vector2.Distance(enemy.transform.position, hintPos);
                    if (d < bestDist)
                    {
                        bestDist = d;
                        best = enemy;
                    }
                }
            }

            // Generous match — spawn layouts can differ slightly
            return bestDist < 8f ? best : null;
        }

        private void Clear()
        {
            _enemies.Clear();
            _scanTimer = 0f;
            _snapshotTimer = 0f;
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

        public static int ResolveNetId(Enemy enemy)
        {
            if (enemy == null)
                return 0;

            if (enemy.SpawningId != 0)
                return enemy.SpawningId;

            // Prefer EnemySpawnPoint id when present
            var spawn = enemy.GetComponentInParent<EnemySpawnPoint>();
            if (spawn != null && spawn.SpawningId != 0)
                return spawn.SpawningId;

            // Deterministic fallback: type + rounded spawn position (same on all clients for scene-placed enemies)
            Vector3 p = enemy.transform.position;
            string key = $"{enemy.Id}|{Mathf.RoundToInt(p.x * 2f) / 2f}|{Mathf.RoundToInt(p.y * 2f) / 2f}";
            return StableHash(key);
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

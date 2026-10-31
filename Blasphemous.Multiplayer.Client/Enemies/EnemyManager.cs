using Blasphemous.ModdingAPI;
using Blasphemous.Multiplayer.Client.Players;
using Blasphemous.Multiplayer.Common.Combat;
using Framework.FrameworkCore;
using Framework.Managers;
using Gameplay.GameControllers.Enemies.Framework.Attack;
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
    /// Host-authoritative room enemies and shared boss health.
    /// Damage uses canonical snapshot IDs and replays the game's complete Hit path.
    /// </summary>
    public class EnemyManager
    {
        private readonly Dictionary<int, SyncedEnemy> _enemies = new();
        private readonly Dictionary<Enemy, SyncedEnemy> _unboundEnemies = new();
        /// <summary>Deaths we could not apply yet (enemy not spawned / not bound). Retried on scan.</summary>
        private readonly Dictionary<int, PendingDeath> _pendingDeaths = new();
        /// <summary>Per-player cooldown so one enemy swing doesn't multi-hit the remote avatar.</summary>
        private readonly Dictionary<string, float> _remoteHitCooldown = new();
        private readonly float _positionCutoff = 0.04f;
        private readonly float _lifeCutoff = 0.25f;
        private readonly float _scanInterval = 0.25f;
        private readonly float _snapshotInterval = 0.35f;
        private readonly float _retargetInterval = 0.1f;
        private float _retargetTimer;
        private readonly float _remoteHitCooldownSeconds = 0.4f;
        private readonly float _aggroStickyBonus = 2.5f;
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
        private GameObject _hitSource;

        private struct PendingDeath
        {
            public string TypeId;
            public Vector2 Position;
        }

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
            foreach (var synced in _enemies.Values.Concat(_unboundEnemies.Values).Distinct())
            {
                if (synced.IsValid && synced.IsPuppet)
                    synced.SetPuppet(false);
                if (synced.IsValid && !synced.IsBoss && !synced.Enemy.Dead && Core.Logic.Penitent != null)
                    synced.Enemy.Target = Core.Logic.Penitent.gameObject;
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

            TickRemoteHitCooldowns();
            TickDeferredDeathHides();

            if (!_isSceneHost)
                return;

            // One shared AI: retarget + contact pulses for whoever is in range
            _retargetTimer -= Time.deltaTime;
            if (_retargetTimer <= 0f)
            {
                _retargetTimer = _retargetInterval;
                RetargetEnemiesToNearestPlayer();
            }

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
                        Vector2 deathPos = synced.LastSentPosition.sqrMagnitude > 0.0001f
                            ? synced.LastSentPosition
                            : (Vector2)synced.Enemy.transform.position;
                        ModLog.Info($"[EnemySync] Death {synced.NetId} ({synced.EnemyTypeId}) @ {deathPos}");
                        Main.Multiplayer.NetworkManager.SendEnemyDeath(synced.NetId, deathPos, synced.EnemyTypeId);
                    }
                    continue;
                }

                Vector2 pos = synced.Enemy.transform.position;
                float life = synced.Enemy.CurrentLife;
                bool facingRight = IsFacingRight(synced.Enemy);
                int animHash = synced.ReadAnimHash();
                float animTime = synced.ReadAnimTime();
                bool attacking = synced.Enemy.IsAttacking;

                bool moved = Mathf.Abs(pos.x - synced.LastSentPosition.x) > _positionCutoff
                    || Mathf.Abs(pos.y - synced.LastSentPosition.y) > _positionCutoff;
                bool lifeChanged = Mathf.Abs(life - synced.LastSentLife) > _lifeCutoff;
                bool facingChanged = facingRight != synced.LastSentFacingRight;
                bool animChanged = animHash != 0 && animHash != synced.LastSentAnimHash;
                // While attacking, also push when swing progress drifts so puppets animate
                bool animTimeChanged = attacking
                    && Mathf.Abs(animTime - synced.LastSentAnimTime) > 0.08f;

                if (!moved && !lifeChanged && !facingChanged && !animChanged && !animTimeChanged)
                    continue;

                synced.LastSentPosition = pos;
                synced.LastSentLife = life;
                synced.LastSentAlive = true;
                synced.LastSentFacingRight = facingRight;
                synced.LastSentAnimHash = animHash;
                synced.LastSentAnimTime = animTime;
                Main.Multiplayer.NetworkManager.SendEnemyState(
                    synced.NetId, pos, life, alive: true, facingRight, animHash, animTime);
            }
        }

        /// <summary>
        /// Host: enemy contact hit the remote player's avatar — tell their client to take HP damage.
        /// </summary>
        public void NotifyRemotePlayerHitByEnemy(string playerName, Hit hit)
        {
            if (!_isSceneHost || string.IsNullOrEmpty(playerName) || playerName == Main.Multiplayer.PlayerName)
                return;
            EnemyHitData data = CombatHit.Capture(hit);
            if (!data.IsValid || (_remoteHitCooldown.TryGetValue(playerName, out float left) && left > 0f))
                return;
            _remoteHitCooldown[playerName] = _remoteHitCooldownSeconds;
            Main.Multiplayer.NetworkManager.SendEnemyPlayerHit(playerName, data);
        }

        public void ReceiveEnemyPlayerHit(string senderName, string receiverName, EnemyHitData data)
        {
            if (!IsAuthoritySender(senderName) || receiverName != Main.Multiplayer.PlayerName || !data.IsValid)
                return;
            if (Core.Logic.Penitent == null || Core.Logic.Penitent.Dead)
                return;
            // The game keeps its normal guard, parry, dodge and damage reduction rules.
            Core.Logic.Penitent.Damage(CombatHit.Restore(data, GetHitSource(data)));
        }

        private GameObject GetHitSource(EnemyHitData data)
        {
            if (_hitSource == null)
                _hitSource = new GameObject("Multiplayer hit source");
            _hitSource.transform.position = new Vector3(data.SourceX, data.SourceY, 0f);
            return _hitSource;
        }

        private bool IsAuthoritySender(string name)
        {
            return !_isSceneHost && Main.Multiplayer.CurrentlyInLevel
                && Main.Multiplayer.NetworkManager.IsConnected && name == _sceneHostName
                && !string.IsNullOrEmpty(name);
        }

        private void TickRemoteHitCooldowns()
        {
            if (_remoteHitCooldown.Count == 0)
                return;
            var keys = _remoteHitCooldown.Keys.ToList();
            foreach (string key in keys)
            {
                float next = _remoteHitCooldown[key] - Time.deltaTime;
                if (next <= 0f)
                    _remoteHitCooldown.Remove(key);
                else
                    _remoteHitCooldown[key] = next;
            }
        }

        private void RetargetEnemiesToNearestPlayer()
        {
            var candidates = CollectPlayerTargets();
            if (candidates.Count == 0)
                return;

            foreach (var synced in UniqueEnemies())
            {
                if (!synced.IsValid || synced.IsBoss || synced.Enemy.Dead || synced.IsPuppet)
                    continue;

                GameObject best = PickNearestTarget(synced.Enemy.transform.position, candidates, synced.Enemy.Target);
                if (best != null && synced.Enemy.Target != best)
                {
                    synced.Enemy.Target = best;
                    ModLog.Info($"[EnemySync] Retarget {synced.NetId} → {best.name}");
                }
            }
        }

        private void TickDeferredDeathHides()
        {
            foreach (var synced in UniqueEnemies())
            {
                if (synced != null)
                    synced.TickDeferredHide();
            }
        }

        private List<GameObject> CollectPlayerTargets()
        {
            var list = new List<GameObject>(4);
            if (Core.Logic.Penitent != null && !Core.Logic.Penitent.Dead)
                list.Add(Core.Logic.Penitent.gameObject);

            foreach (OtherPlayerScript other in Main.Multiplayer.OtherPlayerManager.AllActivePlayers)
            {
                if (other == null || other.IsDead || !other.gameObject.activeInHierarchy)
                    continue;
                list.Add(other.gameObject);
            }
            return list;
        }

        private GameObject PickNearestTarget(Vector2 from, List<GameObject> candidates, GameObject current)
        {
            GameObject best = null;
            float bestDist = float.MaxValue;
            foreach (GameObject go in candidates)
            {
                if (go == null)
                    continue;
                float d = Vector2.Distance(from, go.transform.position);
                // Sticky aggro: prefer current target slightly so AI doesn't flicker
                if (current != null && go == current)
                    d -= _aggroStickyBonus;
                if (d < bestDist)
                {
                    bestDist = d;
                    best = go;
                }
            }
            return best;
        }

        public bool TryInterceptDamage(Enemy enemy, float amount)
        {
            return TryForwardHit(enemy, new EnemyHitData { Damage = amount, DirectDamage = true });
        }

        public bool TryInterceptHit(Enemy enemy, Hit hit)
        {
            return TryForwardHit(enemy, CombatHit.Capture(hit));
        }

        public bool ShouldSuppressEnemyAI(Enemy enemy) => !_isSceneHost && IsSyncableEnemy(enemy)
            && !IsBossEnemy(enemy) && Main.Multiplayer.CurrentlyInLevel && Main.Multiplayer.NetworkManager.IsConnected;

        private bool TryForwardHit(Enemy enemy, EnemyHitData hit)
        {
            if (_applyingRemoteDamage || _isSceneHost || !IsSyncableEnemy(enemy)
                || !Main.Multiplayer.NetworkManager.IsConnected || !Main.Multiplayer.CurrentlyInLevel || !hit.IsValid)
                return false;
            // Never guess a moving enemy from position. Wait for its canonical host ID.
            int? id = GetTrackedNetId(enemy);
            if (id.HasValue)
                Main.Multiplayer.NetworkManager.SendEnemyDamage(id.Value, hit, enemy.transform.position, GetEnemyTypeId(enemy));
            else
                RequestSyncFromHost();
            return true;
        }

        public void ReceiveSnapshot(string senderName, int netId, string enemyTypeId, Vector2 position, float life, bool alive, bool facingRight, int animHash, float animTime)
        {
            if (!IsAuthoritySender(senderName) || !EnemyHitData.Finite(life)
                || !EnemyHitData.Finite(position.x) || !EnemyHitData.Finite(position.y))
                return;
            if (_enemies.TryGetValue(netId, out SyncedEnemy previous) && !previous.IsValid && !previous.LastSentAlive)
                return;

            SyncedEnemy synced = BindRemoteEnemy(netId, enemyTypeId, position, allowProximity: alive && life > 0f);
            if (synced == null)
            {
                if (!alive)
                {
                    if (!_pendingDeaths.ContainsKey(netId))
                        ModLog.Info($"[EnemySync] Waiting for dead enemy {netId}/{enemyTypeId} to spawn");
                    _pendingDeaths[netId] = new PendingDeath { TypeId = enemyTypeId ?? string.Empty, Position = position };
                    return;
                }
                else
                {
                    ModLog.Warn($"[EnemySync] Snapshot miss {netId}/{enemyTypeId} @ {position}");
                }
                RequestSyncFromHost();
                return;
            }

            ApplyRemoteState(synced, position, life, alive, facingRight, animHash, animTime);
            if (!alive || life <= 0.01f)
                _pendingDeaths.Remove(netId);
        }

        public void ReceiveState(string senderName, int netId, Vector2 position, float life, bool alive, bool facingRight, int animHash, float animTime)
        {
            if (!IsAuthoritySender(senderName) || !EnemyHitData.Finite(life)
                || !EnemyHitData.Finite(position.x) || !EnemyHitData.Finite(position.y))
                return;
            if (!_enemies.TryGetValue(netId, out SyncedEnemy synced))
            {
                RequestSyncFromHost();
                return;
            }
            if (synced.IsValid)
                ApplyRemoteState(synced, position, life, alive, facingRight, animHash, animTime);
        }

        public void ReceiveDamage(int netId, EnemyHitData data, string attackerName, Vector2 hitPos, string enemyTypeId)
        {
            if (!_isSceneHost || !data.IsValid || !GetPlayersInScene().Contains(attackerName))
                return;
            if (!_enemies.TryGetValue(netId, out SyncedEnemy synced) || !synced.IsValid || synced.Enemy.Dead
                || synced.EnemyTypeId != enemyTypeId || !(synced.Enemy is IDamageable damageable))
                return;
            _applyingRemoteDamage = true;
            try
            {
                if (data.DirectDamage)
                    synced.Enemy.Damage(data.Damage, string.Empty);
                else
                    damageable.Damage(CombatHit.Restore(data, GetHitSource(data)));
            }
            finally
            {
                _applyingRemoteDamage = false;
            }
            BroadcastEnemyAfterHit(synced);
        }

        public void ReceiveDeath(string senderName, int netId, Vector2 deathPos, string enemyTypeId)
        {
            if (!IsAuthoritySender(senderName))
                return;

            if (ApplyDeathToLocal(netId, deathPos, enemyTypeId))
                return;

            _pendingDeaths[netId] = new PendingDeath
            {
                TypeId = enemyTypeId ?? string.Empty,
                Position = deathPos
            };
            ModLog.Warn($"[EnemySync] Death pending {netId}/{enemyTypeId} @ {deathPos}");
            RequestSyncFromHost();
        }

        private bool ApplyDeathToLocal(int netId, Vector2 deathPos, string enemyTypeId)
        {
            SyncedEnemy synced = null;
            if (_enemies.TryGetValue(netId, out synced) && !synced.IsValid && !synced.LastSentAlive)
                return true;
            if (_enemies.TryGetValue(netId, out synced) && synced.IsValid)
            {
                // already bound
            }
            else
            {
                synced = BindRemoteEnemy(netId, enemyTypeId, deathPos, allowProximity: false);
            }

            if (synced == null || !synced.IsValid)
                return false;

            ForceKill(synced);
            _pendingDeaths.Remove(netId);
            return true;
        }

        private void ForceKill(SyncedEnemy synced)
        {
            if (synced == null || !synced.IsValid)
                return;

            if (synced.IsBoss)
            {
                ApplyBossHealth(synced, 0f, false);
                return;
            }
            _applyingRemoteDamage = true;
            bool newlyApplied;
            try
            {
                if (!synced.Enemy.Dead && synced.Enemy is IDamageable damageable && Core.Logic.Penitent != null)
                    damageable.Damage(CreateLethalHit(synced.Enemy));
                newlyApplied = synced.ForceVisualDeath();
            }
            finally
            {
                _applyingRemoteDamage = false;
            }

            if (newlyApplied)
                ModLog.Info($"[EnemySync] Applied death {synced.NetId} ({synced.EnemyTypeId})");
        }

        private void ApplyPendingDeaths()
        {
            if (_isSceneHost || _pendingDeaths.Count == 0)
                return;

            var ids = _pendingDeaths.Keys.ToList();
            foreach (int netId in ids)
            {
                PendingDeath pending = _pendingDeaths[netId];
                ApplyDeathToLocal(netId, pending.Position, pending.TypeId);
            }
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

        public void QueueRescan() => _scanTimer = 0f;

        public void SendAllSnapshots()
        {
            if (!_isSceneHost)
                return;

            int sent = 0;
            int deadSent = 0;
            foreach (var synced in UniqueEnemies())
            {
                if (!synced.IsValid)
                {
                    Main.Multiplayer.NetworkManager.SendEnemySnapshot(synced.NetId, synced.EnemyTypeId,
                        synced.LastSentPosition, 0f, false, synced.LastSentFacingRight, 0, 0f);
                    continue;
                }

                bool alive = !synced.Enemy.Dead && !synced.VisuallyDead;
                Vector2 pos = alive
                    ? (Vector2)synced.Enemy.transform.position
                    : (synced.LastSentPosition.sqrMagnitude > 0.0001f
                        ? synced.LastSentPosition
                        : (Vector2)synced.Enemy.transform.position);
                float life = alive ? synced.Enemy.CurrentLife : 0f;
                bool facingRight = IsFacingRight(synced.Enemy);
                int animHash = alive ? synced.ReadAnimHash() : 0;
                float animTime = alive ? synced.ReadAnimTime() : 0f;
                synced.LastSentPosition = pos;
                synced.LastSentLife = life;
                synced.LastSentAlive = alive;
                synced.LastSentFacingRight = facingRight;
                synced.LastSentAnimHash = animHash;
                synced.LastSentAnimTime = animTime;
                Main.Multiplayer.NetworkManager.SendEnemySnapshot(
                    synced.NetId, synced.EnemyTypeId, pos, life, alive, facingRight, animHash, animTime);
                sent++;
                if (!alive)
                    deadSent++;
            }

            _snapshotLogCooldown++;
            if (sent > 0 && _snapshotLogCooldown % 5 == 1)
                ModLog.Info($"[EnemySync] Snapshots x{sent} (dead={deadSent}, host={_sceneHostName})");
        }

        private void RequestSyncFromHost()
        {
            if (_isSceneHost || _requestCooldown > 0f)
                return;
            _requestCooldown = 0.4f;
            Main.Multiplayer.NetworkManager.SendEnemySyncRequest();
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
                Main.Multiplayer.NetworkManager.SendEnemyDeath(synced.NetId, pos, synced.EnemyTypeId);
            else
                Main.Multiplayer.NetworkManager.SendEnemyState(
                    synced.NetId, pos, life, true, facingRight, synced.ReadAnimHash(), synced.ReadAnimTime());
        }

        /// <summary>
        /// Preferred host (server operator / first connected) owns enemy AI whenever they are in the room.
        /// Otherwise all clients elect the same remaining player by ordinal name.
        /// </summary>
        private void ClaimOrFollowExistingHost()
        {
            ApplyHostResolution(reason: "level-load");
        }

        private void RecalculateHostAfterRosterChange()
        {
            ApplyHostResolution(reason: "roster");
        }

        private void ApplyHostResolution(string reason)
        {
            string local = Main.Multiplayer.PlayerName ?? string.Empty;
            var present = GetPlayersInScene();
            string previous = _sceneHostName;
            string resolved = EnemyAuthority.Resolve(present, GetPreferredEnemyHostName());

            _sceneHostName = resolved;
            _isSceneHost = !string.IsNullOrEmpty(resolved) && resolved == local;

            if (previous != _sceneHostName || reason == "level-load")
            {
                ModLog.Info(
                    $"[EnemySync] Host resolve ({reason}): {_sceneHostName} localHost={_isSceneHost} preferred={GetPreferredEnemyHostName()}");
            }
        }

        private string GetPreferredEnemyHostName()
        {
            // Authority is a session decision. Per-client overrides can elect two hosts.
            return Main.Multiplayer.NetworkManager.SessionEnemyHostName ?? string.Empty;
        }

        private List<string> GetPlayersInScene()
        {
            var names = new List<string>();
            if (!string.IsNullOrEmpty(Main.Multiplayer.PlayerName))
                names.Add(Main.Multiplayer.PlayerName);
            foreach (PlayerStatus other in Main.Multiplayer.OtherPlayerManager.AllConnectedPlayers)
                if (other.CurrentScene == _currentScene && !names.Contains(other.Name))
                    names.Add(other.Name);
            return names;
        }

        private void ApplyAuthorityToAll()
        {
            foreach (var synced in UniqueEnemies())
            {
                if (!synced.IsValid)
                    continue;

                // Never revive dead enemies when reclaiming host
                if (synced.VisuallyDead || synced.LastSentAlive == false || synced.Enemy.Dead)
                {
                    if (!_isSceneHost || synced.VisuallyDead)
                        ForceKill(synced); // non-host / already hard-killed: hide body
                    else
                        synced.SetPuppet(true); // host natural corpse: AI off, keep death anim
                    continue;
                }

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
                if (!IsSyncableEnemy(enemy) || IsBossEnemy(enemy) || enemy.Dead)
                    continue;
                if (GetTrackedNetId(enemy).HasValue)
                    continue;

                if (!_unboundEnemies.TryGetValue(enemy, out SyncedEnemy unbound))
                    _unboundEnemies[enemy] = unbound = new SyncedEnemy(0, GetEnemyTypeId(enemy), enemy);
                unbound.SetPuppet(true);
            }
        }

        private void ScanAndRegisterEnemies(bool forceSnapshot)
        {
            Enemy[] found = UnityEngine.Object.FindObjectsOfType<Enemy>();
            foreach (Enemy enemy in found)
            {
                if (!IsSyncableEnemy(enemy) || enemy.Stats == null || enemy.Status == null)
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
                if (_enemies.TryGetValue(netId, out SyncedEnemy hostExisting) && hostExisting.Enemy == enemy)
                {
                    hostExisting.SetPuppet(false);
                    continue;
                }
                while (_enemies.ContainsKey(netId))
                    netId = netId == int.MaxValue ? 1 : netId + 1;

                RegisterEnemy(enemy, netId);
            }

            foreach (SyncedEnemy tracked in UniqueEnemies())
            {
                if (!tracked.IsValid)
                {
                    tracked.LastSentAlive = false;
                    tracked.LastSentLife = 0f;
                }
            }

            if (!_isSceneHost)
            {
                SuppressUntrackedEnemyAi();
                ApplyPendingDeaths();

                // If local living enemies exist that still aren't bound to host ids, keep asking
                int unbound = 0;
                foreach (Enemy enemy in found)
                {
                    if (!IsSyncableEnemy(enemy) || enemy.Dead)
                        continue;
                    if (!GetTrackedNetId(enemy).HasValue)
                        unbound++;
                }
                if (unbound > 0)
                    RequestSyncFromHost();
            }

            if (forceSnapshot && _isSceneHost)
                SendAllSnapshots();
        }

        private SyncedEnemy RegisterEnemy(Enemy enemy, int netId)
        {
            if (_unboundEnemies.TryGetValue(enemy, out SyncedEnemy unbound))
            {
                unbound.SetPuppet(false);
                _unboundEnemies.Remove(enemy);
            }
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

            var synced = new SyncedEnemy(netId, GetEnemyTypeId(enemy), enemy);
            synced.SetPuppet(!_isSceneHost);
            _enemies[netId] = synced;
            ModLog.Info($"[EnemySync] Register {netId} {synced.EnemyTypeId} puppet={synced.IsPuppet}");
            return synced;
        }

        private SyncedEnemy BindRemoteEnemy(int netId, string enemyTypeId, Vector2 position, bool allowProximity = true)
        {
            if (_enemies.TryGetValue(netId, out SyncedEnemy synced) && synced.IsValid)
            {
                synced.SetPuppet(true);
                return synced;
            }

            // Prefer stable-id match (works even when host pos has drifted far from friend's spawn)
            Enemy found = FindEnemyByStableId(netId);
            if (found != null && (GetEnemyTypeId(found) != enemyTypeId || GetTrackedNetId(found).HasValue))
                found = null;
            if (found == null && allowProximity)
                found = FindNearestUntrackedEnemy(position, 24f, enemyTypeId, allowDead: true);
            if (found == null && allowProximity && !string.IsNullOrEmpty(enemyTypeId))
                found = FindNearestUntrackedEnemy(position, 40f, enemyTypeId, allowDead: true);
            // Last resort: exactly one unbound enemy of that type in the room
            if (found == null && allowProximity && !string.IsNullOrEmpty(enemyTypeId))
                found = FindSoleUntrackedOfType(enemyTypeId);

            if (found == null)
                return null;

            ModLog.Info($"[EnemySync] Bind {netId}/{enemyTypeId} → local {found.Id} @{found.transform.position}");
            return RegisterEnemy(found, netId);
        }

        private Enemy FindSoleUntrackedOfType(string enemyTypeId)
        {
            Enemy sole = null;
            Enemy[] found = UnityEngine.Object.FindObjectsOfType<Enemy>();
            foreach (Enemy enemy in found)
            {
                if (!IsSyncableEnemy(enemy) || enemy.Dead)
                    continue;
                if (GetEnemyTypeId(enemy) != enemyTypeId)
                    continue;
                if (GetTrackedNetId(enemy).HasValue)
                    continue;
                if (sole != null)
                    return null; // ambiguous
                sole = enemy;
            }
            return sole;
        }

        private Enemy FindEnemyByStableId(int netId)
        {
            if (netId == 0)
                return null;

            Enemy[] found = UnityEngine.Object.FindObjectsOfType<Enemy>();
            foreach (Enemy enemy in found)
            {
                if (!IsSyncableEnemy(enemy))
                    continue;
                if (ComputeStableNetId(enemy) == netId)
                    return enemy;
            }
            return null;
        }

        private Enemy FindNearestUntrackedEnemy(Vector2 hintPos, float maxDist, string enemyTypeId, bool allowDead)
        {
            Enemy[] found = UnityEngine.Object.FindObjectsOfType<Enemy>();
            Enemy best = null;
            float bestDist = maxDist;

            foreach (Enemy enemy in found)
            {
                if (!IsSyncableEnemy(enemy))
                    continue;
                if (!allowDead && enemy.Dead)
                    continue;
                if (!string.IsNullOrEmpty(enemyTypeId) && GetEnemyTypeId(enemy) != enemyTypeId)
                    continue;
                if (GetTrackedNetId(enemy).HasValue)
                    continue;

                float d = Vector2.Distance(enemy.transform.position, hintPos);
                // Also compare against spawn point — host may have pulled the enemy away
                var spawn = enemy.GetComponentInParent<EnemySpawnPoint>();
                if (spawn != null)
                {
                    float spawnDist = Vector2.Distance(spawn.Position, hintPos);
                    if (spawnDist < d)
                        d = spawnDist;
                }

                if (d <= bestDist)
                {
                    bestDist = d;
                    best = enemy;
                }
            }

            return best;
        }

        private void ApplyRemoteState(SyncedEnemy synced, Vector2 position, float life, bool alive, bool facingRight, int animHash, float animTime)
        {
            if (!synced.IsValid)
                return;

            if (synced.IsBoss)
            {
                ApplyBossHealth(synced, life, alive);
                return;
            }
            // Already hard-killed — ignore further living/dead spam from host snapshots
            if (synced.VisuallyDead)
                return;

            // life<=0 must count as dead even if the alive flag is wrong
            bool shouldBeDead = !alive || life <= 0.01f;
            if (shouldBeDead)
            {
                ForceKill(synced);
                return;
            }

            synced.SetPuppet(true);

            if (synced.Enemy.gameObject.activeSelf)
                synced.Enemy.transform.position = new Vector3(position.x, position.y, synced.Enemy.transform.position.z);

            if (synced.Enemy.Status != null)
                synced.Enemy.Status.Orientation = facingRight ? EntityOrientation.Right : EntityOrientation.Left;

            if (synced.Enemy.SpriteRenderer != null)
                synced.Enemy.SpriteRenderer.flipX = !facingRight;

            if (Mathf.Abs(synced.Enemy.CurrentLife - life) > 0.1f)
                synced.Enemy.CurrentLife = life;

            synced.ApplyAnim(animHash, animTime);

            synced.LastSentPosition = position;
            synced.LastSentLife = life;
            synced.LastSentAlive = true;
            synced.LastSentFacingRight = facingRight;
            synced.LastSentAnimHash = animHash;
            synced.LastSentAnimTime = animTime;
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
            if (enemy == null) return 0;
            // Spawned enemies are parented to the level, not to their spawn point.
            foreach (EnemySpawnPoint spawn in UnityEngine.Object.FindObjectsOfType<EnemySpawnPoint>())
            {
                if (spawn.SpawnedEnemy == enemy)
                    return StableHash(GetEnemyTypeId(enemy) + "|spawn|" + TransformPath(spawn.transform));
            }
            return StableHash(GetEnemyTypeId(enemy) + "|scene|" + TransformPath(enemy.transform));
        }

        private static string TransformPath(Transform transform)
        {
            var path = new StringBuilder();
            while (transform != null)
            {
                path.Insert(0, "/" + transform.name + ":" + (transform.parent != null ? transform.GetSiblingIndex() : 0));
                transform = transform.parent;
            }
            return path.ToString();
        }

        public static string GetEnemyTypeId(Enemy enemy) => (enemy.Id ?? string.Empty) + "|" + enemy.GetType().FullName;

        public static bool IsBossEnemy(Enemy enemy)
        {
            if (enemy == null) return false;
            Type type = enemy.GetType();
            return (type.Namespace ?? string.Empty).StartsWith("Gameplay.GameControllers.Bosses", StringComparison.Ordinal)
                || type.Name.IndexOf("Boss", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void ApplyBossHealth(SyncedEnemy synced, float life, bool alive)
        {
            if (synced.Enemy.Dead) return;
            if (alive)
            {
                synced.Enemy.CurrentLife = life;
                synced.LastSentLife = life;
                synced.LastSentAlive = true;
                return;
            }
            // Run the boss's IDamageable method: it owns death animation and arena progression.
            // Never hide the boss object or stop its encounter coroutines as a room-enemy corpse.
            if (!(synced.Enemy is IDamageable damageable) || Core.Logic.Penitent == null) return;
            _applyingRemoteDamage = true;
            try { damageable.Damage(CreateLethalHit(synced.Enemy)); }
            finally { _applyingRemoteDamage = false; }
            synced.LastSentAlive = !synced.Enemy.Dead;
            synced.LastSentLife = synced.Enemy.CurrentLife;
        }

        private static Hit CreateLethalHit(Enemy enemy) => new Hit
        {
            AttackingEntity = Core.Logic.Penitent.gameObject,
            DamageAmount = Mathf.Ceil(enemy.CurrentLife + enemy.Stats.Defense.Final + 1f),
            Unblockable = true, Unparriable = true, Unnavoidable = true,
        };

        public static int ResolveNetId(Enemy enemy) => ComputeStableNetId(enemy);

        private void Clear()
        {
            if (_hitSource != null)
                UnityEngine.Object.Destroy(_hitSource);
            _hitSource = null;
            _enemies.Clear();
            _unboundEnemies.Clear();
            _pendingDeaths.Clear();
            _remoteHitCooldown.Clear();
            _scanTimer = 0f;
            _snapshotTimer = 0f;
            _retargetTimer = 0f;
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
            return enemy != null && enemy.gameObject != null && enemy.Id != "EV09"
                && (IsBossEnemy(enemy) || !string.IsNullOrEmpty(enemy.Id));
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

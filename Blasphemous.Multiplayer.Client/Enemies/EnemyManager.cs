using Blasphemous.ModdingAPI;
using Blasphemous.Multiplayer.Client.Players;
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
    /// Host-authoritative room-enemy sync.
    /// Canonical IDs come from the scene host's snapshots. Damage always carries
    /// hit position + enemy type so the host can remap when client hashes disagree.
    /// </summary>
    public class EnemyManager
    {
        private readonly Dictionary<int, SyncedEnemy> _enemies = new();
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
        private readonly float _remoteContactRange = 1.75f;
        private readonly float _remoteContactRangeAttacking = 2.4f;
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

            TickRemoteHitCooldowns();

            if (!_isSceneHost)
                return;

            // One shared AI: retarget + ensure remote players take contact/attack hits
            _retargetTimer -= Time.deltaTime;
            if (_retargetTimer <= 0f)
            {
                _retargetTimer = _retargetInterval;
                RetargetEnemiesToNearestPlayer();
                PulseRemoteContactHits();
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

                bool moved = Mathf.Abs(pos.x - synced.LastSentPosition.x) > _positionCutoff
                    || Mathf.Abs(pos.y - synced.LastSentPosition.y) > _positionCutoff;
                bool lifeChanged = Mathf.Abs(life - synced.LastSentLife) > _lifeCutoff;
                bool facingChanged = facingRight != synced.LastSentFacingRight;
                bool animChanged = animHash != 0 && animHash != synced.LastSentAnimHash;

                if (!moved && !lifeChanged && !facingChanged && !animChanged)
                    continue;

                synced.LastSentPosition = pos;
                synced.LastSentLife = life;
                synced.LastSentAlive = true;
                synced.LastSentFacingRight = facingRight;
                synced.LastSentAnimHash = animHash;
                Main.Multiplayer.NetworkManager.SendEnemyState(synced.NetId, pos, life, alive: true, facingRight, animHash);
            }
        }

        /// <summary>
        /// Host: enemy contact hit the remote player's avatar — tell their client to take HP damage.
        /// </summary>
        public void NotifyRemotePlayerHitByEnemy(string playerName, float damageAmount)
        {
            if (!_isSceneHost || string.IsNullOrEmpty(playerName) || damageAmount <= 0f)
                return;
            if (playerName == Main.Multiplayer.PlayerName)
                return;

            if (_remoteHitCooldown.TryGetValue(playerName, out float left) && left > 0f)
                return;

            _remoteHitCooldown[playerName] = _remoteHitCooldownSeconds;
            ModLog.Info($"[EnemySync] Enemy hit remote {playerName} for {damageAmount:0.#}");
            Main.Multiplayer.NetworkManager.SendEnemyPlayerHit(playerName, damageAmount);
        }

        public void ReceiveEnemyPlayerHit(string senderName, string receiverName, float damageAmount)
        {
            if (receiverName != Main.Multiplayer.PlayerName)
                return;
            if (Core.Logic.Penitent == null || Core.Logic.Penitent.Dead)
                return;
            if (damageAmount <= 0f)
                return;

            GameObject attacker = FindNearestSyncedEnemyGameObject() ?? Core.Logic.Penitent.gameObject;
            var hit = new Hit
            {
                AttackingEntity = attacker,
                DamageAmount = damageAmount,
                DamageElement = DamageArea.DamageElement.Normal,
                DamageType = DamageArea.DamageType.Normal,
                Force = 1f,
                ThrowbackDirByOwnerPosition = true,
                Unparriable = true,
                Unblockable = true,
                Unnavoidable = true,
            };

            ModLog.Info($"[EnemySync] Applying shared-enemy hit ({damageAmount:0.#}) from host {senderName}");
            Core.Logic.Penitent.Damage(hit);
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
                if (!synced.IsValid || synced.Enemy.Dead || synced.IsPuppet)
                    continue;

                GameObject best = PickNearestTarget(synced.Enemy.transform.position, candidates, synced.Enemy.Target);
                if (best != null && synced.Enemy.Target != best)
                {
                    synced.Enemy.Target = best;
                    ModLog.Info($"[EnemySync] Retarget {synced.NetId} → {best.name}");
                }
            }
        }

        /// <summary>
        /// Belt-and-suspenders: when a shared enemy is on top of a remote avatar,
        /// push contact damage even if a specific attack hardcoded Penitent.
        /// </summary>
        private void PulseRemoteContactHits()
        {
            foreach (var synced in UniqueEnemies())
            {
                if (!synced.IsValid || synced.Enemy.Dead || synced.IsPuppet)
                    continue;

                GameObject targetGo = synced.Enemy.Target;
                if (targetGo == null)
                    continue;

                OtherPlayerScript other = targetGo.GetComponent<OtherPlayerScript>()
                    ?? targetGo.GetComponentInParent<OtherPlayerScript>();
                if (other == null || string.IsNullOrEmpty(other.PlayerName))
                    continue;

                float dist = Vector2.Distance(synced.Enemy.transform.position, targetGo.transform.position);
                float range = synced.Enemy.IsAttacking ? _remoteContactRangeAttacking : _remoteContactRange;
                if (dist > range)
                    continue;

                float amount = EstimateContactDamage(synced.Enemy);
                NotifyRemotePlayerHitByEnemy(other.PlayerName, amount);
            }
        }

        private static float EstimateContactDamage(Enemy enemy)
        {
            if (enemy == null)
                return 10f;

            EnemyAttack attack = enemy.GetComponentInChildren<EnemyAttack>();
            if (attack != null && attack.ContactDamageAmount > 0f)
                return attack.ContactDamageAmount;

            // Fallback from typical early-game contact values
            return Mathf.Max(8f, enemy.CurrentOutputDamage > 0 ? enemy.CurrentOutputDamage : 10f);
        }

        private List<GameObject> CollectPlayerTargets()
        {
            var list = new List<GameObject>(4);
            if (Core.Logic.Penitent != null && !Core.Logic.Penitent.Dead)
                list.Add(Core.Logic.Penitent.gameObject);

            foreach (OtherPlayerScript other in Main.Multiplayer.OtherPlayerManager.AllActivePlayers)
            {
                if (other == null || !other.gameObject.activeInHierarchy)
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

        private GameObject FindNearestSyncedEnemyGameObject()
        {
            if (Core.Logic.Penitent == null)
                return null;
            Vector2 from = Core.Logic.Penitent.transform.position;
            GameObject best = null;
            float bestDist = float.MaxValue;
            foreach (var synced in UniqueEnemies())
            {
                if (!synced.IsValid || synced.Enemy.Dead)
                    continue;
                float d = Vector2.Distance(from, synced.Enemy.transform.position);
                if (d < bestDist)
                {
                    bestDist = d;
                    best = synced.Enemy.gameObject;
                }
            }
            return best;
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

        public void ReceiveSnapshot(string senderName, int netId, string enemyTypeId, Vector2 position, float life, bool alive, bool facingRight, int animHash)
        {
            // Sticky host: ignore peer authority traffic; do not yield mid-room
            if (_isSceneHost)
                return;

            SyncedEnemy synced = BindRemoteEnemy(netId, enemyTypeId, position);
            if (synced == null)
            {
                if (!alive)
                {
                    _pendingDeaths[netId] = new PendingDeath { TypeId = enemyTypeId ?? string.Empty, Position = position };
                    ModLog.Warn($"[EnemySync] Snapshot death pending {netId}/{enemyTypeId}");
                }
                else
                {
                    ModLog.Warn($"[EnemySync] Snapshot miss {netId}/{enemyTypeId} @ {position}");
                }
                RequestSyncFromHost();
                return;
            }

            ApplyRemoteState(synced, position, life, alive, facingRight, animHash);
            if (!alive)
                _pendingDeaths.Remove(netId);
        }

        public void ReceiveState(string senderName, int netId, Vector2 position, float life, bool alive, bool facingRight, int animHash)
        {
            if (_isSceneHost)
                return;

            SyncedEnemy synced = BindRemoteEnemy(netId, null, position);
            if (synced == null)
                return;

            ApplyRemoteState(synced, position, life, alive, facingRight, animHash);
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

        public void ReceiveDeath(string senderName, int netId, Vector2 deathPos, string enemyTypeId)
        {
            if (_isSceneHost)
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
            if (_enemies.TryGetValue(netId, out synced) && synced.IsValid)
            {
                // already bound
            }
            else
            {
                synced = BindRemoteEnemy(netId, enemyTypeId, deathPos);
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

            _applyingRemoteDamage = true;
            try
            {
                synced.ForceVisualDeath();
            }
            finally
            {
                _applyingRemoteDamage = false;
            }

            ModLog.Info($"[EnemySync] Applied death {synced.NetId} ({synced.EnemyTypeId}) visual={synced.VisuallyDead}");
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

        public void SendAllSnapshots()
        {
            if (!_isSceneHost)
                return;

            int sent = 0;
            int deadSent = 0;
            foreach (var synced in UniqueEnemies())
            {
                if (!synced.IsValid)
                    continue;

                bool alive = !synced.Enemy.Dead;
                Vector2 pos = alive
                    ? (Vector2)synced.Enemy.transform.position
                    : (synced.LastSentPosition.sqrMagnitude > 0.0001f
                        ? synced.LastSentPosition
                        : (Vector2)synced.Enemy.transform.position);
                float life = alive ? synced.Enemy.CurrentLife : 0f;
                bool facingRight = IsFacingRight(synced.Enemy);
                int animHash = alive ? synced.ReadAnimHash() : 0;
                synced.LastSentPosition = pos;
                synced.LastSentLife = life;
                synced.LastSentAlive = alive;
                synced.LastSentFacingRight = facingRight;
                synced.LastSentAnimHash = animHash;
                Main.Multiplayer.NetworkManager.SendEnemySnapshot(
                    synced.NetId, synced.EnemyTypeId, pos, life, alive, facingRight, animHash);
                sent++;
                if (!alive)
                    deadSent++;
            }

            _snapshotLogCooldown++;
            if (sent > 0 && _snapshotLogCooldown % 5 == 1)
                ModLog.Info($"[EnemySync] Snapshots x{sent} (dead={deadSent}, host={_sceneHostName})");
        }

        private SyncedEnemy ResolveDamageTarget(int netId, Vector2 hitPos, string enemyTypeId)
        {
            // Exact living id (or alias) — never remap a known-dead id onto another mob
            if (netId != 0 && _enemies.TryGetValue(netId, out SyncedEnemy byId) && byId.IsValid)
            {
                if (byId.Enemy.Dead)
                    return null;
                return byId;
            }

            // Proximity remap: same type only, never cross types
            Enemy found = null;
            if (!string.IsNullOrEmpty(enemyTypeId))
                found = FindNearestLivingEnemy(hitPos, 10f, enemyTypeId);

            if (found == null && netId != 0)
            {
                Enemy byHash = FindEnemyByNetId(netId, enemyTypeId, hitPos);
                if (byHash != null && !byHash.Dead)
                    found = byHash;
            }

            if (found == null)
                return null;

            // Keep host canonical id if already tracked; otherwise adopt attacker id only when unused
            int? tracked = GetTrackedNetId(found);
            int canonical = tracked ?? ComputeStableNetId(found);
            return RegisterEnemy(found, canonical);
        }

        private void AliasNetId(int aliasId, SyncedEnemy synced)
        {
            if (aliasId == 0 || synced == null || !synced.IsValid || synced.Enemy.Dead)
                return;
            if (_enemies.TryGetValue(aliasId, out SyncedEnemy existing))
            {
                if (existing == synced)
                    return;
                // Never overwrite a different living enemy's slot
                if (existing.IsValid && !existing.Enemy.Dead && existing.NetId != synced.NetId)
                    return;
            }

            _enemies[aliasId] = synced;
            ModLog.Info($"[EnemySync] Alias {aliasId} → canonical {synced.NetId} ({synced.EnemyTypeId})");
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
                Main.Multiplayer.NetworkManager.SendEnemyState(synced.NetId, pos, life, true, facingRight, synced.ReadAnimHash());
        }

        /// <summary>
        /// Sticky scene host: first occupant claims; joiners always follow while anyone else is present.
        /// Host is never stolen mid-room by peer enemy packets.
        /// </summary>
        private void ClaimOrFollowExistingHost()
        {
            string local = Main.Multiplayer.PlayerName ?? string.Empty;
            var present = GetPlayersInScene();
            bool othersPresent = present.Any(p => p != local);

            if (othersPresent)
            {
                // Joiner: never claim. Prefer lex-first other as the expected sticky host name
                // until roster recalc confirms (existing host keeps themselves via Recalculate).
                string other = present.Where(p => p != local).OrderBy(p => p, StringComparer.Ordinal).First();
                _sceneHostName = other;
                _isSceneHost = false;
                ModLog.Info($"[EnemySync] Joiner following host={_sceneHostName}");
            }
            else
            {
                _sceneHostName = local;
                _isSceneHost = true;
                ModLog.Info($"[EnemySync] Claiming sticky host ({local})");
            }
        }

        private void RecalculateHostAfterRosterChange()
        {
            string local = Main.Multiplayer.PlayerName ?? string.Empty;
            var present = GetPlayersInScene();

            // Sticky: keep current host for as long as they remain in the scene
            if (!string.IsNullOrEmpty(_sceneHostName) && present.Contains(_sceneHostName))
            {
                _isSceneHost = _sceneHostName == local;
                return;
            }

            // Host left — re-elect once among remaining players
            string previous = _sceneHostName;
            _sceneHostName = present.OrderBy(p => p, StringComparer.Ordinal).FirstOrDefault() ?? local;
            _isSceneHost = _sceneHostName == local;
            ModLog.Info($"[EnemySync] Host left ({previous}) → re-elect {_sceneHostName} (localHost={_isSceneHost})");
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

            // Prefer stable-id match (works even when host pos has drifted far from friend's spawn)
            Enemy found = FindEnemyByStableId(netId);
            if (found == null)
                found = FindNearestUntrackedEnemy(position, 24f, enemyTypeId, allowDead: true);
            if (found == null && !string.IsNullOrEmpty(enemyTypeId))
                found = FindNearestUntrackedEnemy(position, 40f, enemyTypeId, allowDead: true);
            // Last resort: exactly one unbound enemy of that type in the room
            if (found == null && !string.IsNullOrEmpty(enemyTypeId))
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
                if (enemy.Id != enemyTypeId)
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
                if (!string.IsNullOrEmpty(enemyTypeId) && enemy.Id != enemyTypeId)
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

        private void ApplyRemoteState(SyncedEnemy synced, Vector2 position, float life, bool alive, bool facingRight, int animHash)
        {
            if (!synced.IsValid)
                return;

            // life<=0 must count as dead even if the alive flag is wrong
            bool shouldBeDead = !alive || life <= 0.01f || synced.VisuallyDead;
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

            synced.ApplyAnimHash(animHash);

            synced.LastSentPosition = position;
            synced.LastSentLife = life;
            synced.LastSentAlive = true;
            synced.LastSentFacingRight = facingRight;
            synced.LastSentAnimHash = animHash;
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

            // Spawn-point position hash is stable across clients. SpawningId can be
            // instance-local and caused Death/Snapshot misses between players.
            var spawn = enemy.GetComponentInParent<EnemySpawnPoint>();
            if (spawn != null)
            {
                Vector3 p = spawn.Position;
                return StableHash($"{enemy.Id}|sp|{Round(p.x)}|{Round(p.y)}");
            }

            if (enemy.SpawningId != 0)
                return enemy.SpawningId;

            string goName = enemy.gameObject != null ? enemy.gameObject.name : "unk";
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

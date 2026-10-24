using Blasphemous.ModdingAPI;
using Framework.FrameworkCore;
using Gameplay.GameControllers.Entities;
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Blasphemous.Multiplayer.Client.Enemies
{
    /// <summary>
    /// Host-authoritative room-enemy sync for the current scene.
    /// Scene host runs real AI; other clients treat local enemies as puppets.
    /// </summary>
    public class EnemyManager
    {
        private readonly Dictionary<int, SyncedEnemy> _enemies = new();
        private readonly float _positionCutoff = 0.05f;
        private readonly float _lifeCutoff = 0.5f;
        private readonly float _scanInterval = 0.5f;
        private float _scanTimer;
        private bool _isSceneHost;
        private string _currentScene = string.Empty;
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

            if (!Main.Multiplayer.CurrentlyInLevel || !Main.Multiplayer.NetworkManager.IsConnected)
                return;

            RecalculateHost();
            ScanAndRegisterEnemies(forceSnapshot: true);
            ModLog.Info($"[EnemySync] Level {scene} — sceneHost={_isSceneHost}, tracked={_enemies.Count}");
        }

        public void LevelUnloaded()
        {
            Clear();
            _currentScene = string.Empty;
            _isSceneHost = false;
        }

        public void OnDisconnect()
        {
            // Restore local AI if we had turned enemies into puppets
            foreach (var synced in _enemies.Values)
            {
                if (synced.IsValid && synced.IsPuppet)
                    synced.SetPuppet(false);
            }
            Clear();
            _isSceneHost = false;
        }

        public void OnPlayersInSceneChanged()
        {
            if (!Main.Multiplayer.CurrentlyInLevel || !Main.Multiplayer.NetworkManager.IsConnected)
                return;

            bool wasHost = _isSceneHost;
            RecalculateHost();
            if (wasHost == _isSceneHost)
                return;

            ModLog.Info($"[EnemySync] Host changed → {_isSceneHost}");
            ApplyAuthorityToAll();
            if (_isSceneHost)
                ScanAndRegisterEnemies(forceSnapshot: true);
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

            foreach (var synced in _enemies.Values)
            {
                if (!synced.IsValid)
                    continue;

                if (synced.Enemy.Dead)
                {
                    if (synced.LastSentAlive)
                    {
                        synced.LastSentAlive = false;
                        Main.Multiplayer.NetworkManager.SendEnemyDeath(synced.NetId);
                    }
                    continue;
                }

                Vector2 pos = synced.Enemy.transform.position;
                float life = synced.Enemy.CurrentLife;
                bool facingRight = synced.Enemy.Status == null
                    || synced.Enemy.Status.Orientation == EntityOrientation.Right;

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

        /// <summary>
        /// Non-host: forward player damage to the scene host instead of applying locally.
        /// </summary>
        public bool TryInterceptDamage(Enemy enemy, float amount)
        {
            if (_applyingRemoteDamage || _isSceneHost || enemy == null)
                return false;
            if (!IsSyncableEnemy(enemy))
                return false;
            if (!Main.Multiplayer.NetworkManager.IsConnected)
                return false;

            int netId = ResolveNetId(enemy);
            if (!_enemies.ContainsKey(netId))
                RegisterEnemy(enemy, sendSnapshot: false);

            Main.Multiplayer.NetworkManager.SendEnemyDamage(netId, amount);
            return true;
        }

        public void ReceiveSnapshot(int netId, string enemyTypeId, Vector2 position, float life, bool alive, bool facingRight)
        {
            if (_isSceneHost)
                return;

            if (!_enemies.TryGetValue(netId, out SyncedEnemy synced) || !synced.IsValid)
            {
                Enemy found = FindEnemyByNetId(netId, enemyTypeId, position);
                if (found == null)
                {
                    ModLog.Warn($"[EnemySync] Snapshot for {netId} ({enemyTypeId}) — no local enemy");
                    return;
                }
                synced = RegisterEnemy(found, sendSnapshot: false);
            }

            ApplyRemoteState(synced, position, life, alive, facingRight);
        }

        public void ReceiveState(int netId, Vector2 position, float life, bool alive, bool facingRight)
        {
            if (_isSceneHost)
                return;

            if (!_enemies.TryGetValue(netId, out SyncedEnemy synced) || !synced.IsValid)
            {
                // Late state without snapshot — try bind by id alone
                Enemy found = FindEnemyByNetId(netId, null, position);
                if (found == null)
                    return;
                synced = RegisterEnemy(found, sendSnapshot: false);
            }

            ApplyRemoteState(synced, position, life, alive, facingRight);
        }

        public void ReceiveDamage(int netId, float amount, string attackerName)
        {
            if (!_isSceneHost)
                return;
            if (!_enemies.TryGetValue(netId, out SyncedEnemy synced) || !synced.IsValid || synced.Enemy.Dead)
                return;

            // Ignore our own forwarded packet if somehow looped; host applies for everyone including self locally already
            if (!string.IsNullOrEmpty(attackerName) && attackerName == Main.Multiplayer.PlayerName)
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

            Vector2 pos = synced.Enemy.transform.position;
            float life = synced.Enemy.CurrentLife;
            bool facingRight = synced.Enemy.Status == null
                || synced.Enemy.Status.Orientation == EntityOrientation.Right;
            bool alive = !synced.Enemy.Dead;

            synced.LastSentPosition = pos;
            synced.LastSentLife = life;
            synced.LastSentAlive = alive;
            synced.LastSentFacingRight = facingRight;

            if (!alive)
                Main.Multiplayer.NetworkManager.SendEnemyDeath(netId);
            else
                Main.Multiplayer.NetworkManager.SendEnemyState(netId, pos, life, true, facingRight);
        }

        public void ReceiveDeath(int netId)
        {
            if (_isSceneHost)
                return;
            if (!_enemies.TryGetValue(netId, out SyncedEnemy synced) || !synced.IsValid)
                return;

            if (!synced.Enemy.Dead)
            {
                _applyingRemoteDamage = true;
                try
                {
                    synced.Enemy.Kill();
                }
                finally
                {
                    _applyingRemoteDamage = false;
                }
            }
            synced.LastSentAlive = false;
        }

        /// <summary>Host sends full snapshots to a peer who just entered this scene (via broadcast).</summary>
        public void SendAllSnapshots()
        {
            if (!_isSceneHost)
                return;

            foreach (var synced in _enemies.Values)
            {
                if (!synced.IsValid || synced.Enemy.Dead)
                    continue;

                Vector2 pos = synced.Enemy.transform.position;
                float life = synced.Enemy.CurrentLife;
                bool facingRight = synced.Enemy.Status == null
                    || synced.Enemy.Status.Orientation == EntityOrientation.Right;
                Main.Multiplayer.NetworkManager.SendEnemySnapshot(
                    synced.NetId, synced.EnemyTypeId, pos, life, true, facingRight);
            }
        }

        private void RecalculateHost()
        {
            // Scene host = lexicographically first player name among players currently in this scene (including self).
            string best = Main.Multiplayer.PlayerName ?? string.Empty;
            foreach (var other in Main.Multiplayer.OtherPlayerManager.AllActivePlayers)
            {
                if (other == null || string.IsNullOrEmpty(other.name))
                    continue;
                string name = other.name.StartsWith("_") ? other.name.Substring(1) : other.name;
                if (string.IsNullOrEmpty(best) || string.CompareOrdinal(name, best) < 0)
                    best = name;
            }
            _isSceneHost = !string.IsNullOrEmpty(best) && best == Main.Multiplayer.PlayerName;
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
                    continue;
                }

                RegisterEnemy(enemy, sendSnapshot: _isSceneHost && forceSnapshot);
            }

            // Drop destroyed references
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
                    netId, synced.EnemyTypeId, enemy.transform.position, enemy.CurrentLife, true,
                    enemy.Status == null || enemy.Status.Orientation == EntityOrientation.Right);
            }

            return synced;
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

            // Nudge life toward host value without re-entering damage hooks
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
            }

            return bestDist < 3f ? best : null;
        }

        private void Clear()
        {
            _enemies.Clear();
            _scanTimer = 0f;
        }

        public static bool IsSyncableEnemy(Enemy enemy)
        {
            if (enemy == null || enemy.gameObject == null)
                return false;
            if (string.IsNullOrEmpty(enemy.Id))
                return false;
            if (enemy.Id == "EV09") // Dummy / special
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

            // Fallback: stable hash from type id + rounded spawn position
            Vector3 p = enemy.transform.position;
            string key = $"{enemy.Id}|{Mathf.RoundToInt(p.x)}|{Mathf.RoundToInt(p.y)}";
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
                // Keep non-zero so we never collide with "unset" SpawningId semantics accidentally as 0
                return hash == 0 ? 1 : hash;
            }
        }
    }
}

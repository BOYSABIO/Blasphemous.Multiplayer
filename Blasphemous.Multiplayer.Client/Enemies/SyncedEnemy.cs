using Framework.FrameworkCore;
using Gameplay.GameControllers.Entities;
using UnityEngine;

namespace Blasphemous.Multiplayer.Client.Enemies
{
    /// <summary>
    /// Tracks one room enemy under host authority (or as a remote-driven puppet).
    /// </summary>
    public class SyncedEnemy
    {
        public int NetId { get; }
        public string EnemyTypeId { get; }
        public Enemy Enemy { get; private set; }

        public Vector2 LastSentPosition { get; set; }
        public float LastSentLife { get; set; }
        public bool LastSentAlive { get; set; }
        public bool LastSentFacingRight { get; set; }

        public bool IsPuppet { get; private set; }

        public SyncedEnemy(int netId, string enemyTypeId, Enemy enemy)
        {
            NetId = netId;
            EnemyTypeId = enemyTypeId ?? string.Empty;
            Enemy = enemy;
            if (enemy != null)
            {
                LastSentPosition = enemy.transform.position;
                LastSentLife = enemy.CurrentLife;
                LastSentAlive = !enemy.Dead;
                LastSentFacingRight = enemy.Status == null || enemy.Status.Orientation == EntityOrientation.Right;
            }
        }

        public void Bind(Enemy enemy)
        {
            Enemy = enemy;
        }

        public void SetPuppet(bool puppet)
        {
            IsPuppet = puppet;
            if (Enemy == null)
                return;

            var behaviour = Enemy.EnemyBehaviour;
            if (behaviour == null)
                return;

            if (puppet)
            {
                behaviour.StopBehaviour();
                behaviour.StopMovement();
                behaviour.enabled = false;
            }
            else
            {
                behaviour.enabled = true;
                behaviour.StartBehaviour();
            }
        }

        public bool IsValid => Enemy != null && Enemy.gameObject != null;
    }
}

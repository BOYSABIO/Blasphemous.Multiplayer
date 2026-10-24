using Framework.FrameworkCore;
using Gameplay.GameControllers.Entities;
using UnityEngine;

namespace Blasphemous.Multiplayer.Client.Enemies
{
    /// <summary>
    /// Tracks one room enemy under host authority (or as a remote-driven puppet).
    /// One logical enemy across clients — host runs AI, non-hosts mirror state + anims.
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
        public int LastSentAnimHash { get; set; }
        public float LastSentAnimTime { get; set; }

        public bool IsPuppet { get; private set; }
        public bool VisuallyDead { get; private set; }

        public SyncedEnemy(int netId, string enemyTypeId, Enemy enemy)
        {
            NetId = netId;
            EnemyTypeId = enemyTypeId ?? string.Empty;
            Bind(enemy);
        }

        public void Bind(Enemy enemy)
        {
            Enemy = enemy;
            VisuallyDead = false;
            if (enemy == null)
                return;

            LastSentPosition = enemy.transform.position;
            LastSentLife = enemy.CurrentLife;
            LastSentAlive = !enemy.Dead;
            LastSentFacingRight = enemy.Status == null || enemy.Status.Orientation == EntityOrientation.Right;
            LastSentAnimHash = ReadAnimHash();
            LastSentAnimTime = ReadAnimTime();
        }

        public int ReadAnimHash()
        {
            if (Enemy == null || Enemy.Animator == null)
                return 0;
            return Enemy.Animator.GetCurrentAnimatorStateInfo(0).shortNameHash;
        }

        public float ReadAnimTime()
        {
            if (Enemy == null || Enemy.Animator == null)
                return 0f;
            return Enemy.Animator.GetCurrentAnimatorStateInfo(0).normalizedTime;
        }

        /// <summary>
        /// Mirror host animator state. Restarts only when the clip changes;
        /// otherwise seeks normalized time so attack windups look continuous.
        /// </summary>
        public void ApplyAnim(int animHash, float normalizedTime)
        {
            if (VisuallyDead || LastSentAlive == false)
                return;
            if (animHash == 0 || Enemy == null || Enemy.Animator == null)
                return;

            Animator animator = Enemy.Animator;
            AnimatorStateInfo current = animator.GetCurrentAnimatorStateInfo(0);

            if (current.shortNameHash != animHash)
            {
                animator.Play(animHash, 0, Mathf.Clamp01(normalizedTime % 1f));
                animator.Update(0f);
            }
            else
            {
                // Same clip — keep playback in sync without restarting the swing
                float local = current.normalizedTime % 1f;
                float remote = normalizedTime % 1f;
                if (Mathf.Abs(local - remote) > 0.12f)
                {
                    animator.Play(animHash, 0, remote);
                    animator.Update(0f);
                }
            }

            LastSentAnimHash = animHash;
            LastSentAnimTime = normalizedTime;
        }

        public void SetPuppet(bool puppet)
        {
            if (VisuallyDead || (Enemy != null && Enemy.Dead) || LastSentAlive == false)
            {
                IsPuppet = true;
                ApplyPuppetState(true);
                DisableCombatAndVisuals(hideSprite: VisuallyDead);
                return;
            }

            if (IsPuppet == puppet && Enemy != null)
            {
                ApplyPuppetState(puppet);
                return;
            }

            IsPuppet = puppet;
            ApplyPuppetState(puppet);
        }

        /// <summary>
        /// Hard death for sync. Idempotent — safe to call repeatedly from dead snapshots.
        /// </summary>
        /// <returns>True if this call newly applied death visuals.</returns>
        public bool ForceVisualDeath()
        {
            if (Enemy == null)
                return false;

            if (VisuallyDead)
            {
                // Already handled — keep body gone if something re-enabled it
                if (Enemy.gameObject != null && Enemy.gameObject.activeSelf)
                    Enemy.gameObject.SetActive(false);
                return false;
            }

            VisuallyDead = true;
            LastSentAlive = false;
            LastSentLife = 0f;
            IsPuppet = true;

            ApplyPuppetState(true);

            if (!Enemy.Dead)
            {
                try
                {
                    Enemy.CurrentLife = 0f;
                    Enemy.Kill();
                }
                catch
                {
                    // Some enemies throw if Kill runs with behaviour disabled — still hide below
                }
            }
            else
            {
                Enemy.CurrentLife = 0f;
            }

            DisableCombatAndVisuals(hideSprite: true);
            return true;
        }

        private void ApplyPuppetState(bool puppet)
        {
            if (Enemy == null)
                return;

            var behaviour = Enemy.EnemyBehaviour;
            if (behaviour != null)
            {
                if (puppet)
                {
                    behaviour.StopBehaviour();
                    behaviour.StopMovement();
                    behaviour.enabled = false;
                }
                else if (!VisuallyDead && !Enemy.Dead)
                {
                    behaviour.enabled = true;
                    behaviour.StartBehaviour();
                }
            }

            // Keep animator running on living puppets so attack clips can play
            if (Enemy.Animator != null && !VisuallyDead)
                Enemy.Animator.enabled = true;

            if (puppet || VisuallyDead)
                DisableCombatColliders();
        }

        private void DisableCombatAndVisuals(bool hideSprite)
        {
            DisableCombatColliders();

            if (!hideSprite || Enemy == null)
                return;

            if (Enemy.Animator != null)
                Enemy.Animator.enabled = false;

            if (Enemy.SpriteRenderer != null)
                Enemy.SpriteRenderer.enabled = false;

            var renderers = Enemy.GetComponentsInChildren<SpriteRenderer>(true);
            for (int i = 0; i < renderers.Length; i++)
                renderers[i].enabled = false;

            if (Enemy.gameObject != null && Enemy.gameObject.activeSelf)
                Enemy.gameObject.SetActive(false);
        }

        private void DisableCombatColliders()
        {
            if (Enemy == null)
                return;

            var colliders = Enemy.GetComponentsInChildren<Collider2D>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                if (colliders[i] != null)
                    colliders[i].enabled = false;
            }
        }

        public bool IsValid => Enemy != null && Enemy.gameObject != null;
    }
}

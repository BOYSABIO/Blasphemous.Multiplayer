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
        public const float DeathHideDelaySeconds = 1.35f;

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
        public bool BodyHidden { get; private set; }
        /// <summary>Unity time when the corpse should hard-hide (0 = none).</summary>
        public float HideBodyAt { get; private set; }

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
            BodyHidden = false;
            HideBodyAt = 0f;
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
            if (VisuallyDead || BodyHidden || LastSentAlive == false)
                return;
            if (animHash == 0 || Enemy == null || Enemy.Animator == null)
                return;

            Animator animator = Enemy.Animator;
            if (!animator.enabled)
                animator.enabled = true;

            AnimatorStateInfo current = animator.GetCurrentAnimatorStateInfo(0);

            if (current.shortNameHash != animHash)
            {
                animator.Play(animHash, 0, Mathf.Clamp01(normalizedTime % 1f));
                animator.Update(0f);
            }
            else
            {
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
                if (BodyHidden)
                    HardHideBody();
                else
                    DisableOutboundAttackColliders();
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
        /// Soft death: play Kill/death anim, keep body visible briefly, then hard-hide.
        /// Idempotent — safe from repeated dead snapshots.
        /// </summary>
        /// <returns>True if this call newly started the death sequence.</returns>
        public bool ForceVisualDeath()
        {
            if (Enemy == null)
                return false;

            if (VisuallyDead)
            {
                if (BodyHidden)
                    HardHideBody();
                return false;
            }

            VisuallyDead = true;
            LastSentAlive = false;
            LastSentLife = 0f;
            IsPuppet = true;
            BodyHidden = false;
            HideBodyAt = Time.time + DeathHideDelaySeconds;

            ApplyPuppetState(true);
            DisableOutboundAttackColliders();

            if (!Enemy.Dead)
            {
                try
                {
                    Enemy.CurrentLife = 0f;
                    Enemy.Kill();
                }
                catch
                {
                    // Some enemies throw if Kill runs with behaviour disabled
                }
            }
            else
            {
                Enemy.CurrentLife = 0f;
            }

            // Keep animator on so the death clip can play
            if (Enemy.Animator != null)
                Enemy.Animator.enabled = true;

            return true;
        }

        /// <summary>Called from EnemyManager.Update when HideBodyAt elapses.</summary>
        public void TickDeferredHide()
        {
            if (!VisuallyDead || BodyHidden)
                return;
            if (HideBodyAt <= 0f || Time.time < HideBodyAt)
                return;
            HardHideBody();
        }

        public void HardHideBody()
        {
            if (Enemy == null)
                return;

            BodyHidden = true;
            HideBodyAt = 0f;
            DisableAllColliders();

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

            // Living puppets keep animator so attack/death clips can play
            if (Enemy.Animator != null && !BodyHidden)
                Enemy.Animator.enabled = true;

            // Living puppets: keep DamageArea receive colliders so the friend can hit them.
            // Only strip outbound attack volumes. Dead bodies strip everything on hard-hide.
            if (puppet && !VisuallyDead)
                DisableOutboundAttackColliders();
        }

        /// <summary>
        /// Disable contact/attack outbound colliders but leave DamageArea hurtboxes alone
        /// so local player weapons can still land and be intercepted to the host.
        /// </summary>
        private void DisableOutboundAttackColliders()
        {
            if (Enemy == null)
                return;

            var colliders = Enemy.GetComponentsInChildren<Collider2D>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                Collider2D col = colliders[i];
                if (col == null)
                    continue;

                // Keep receive hurtboxes (DamageArea owns these)
                if (col.GetComponentInParent<DamageArea>() != null)
                    continue;

                // ContactDamage is outbound touch damage from the enemy
                if (col.GetComponentInParent<ContactDamage>() != null)
                {
                    col.enabled = false;
                    continue;
                }

                // AttackArea / weapon volumes
                if (col.GetComponentInParent<AttackArea>() != null)
                {
                    col.enabled = false;
                    continue;
                }
            }
        }

        private void DisableAllColliders()
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

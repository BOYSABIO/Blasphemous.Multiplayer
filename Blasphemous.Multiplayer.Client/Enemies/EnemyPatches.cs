using Blasphemous.Multiplayer.Client.Players;
using Framework.Managers;
using Gameplay.GameControllers.Enemies.Framework.Attack;
using Gameplay.GameControllers.Entities;
using Gameplay.GameControllers.Penitent;
using HarmonyLib;
using Tools.Level.Layout;
using UnityEngine;

namespace Blasphemous.Multiplayer.Client.Enemies
{
    /// <summary>
    /// Non-hosts forward enemy damage to the scene host instead of applying it locally.
    /// </summary>
    [HarmonyPatch(typeof(DamageArea), nameof(DamageArea.TakeDamage))]
    class Enemy_TakeDamage_Patch
    {
        public static bool Prefix(DamageArea __instance, Hit hit)
        {
            return !EnemyDamageHooks.ShouldBlockLocalDamage(__instance.GetComponentInParent<Enemy>(), hit.DamageAmount);
        }
    }

    /// <summary>
    /// Backup hook — some hits call Entity.Damage directly.
    /// </summary>
    [HarmonyPatch(typeof(Entity), nameof(Entity.Damage))]
    class Enemy_EntityDamage_Patch
    {
        public static bool Prefix(Entity __instance, float amount)
        {
            var enemy = __instance as Enemy;
            if (enemy == null)
                enemy = __instance.GetComponentInParent<Enemy>();
            return !EnemyDamageHooks.ShouldBlockLocalDamage(enemy, amount);
        }
    }

    /// <summary>
    /// Many enemy attacks call ContactAttack(Core.Logic.Penitent) even when the AI Target
    /// is a remote co-op avatar. Redirect the hit onto that avatar so one shared enemy
    /// can damage either player.
    /// </summary>
    [HarmonyPatch(typeof(EnemyAttack), nameof(EnemyAttack.ContactAttack))]
    class Enemy_ContactAttack_Redirect_Patch
    {
        public static void Prefix(EnemyAttack __instance, ref IDamageable damageable)
        {
            if (!Main.Multiplayer.NetworkManager.IsConnected)
                return;
            if (Main.Multiplayer.EnemyManager == null || !Main.Multiplayer.EnemyManager.IsSceneHost)
                return;

            Enemy enemy = __instance != null
                ? __instance.GetComponentInParent<Enemy>()
                : null;
            if (enemy == null || !EnemyManager.IsSyncableEnemy(enemy) || enemy.Dead)
                return;

            OtherPlayerScript remote = ResolveRemoteTarget(enemy);
            if (remote == null)
                return;

            // Only redirect when the game was about to hit the local Penitent (or null)
            if (damageable == null || IsLocalPenitent(damageable))
                damageable = remote;
        }

        private static OtherPlayerScript ResolveRemoteTarget(Enemy enemy)
        {
            if (enemy.Target == null)
                return null;
            return enemy.Target.GetComponent<OtherPlayerScript>()
                ?? enemy.Target.GetComponentInParent<OtherPlayerScript>();
        }

        private static bool IsLocalPenitent(IDamageable damageable)
        {
            if (damageable is Penitent)
                return true;
            if (damageable is Component c)
                return c.GetComponentInParent<Penitent>() != null;
            return false;
        }
    }

    /// <summary>
    /// Re-scan when a spawn point creates an enemy (many rooms spawn after level load).
    /// </summary>
    [HarmonyPatch(typeof(EnemySpawnPoint), nameof(EnemySpawnPoint.CreateEnemy))]
    class Enemy_SpawnPoint_Create_Patch
    {
        public static void Postfix()
        {
            Main.Multiplayer?.EnemyManager?.ForceRescan();
        }
    }

    static class EnemyDamageHooks
    {
        public static bool ShouldBlockLocalDamage(Enemy enemy, float amount)
        {
            EnemyManager manager = Main.Multiplayer?.EnemyManager;
            if (manager == null || manager.ApplyingRemoteDamage)
                return false;
            if (enemy == null || !EnemyManager.IsSyncableEnemy(enemy))
                return false;
            if (!Main.Multiplayer.NetworkManager.IsConnected)
                return false;
            if (amount <= 0f)
                return false;

            return manager.TryInterceptDamage(enemy, amount);
        }
    }
}

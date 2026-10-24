using Gameplay.GameControllers.Entities;
using HarmonyLib;
using Tools.Level.Layout;

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

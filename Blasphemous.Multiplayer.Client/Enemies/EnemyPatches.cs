using Gameplay.GameControllers.Entities;
using HarmonyLib;

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
            EnemyManager manager = Main.Multiplayer?.EnemyManager;
            if (manager == null || manager.ApplyingRemoteDamage)
                return true;

            Enemy enemy = __instance.GetComponentInParent<Enemy>();
            if (enemy == null || !EnemyManager.IsSyncableEnemy(enemy))
                return true;

            if (!Main.Multiplayer.NetworkManager.IsConnected)
                return true;

            // Scene host keeps vanilla damage; state broadcast happens in EnemyManager.Update
            if (manager.IsSceneHost)
                return true;

            float amount = hit.DamageAmount;
            if (amount <= 0f)
                return true;

            if (manager.TryInterceptDamage(enemy, amount))
                return false;

            return true;
        }
    }
}

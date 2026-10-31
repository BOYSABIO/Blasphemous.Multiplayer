using System;
using System.Collections.Generic;
using System.Reflection;
using Blasphemous.Multiplayer.Client.Players;
using Framework.Managers;
using Gameplay.GameControllers.Enemies.Framework.Attack;
using Gameplay.GameControllers.Entities;
using Gameplay.GameControllers.Penitent;
using HarmonyLib;
using Tools.Level.Layout;

namespace Blasphemous.Multiplayer.Client.Enemies;

/// <summary>
/// Intercept the complete IDamageable call before guard, execution, HP and death effects.
/// DamageArea.TakeDamage is only the base event hook; blocking it does not block
/// EnemyDamageArea.TakeDamageAmount and previously forwarded the same hit twice.
/// </summary>
[HarmonyPatch]
internal static class Enemy_Hit_Patch
{
    internal static IEnumerable<MethodBase> TargetMethods()
    {
        foreach (Type type in typeof(Enemy).Assembly.GetTypes())
        {
            if (!typeof(Enemy).IsAssignableFrom(type)) continue;
            foreach (MethodInfo method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic
                | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                ParameterInfo[] args = method.GetParameters();
                if (!method.IsAbstract && (method.Name == "Damage" || method.Name.EndsWith(".Damage", StringComparison.Ordinal))
                    && method.ReturnType == typeof(void) && args.Length == 1 && args[0].ParameterType == typeof(Hit))
                    yield return method;
            }
        }
    }

    public static bool Prefix(Enemy __instance, Hit __0)
    {
        var manager = Main.Multiplayer?.EnemyManager;
        return manager == null || !manager.TryInterceptHit(__instance, __0);
    }
}

/// <summary>Scripted damage that does not enter through IDamageable still uses authority.</summary>
[HarmonyPatch(typeof(Entity), nameof(Entity.Damage))]
internal static class Enemy_EntityDamage_Patch
{
    public static bool Prefix(Entity __instance, float amount)
    {
        var manager = Main.Multiplayer?.EnemyManager;
        return manager == null || !manager.TryInterceptDamage(__instance as Enemy, amount);
    }
}

/// <summary>Some contact routines hardcode Penitent even after AI targets a remote avatar.</summary>
[HarmonyPatch(typeof(EnemyAttack), nameof(EnemyAttack.ContactAttack))]
internal static class Enemy_ContactAttack_Patch
{
    public static bool Prefix(EnemyAttack __instance, ref IDamageable damageable)
    {
        var manager = Main.Multiplayer?.EnemyManager;
        if (manager == null || !Main.Multiplayer.NetworkManager.IsConnected || !manager.IsSceneHost
            || !(damageable is Penitent))
            return true;
        Enemy enemy = __instance.GetComponentInParent<Enemy>();
        if (!EnemyManager.IsSyncableEnemy(enemy) || EnemyManager.IsBossEnemy(enemy) || enemy.Target == null)
            return true;
        OtherPlayerScript remote = enemy.Target.GetComponentInParent<OtherPlayerScript>();
        if (remote == null || remote.IsDead)
            return true;
        var contact = enemy.GetComponentInChildren<ContactDamage>()?.ContactDamageCollider;
        var local = Core.Logic.Penitent?.EntityDamageArea?.DamageAreaCollider;
        var other = remote.GetComponent<UnityEngine.BoxCollider2D>();
        if (contact == null || local == null || other == null)
            return true;
        if (contact.enabled && contact.IsTouching(local))
            return true;
        if (!contact.enabled || !contact.IsTouching(other))
            return false;
        damageable = remote;
        return true;
    }
}

/// <summary>Enemy update methods can restart the behaviour tree and movement controller.</summary>
[HarmonyPatch]
internal static class Enemy_PuppetUpdate_Patch
{
    internal static IEnumerable<MethodBase> TargetMethods()
    {
        foreach (Type type in typeof(Enemy).Assembly.GetTypes())
        {
            if (!typeof(Enemy).IsAssignableFrom(type)) continue;
            MethodInfo method = type.GetMethod("OnUpdate", BindingFlags.Public | BindingFlags.NonPublic
                | BindingFlags.Instance | BindingFlags.DeclaredOnly, null, Type.EmptyTypes, null);
            if (method != null && !method.IsAbstract)
                yield return method;
        }
    }

    public static bool Prefix(Enemy __instance)
    {
        return Main.Multiplayer?.EnemyManager == null || !Main.Multiplayer.EnemyManager.ShouldSuppressEnemyAI(__instance);
    }
}

[HarmonyPatch(typeof(EnemySpawnPoint), nameof(EnemySpawnPoint.CreateEnemy))]
internal static class Enemy_SpawnPoint_Create_Patch
{
    public static void Postfix()
    {
        // CreateEnemy can run before Unity Start initializes the enemy components.
        Main.Multiplayer?.EnemyManager?.QueueRescan();
    }
}

using System;
using System.Collections.Generic;
using System.Reflection;
using Blasphemous.ModdingAPI;
using Framework.Managers;
using Gameplay.GameControllers.Bosses.BossFight;
using Gameplay.GameControllers.Bosses.ElderBrother;
using Gameplay.GameControllers.Entities;
using HarmonyLib;
using UnityEngine;

namespace Blasphemous.Multiplayer.Client.Enemies;

// Deliberately limited to ElderBrother. Other encounters retain the release's
// exclusions until their decisions, projectiles and arena scripts are proven.
internal sealed class WardenSyncAdapter
{
    private struct ColliderState { public Collider2D Collider; public bool Enabled; }
    private static readonly Dictionary<int, WardenSyncAdapter> Instances = new Dictionary<int, WardenSyncAdapter>();
    private static readonly PropertyInfo BehaviourTree = AccessTools.Property(typeof(Gameplay.GameControllers.Enemies.Framework.IA.EnemyBehaviour), "BehaviourTree");
    private static readonly MethodInfo StartWaiting = AccessTools.Method(typeof(ElderBrotherBehaviour), "StartWaitingPeriod");
    private readonly ElderBrother _enemy;
    private readonly ElderBrotherBehaviour _behaviour;
    private readonly bool _behaviourEnabled;
    private readonly bool _controllerEnabled;
    private readonly bool _colliderEnabled;
    private readonly bool _fireEvents;
    private readonly bool _rootMotion;
    private readonly bool _treeWasRunning;
    private readonly List<ColliderState> _colliders = new List<ColliderState>();
    private bool _puppet;
    private bool _introPending;
    private bool _hasRemoteState;
    private bool _warnedDeathBlocked;
    private bool _authorityObserved;
    private BossFightManager _fight;

    public static bool Supports(Enemy enemy) => enemy is ElderBrother;

    public static WardenSyncAdapter For(Enemy enemy)
    {
        if (!Supports(enemy)) return null;
        int key = enemy.GetInstanceID();
        if (!Instances.TryGetValue(key, out var adapter))
        {
            adapter = new WardenSyncAdapter((ElderBrother)enemy);
            Instances.Add(key, adapter);
        }
        return adapter;
    }

    public static void Clear() => Instances.Clear();
    public static void Prune()
    {
        var stale = new List<int>();
        foreach (var pair in Instances)
            if (pair.Value._enemy == null) stale.Add(pair.Key);
        foreach (int key in stale) Instances.Remove(key);
    }
    public static void RestoreAll()
    {
        foreach (WardenSyncAdapter adapter in Instances.Values) adapter.SetPuppet(false);
    }

    public static bool Suppress(ElderBrother enemy)
    {
        return enemy != null && Main.Multiplayer != null && Main.Multiplayer.CurrentlyInLevel
            && Main.Multiplayer.NetworkManager.IsConnected && Main.Multiplayer.EnemyManager != null
            && !Main.Multiplayer.EnemyManager.IsSceneHost;
    }

    public void RememberIntro()
    {
        if (!_introPending) ModLog.Info($"[WardenSync] Intro requested actor={_enemy.Id} puppet={_puppet} remote={_hasRemoteState}");
        _introPending = true;
    }

    private WardenSyncAdapter(ElderBrother enemy)
    {
        _enemy = enemy;
        // ElderBrother assigns Behaviour, but leaves base EnemyBehaviour null.
        _behaviour = enemy.Behaviour ?? enemy.GetComponent<ElderBrotherBehaviour>();
        _behaviourEnabled = _behaviour != null && _behaviour.enabled;
        _controllerEnabled = enemy.Controller != null && enemy.Controller.enabled;
        _colliderEnabled = enemy.Collider != null && enemy.Collider.enabled;
        _fireEvents = enemy.Animator == null || enemy.Animator.fireEvents;
        _rootMotion = enemy.Animator != null && enemy.Animator.applyRootMotion;
        if (_behaviour != null && BehaviourTree != null)
        {
            object tree = BehaviourTree.GetValue(_behaviour, null);
            PropertyInfo running = tree == null ? null : AccessTools.Property(tree.GetType(), "isRunning");
            _treeWasRunning = running != null && (bool)running.GetValue(tree, null);
        }
        foreach (Collider2D collider in enemy.GetComponentsInChildren<Collider2D>(true))
        {
            if (collider != null && (collider.GetComponentInParent<ContactDamage>() != null
                || collider.GetComponentInParent<AttackArea>() != null))
                _colliders.Add(new ColliderState { Collider = collider, Enabled = collider.enabled });
        }
    }

    public void SetPuppet(bool puppet)
    {
        if (_enemy == null || _enemy.Dead) return;
        if (!_authorityObserved || _puppet != puppet)
        {
            ModLog.Info($"[WardenSync] Authority puppet={puppet} actor={_enemy.Id} pendingIntro={_introPending} remote={_hasRemoteState} originalTree={_treeWasRunning} fireEvents={_enemy.Animator?.fireEvents}");
            _authorityObserved = true;
        }
        if (puppet)
        {
            if (_behaviour != null)
            {
                _behaviour.StopBehaviour();
                if (!_puppet)
                {
                    _behaviour.StopAllCoroutines();
                    _behaviour.jumpAttack?.StopJump();
                }
                _behaviour.enabled = false;
            }
            if (_enemy.Controller != null) _enemy.Controller.enabled = false;
            if (_enemy.Collider != null) _enemy.Collider.enabled = false;
            foreach (ColliderState state in _colliders)
                if (state.Collider != null) state.Collider.enabled = false;
            if (_enemy.Animator != null) { _enemy.Animator.fireEvents = false; _enemy.Animator.applyRootMotion = false; }
        }
        else if (_puppet)
        {
            if (_enemy.Controller != null) _enemy.Controller.enabled = _controllerEnabled;
            if (_enemy.Collider != null) _enemy.Collider.enabled = _colliderEnabled;
            foreach (ColliderState state in _colliders)
                if (state.Collider != null) state.Collider.enabled = state.Enabled;
            if (_enemy.Animator != null) { _enemy.Animator.fireEvents = _fireEvents; _enemy.Animator.applyRootMotion = _rootMotion; }
            if (_behaviour != null)
            {
                _behaviour.enabled = _behaviourEnabled;
                if (_hasRemoteState)
                {
                    // A promoted replica must become available for a new native
                    // decision rather than replay an old jump/coroutine.
                    StartWaiting?.Invoke(_behaviour, new object[] { 0f });
                    _behaviour.StartBehaviour();
                    _introPending = false;
                }
                else if (_introPending)
                {
                    _introPending = false;
                    _behaviour.IntroJump();
                }
                else if (_treeWasRunning) _behaviour.StartBehaviour();
            }
        }
        _puppet = puppet;
    }

    public void ApplyHealth(float life)
    {
        if (_enemy == null || _enemy.Dead || float.IsNaN(life) || float.IsInfinity(life)) return;
        _hasRemoteState = true;
        _enemy.CurrentLife = Mathf.Max(0f, life);
        FindFight()?.SetCurrentActivePhase();
    }

    public void ApplyAnimation(int hash, float normalizedTime)
    {
        if (_enemy == null || _enemy.Dead || _enemy.Animator == null || hash == 0
            || float.IsNaN(normalizedTime) || float.IsInfinity(normalizedTime)) return;
        Animator animator = _enemy.Animator;
        if (!animator.HasState(0, hash)) return;
        animator.enabled = true;
        AnimatorStateInfo current = animator.GetCurrentAnimatorStateInfo(0);
        float remote = current.shortNameHash == hash && current.loop ? normalizedTime % 1f : normalizedTime;
        float local = current.loop ? current.normalizedTime % 1f : current.normalizedTime;
        if (current.shortNameHash != hash || Mathf.Abs(local - remote) > 0.12f)
        {
            animator.Play(hash, 0, remote);
            animator.Update(0f);
        }
    }

    // Caller sets ApplyingRemoteDamage so the native Hit is not forwarded back.
    // Never substitute Kill()/object hiding for the encounter's normal death.
    public bool ApplyDeath()
    {
        if (_enemy == null || _enemy.Dead) return true;
        if (Core.Logic.Penitent == null || Core.Logic.Penitent.Stats.Life.Current <= 0f)
        {
            if (!_warnedDeathBlocked) ModLog.Warn("[WardenSync] Native death deferred while local player is unavailable/dead");
            _warnedDeathBlocked = true;
            return false;
        }
        _warnedDeathBlocked = false;
        if (_enemy.Animator != null) { _enemy.Animator.enabled = true; _enemy.Animator.fireEvents = _fireEvents; }
        ((IDamageable)_enemy).Damage(new Hit
        {
            AttackingEntity = Core.Logic.Penitent.gameObject,
            DamageAmount = Mathf.Ceil(_enemy.CurrentLife + _enemy.Stats.Defense.Final + 1f),
            Unblockable = true, Unparriable = true, Unnavoidable = true
        });
        return _enemy == null || _enemy.Dead;
    }

    private BossFightManager FindFight()
    {
        if (_fight != null) return _fight;
        foreach (BossFightManager fight in UnityEngine.Object.FindObjectsOfType<BossFightManager>())
            if (fight.Boss == _enemy) { _fight = fight; break; }
        return _fight;
    }
}

[HarmonyPatch(typeof(ElderBrother), "Damage")]
internal static class Warden_HitAuthority_Patch
{
    private static bool Prefix(ElderBrother __instance, Hit hit)
    {
        EnemyManager manager = Main.Multiplayer?.EnemyManager;
        if (!WardenSyncAdapter.Suppress(__instance) || manager == null || manager.ApplyingRemoteDamage) return true;
        if (!manager.TryInterceptDamage(__instance, hit)) return true;
        manager.PlayLocalHitPresentation(__instance, hit);
        return false;
    }
}

[HarmonyPatch(typeof(ElderBrother), "OnUpdate")]
internal static class Warden_PuppetUpdate_Patch
{
    private static bool Prefix(ElderBrother __instance) => !WardenSyncAdapter.Suppress(__instance);
}

[HarmonyPatch]
internal static class Warden_DecisionAuthority_Patch
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        foreach (string name in new[] { "IntroJump", "LaunchAction", "JumpAttack", "AreaAttack" })
        {
            MethodInfo method = AccessTools.Method(typeof(ElderBrotherBehaviour), name);
            if (method != null) yield return method;
        }
    }
    private static bool Prefix(ElderBrotherBehaviour __instance, MethodBase __originalMethod)
    {
        ElderBrother enemy = __instance.GetComponent<ElderBrother>();
        if (WardenNetwork.ReplayingEncounter && __originalMethod.Name == "IntroJump") return false;
        // Remember an intro that begins just before level/authority callbacks;
        // waiting for the server may interrupt its first coroutine frame.
        if (__originalMethod.Name == "IntroJump" && Main.Multiplayer != null
            && Main.Multiplayer.NetworkManager.IsConnected && enemy != null)
            WardenSyncAdapter.For(enemy).RememberIntro();
        if (!WardenSyncAdapter.Suppress(enemy)) return true;
        return false;
    }
}

[HarmonyPatch(typeof(ElderBrotherBehaviour), "GetTargetPredictedPos")]
internal static class Warden_RemotePrediction_Patch
{
    private static bool Prefix(ElderBrotherBehaviour __instance, ref Vector3 __result)
    {
        ElderBrother enemy = __instance.GetComponent<ElderBrother>();
        if (enemy == null || Main.Multiplayer == null || !Main.Multiplayer.NetworkManager.IsConnected
            || Main.Multiplayer.EnemyManager == null || !Main.Multiplayer.EnemyManager.IsSceneHost || enemy.Target == null) return true;
        var remote = enemy.Target.GetComponentInParent<Blasphemous.Multiplayer.Client.Players.OtherPlayerScript>();
        if (remote == null || remote.IsDead) return true;
        Vector2 point = remote.LatestNetworkPosition;
        // No trustworthy remote velocity is currently transmitted. Use the
        // latest real network position instead of predicting the host player.
        __result = new Vector3(point.x, point.y, enemy.Target.transform.position.z);
        ModLog.Info($"[WardenSync] Jump target={remote.PlayerName} pos={point}");
        return false;
    }
}

[HarmonyPatch(typeof(ElderBrotherIntroDummy), "TriggerIntro")]
internal static class Warden_DummyIntroTrace_Patch
{
    private static void Prefix(ElderBrotherIntroDummy __instance)
    {
        if (Main.Multiplayer != null && Main.Multiplayer.NetworkManager.IsConnected)
            ModLog.Info($"[WardenSync] Native dummy intro actor={__instance.name} host={Main.Multiplayer.EnemyManager?.IsSceneHost} fireEvents={__instance.animator?.fireEvents}");
    }
}

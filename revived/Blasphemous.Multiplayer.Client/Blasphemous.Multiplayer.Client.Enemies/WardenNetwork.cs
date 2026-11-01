using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Blasphemous.ModdingAPI;
using Blasphemous.Multiplayer.Common.Networking;
using Framework.Managers;
using Gameplay.GameControllers.Bosses.BossFight;
using Gameplay.GameControllers.Bosses.ElderBrother;
using Gameplay.GameControllers.Bosses.Quirce.Attack;
using Gameplay.GameControllers.Entities;
using HarmonyLib;
using UnityEngine;

namespace Blasphemous.Multiplayer.Client.Enemies;

internal static class WardenNetwork
{
    private sealed class Entry
    {
        public BossSpawnedAreaAttack Area;
        public BossHazardData Data;
        public bool Replica;
        public bool PromotedReplica;
        public bool EndingResumed;
        public readonly Dictionary<Collider2D, bool> Colliders = new Dictionary<Collider2D, bool>();
        public uint ReceivedTerm, ReceivedSequence;
    }
    private sealed class PendingSpawn
    {
        public ElderBrother Owner;
        public BossSpawnedAreaAttack Area;
        public WardenAttackKind Kind;
        public short Prefab;
        public float Strength;
    }
    private sealed class DeferredHazard { public BossHazardData Data; public float ReceivedAt; }
    private static readonly FieldInfo Stage = AccessTools.Field(typeof(BossSpawnedAreaAttack), "_state");
    private static readonly FieldInfo Timer = AccessTools.Field(typeof(BossSpawnedAreaAttack), "timer");
    private static readonly FieldInfo Preparation = AccessTools.Field(typeof(BossSpawnedAreaAttack), "actualPreparationSeconds");
    private static readonly FieldInfo Loop = AccessTools.Field(typeof(BossSpawnedAreaAttack), "actualLoopSeconds");
    private static readonly List<PendingSpawn> Pending = new List<PendingSpawn>();
    private static readonly Dictionary<ulong, Entry> Hazards = new Dictionary<ulong, Entry>();
    private static readonly HashSet<ulong> RetiredHazards = new HashSet<ulong>();
    private static readonly Queue<ulong> RetiredOrder = new Queue<ulong>();
    private static readonly Dictionary<int, Entry> ByInstance = new Dictionary<int, Entry>();
    private static readonly Dictionary<int, BossEncounterState> EncounterStates = new Dictionary<int, BossEncounterState>();
    private static readonly HashSet<int> FinishedEncounters = new HashSet<int>();
    private static readonly Dictionary<int, PlayMakerFSM> NativeEncounterFsms = new Dictionary<int, PlayMakerFSM>();
    private static readonly HashSet<int> MissingNativeFsmLogged = new HashSet<int>();
    private static readonly Dictionary<int, BossEncounterData> PendingEncounters = new Dictionary<int, BossEncounterData>();
    private static readonly Dictionary<ulong, DeferredHazard> PendingHazards = new Dictionary<ulong, DeferredHazard>();
    private static uint _sequence, _hazardCounter, _receiveTerm, _receiveSequence, _observedTerm, _emittedTerm;
    private static float _sendAt, _clockOffset;
    private static bool _clockKnown;
    public static bool ReplayingEncounter { get; private set; }
    public static bool ApplyingHazard { get; private set; }
    public static int ActiveHazardCount => Hazards.Count;
    public static int ReplicaHazardCount => Hazards.Values.Count(entry => entry.Replica);
    private static EnemyManager Manager => Main.Multiplayer?.EnemyManager;
    private static bool Connected => Main.Multiplayer != null && Main.Multiplayer.CurrentlyInLevel && Main.Multiplayer.NetworkManager.IsConnected;
    private static bool Host => Connected && Manager != null && Manager.IsSceneHost && Manager.HostTerm != 0;
    private static int Scene => EnemySyncCodec.SceneHash(Core.LevelManager.currentLevel.LevelName);

    public static void NoteHostClock(float hostTime)
    { if (!Host && !float.IsNaN(hostTime) && !float.IsInfinity(hostTime)) { _clockOffset = Time.time - hostTime; _clockKnown = true; } }

    public static void Clear()
    {
        foreach (Entry entry in Hazards.Values.ToArray()) if (entry.Replica) RecycleReplica(entry);
        Hazards.Clear(); ByInstance.Clear(); RetiredHazards.Clear(); RetiredOrder.Clear(); Pending.Clear(); EncounterStates.Clear(); FinishedEncounters.Clear(); NativeEncounterFsms.Clear(); MissingNativeFsmLogged.Clear(); PendingEncounters.Clear(); PendingHazards.Clear();
        _clockKnown = false; _receiveTerm = 0; _receiveSequence = 0; _observedTerm = 0; _emittedTerm = 0; _sendAt = 0;
    }

    private static void ObserveAuthority()
    {
        uint term = Manager.HostTerm;
        if (_observedTerm == term) return;
        foreach (var pending in PendingEncounters.Values.ToArray())
            if (pending.Term != term || pending.SceneHash != Scene) PendingEncounters.Remove(pending.SpawnKey);
        foreach (var pending in PendingHazards.Values.ToArray())
            if (pending.Data.Term != term || pending.Data.SceneHash != Scene) PendingHazards.Remove(pending.Data.Id);
        _clockKnown = false;
        _receiveTerm = term; _receiveSequence = 0; _observedTerm = term;
    }

    public static void Tick()
    {
        if (!Connected || Manager == null || Manager.HostTerm == 0) return;
        ObserveAuthority();
        foreach (BossEncounterData encounter in PendingEncounters.Values.ToArray())
            if (ApplyEncounter(encounter)) PendingEncounters.Remove(encounter.SpawnKey);
        foreach (DeferredHazard hazard in PendingHazards.Values.ToArray())
            if (ApplyHazard(hazard.Data, Time.time - hazard.ReceivedAt)) PendingHazards.Remove(hazard.Data.Id);
        foreach (Entry entry in Hazards.Values.ToArray())
        {
            if (Host && entry.Replica) { SetReplica(entry, false); entry.PromotedReplica = true; ModLog.Info("[WardenHazard] Promoted " + entry.Data.Id); }
            else if (!Host && !entry.Replica) SetReplica(entry, true);
        }
        if (!Host) { Pending.Clear(); return; }
        // Delay creation capture until LateUpdate: the native summon coroutine
        // adjusts vertical displacement/sorting after InstantiateArea returns.
        foreach (PendingSpawn spawn in Pending.ToArray())
        {
            if (spawn.Owner == null || spawn.Area == null || !spawn.Area.gameObject.activeInHierarchy || Hazards.Count >= BossSyncCodec.MaxHazards) continue;
            ulong id = ((ulong)Manager.HostTerm << 32) | ++_hazardCounter;
            var entry = new Entry { Area = spawn.Area, Data = new BossHazardData { Id = id,
                SpawnKey = EnemyManager.ComputeStableNetId(spawn.Owner), Kind = spawn.Kind, PrefabIndex = spawn.Prefab, Strength = spawn.Strength } };
            if (ByInstance.TryGetValue(spawn.Area.GetInstanceID(), out var previous)) SendInactive(previous);
            Hazards[id] = entry; ByInstance[spawn.Area.GetInstanceID()] = entry;
            SendHazard(entry);
            ModLog.Info($"[WardenHazard] Created {id} kind={spawn.Kind} prefab={spawn.Prefab} pos={spawn.Area.transform.position}");
        }
        Pending.Clear();
        if (Time.time < _sendAt) return;
        _sendAt = Time.time + 0.1f;
        foreach (BossFightManager fight in UnityEngine.Object.FindObjectsOfType<BossFightManager>())
            if (fight.Boss is ElderBrother warden)
                EmitEncounter(warden, warden.Dead ? BossEncounterState.Finished : fight.IsFightStarted ? BossEncounterState.Started : BossEncounterState.Waiting);
        foreach (Entry entry in Hazards.Values.ToArray())
        {
            if (entry.Area == null || !entry.Area.gameObject.activeInHierarchy) SendInactive(entry);
            else if (entry.PromotedReplica && Convert.ToByte(Stage.GetValue(entry.Area)) == 2 && entry.Area.Animator != null)
            {
                AnimatorStateInfo animation = entry.Area.Animator.GetCurrentAnimatorStateInfo(0);
                if (animation.loop && !entry.EndingResumed)
                {
                    // Replication can advance the native stage before its END
                    // trigger/transition arrives. OnUpdate has no ENDING case,
                    // so promotion must finish that authored transition once.
                    entry.EndingResumed = true;
                    entry.Area.Animator.SetTrigger("END");
                }
                if (!animation.loop && animation.normalizedTime >= 1f)
                {
                    // The end event may already have run while recycling was
                    // suppressed on the replica.
                    SendInactive(entry); RecycleReplica(entry);
                }
                else SendHazard(entry);
            }
            else SendHazard(entry);
        }
    }

    public static void EncounterChanged(BossFightManager fight)
    {
        if (!ReplayingEncounter && Host && fight.Boss is ElderBrother warden)
            EmitEncounter(warden, warden.Dead ? BossEncounterState.Finished : fight.IsFightStarted ? BossEncounterState.Started : BossEncounterState.Waiting);
    }
    private static void EmitEncounter(ElderBrother warden, BossEncounterState state)
    {
        if (_emittedTerm != Manager.HostTerm) { EncounterStates.Clear(); _emittedTerm = Manager.HostTerm; }
        int key = EnemyManager.ComputeStableNetId(warden);
        if (state == BossEncounterState.Finished) RememberFinished(key);
        if (EncounterStates.TryGetValue(key, out var previous) && previous == state) return;
        EncounterStates[key] = state;
        Main.Multiplayer.NetworkManager.SendBossPacket(BossSyncCodec.EncodeEncounter(new BossEncounterData {
            SceneHash = Scene, Term = Manager.HostTerm, Sequence = ++_sequence, SpawnKey = key, State = state }), false);
        ModLog.Info($"[WardenEncounter] Host {state} actor={key} term={Manager.HostTerm}");
    }

    public static void Receive(string sender, bool isHazard, byte[] bytes, int offset)
    {
        BossEncounterData encounter = null; BossHazardData hazard = null;
        bool valid = isHazard ? BossSyncCodec.TryReadHazard(bytes, offset, out hazard) : BossSyncCodec.TryReadEncounter(bytes, offset, out encounter);
        BossEncounterData header = encounter ?? hazard;
        if (!valid || !Connected || Manager == null || !Manager.AcceptBossAuthority(sender, header.SceneHash, header.Term)) return;
        ObserveAuthority();
        if (_receiveTerm != header.Term) { _receiveTerm = header.Term; _receiveSequence = 0; }
        if (header.Sequence != 0 && header.Sequence <= _receiveSequence) return;
        if (header.Sequence != 0) _receiveSequence = header.Sequence;
        if (hazard != null)
        {
            if (!ApplyHazard(hazard) && (PendingHazards.ContainsKey(hazard.Id) || PendingHazards.Count < BossSyncCodec.MaxHazards))
                PendingHazards[hazard.Id] = new DeferredHazard { Data = hazard, ReceivedAt = Time.time };
        }
        else
        {
            if (encounter.State == BossEncounterState.Finished) RememberFinished(encounter.SpawnKey);
            else if (FinishedEncounters.Contains(encounter.SpawnKey)) return;
            if (!ApplyEncounter(encounter) && (PendingEncounters.ContainsKey(encounter.SpawnKey) || PendingEncounters.Count < BossSyncCodec.MaxEncounters))
                PendingEncounters[encounter.SpawnKey] = encounter;
        }
    }

    private static void RememberFinished(int spawnKey)
    {
        if (FinishedEncounters.Count < BossSyncCodec.MaxEncounters) FinishedEncounters.Add(spawnKey);
    }

    private static ElderBrother FindWarden(int spawnKey)
    {
        foreach (ElderBrother warden in UnityEngine.Object.FindObjectsOfType<ElderBrother>())
            if (EnemyManager.ComputeStableNetId(warden) == spawnKey) return warden;
        return null;
    }

    internal static bool EnsureNativeEncounterReady(ElderBrother warden)
    {
        if (!Connected || warden == null || warden.Dead) return true;
        BossFightManager fight = UnityEngine.Object.FindObjectsOfType<BossFightManager>().FirstOrDefault(candidate => candidate.Boss == warden);
        if (fight == null) return false;
        if (Core.LevelManager.currentLevel.LevelName != "D17Z01S11")
        {
            // Other Warden arenas retain their existing manager-based start.
            if (!fight.IsFightStarted) ReplayNativeStart(() => fight.StartBossFight());
            return fight.IsFightStarted;
        }

        int key = fight.GetInstanceID();
        if (!NativeEncounterFsms.TryGetValue(key, out var fsm) || fsm == null)
        {
            foreach (PlayMakerFSM candidate in fight.GetComponents<PlayMakerFSM>())
            {
                var combat = candidate.Fsm.GetState("Start Combat");
                var listener = candidate.Fsm.GetState("Is Boss Dead?");
                if (combat == null || listener == null) continue;
                bool startsThisFight = combat.Actions.OfType<HutongGames.PlayMaker.Actions.CallMethod>()
                    .Any(action => action.methodName.Value == "StartBossFight" && action.behaviour.Value == fight);
                if (!startsThisFight || !listener.Actions.Any(action => action is Tools.Playmaker2.Events.EntityDead)) continue;
                fsm = candidate;
                if (NativeEncounterFsms.Count < BossSyncCodec.MaxEncounters) NativeEncounterFsms[key] = fsm;
                break;
            }
        }
        if (fsm == null)
        {
            if (MissingNativeFsmLogged.Count < BossSyncCodec.MaxEncounters && MissingNativeFsmLogged.Add(key))
                ModLog.Warn("[WardenEncounter] Waiting for the verified native story encounter FSM");
            return false;
        }

        string state = fsm.ActiveStateName;
        if (state == "Is Boss Dead?") return true;
        // Let level-ready/ambience initialization finish first. Never restart
        // the combat listener or any native completion/cutscene state.
        if (state != "Wait For Player" && state != "Is Penitent?" && state != "Wait a second (Input Block)"
            && state != "TRIGGER INTRO" && state != "Wait before combat starts") return false;
        ReplayNativeStart(() => fsm.SetState("Start Combat"));
        ModLog.Info("[WardenEncounter] Applied native story FSM start actor=" + EnemyManager.ComputeStableNetId(warden));
        return fsm.ActiveStateName == "Is Boss Dead?";
    }

    private static void ReplayNativeStart(Action start)
    {
        bool previous = ReplayingEncounter; ReplayingEncounter = true;
        try { start(); }
        finally { ReplayingEncounter = previous; }
    }

    private static bool ApplyEncounter(BossEncounterData data)
    {
        ElderBrother warden = FindWarden(data.SpawnKey);
        if (warden == null) return false;
        BossFightManager fight = UnityEngine.Object.FindObjectsOfType<BossFightManager>().FirstOrDefault(candidate => candidate.Boss == warden);
        if (fight == null) return false;
        // Cached replay has sequence zero and can arrive after a newer live
        // finish. Death is terminal for this actor until the level is cleared,
        // including when native death must be deferred for a dead local player.
        if (data.State == BossEncounterState.Finished)
        {
            RememberFinished(data.SpawnKey);
        }
        else if (warden.Dead || FinishedEncounters.Contains(data.SpawnKey)) return true;
        if (data.State == BossEncounterState.Started)
        {
            if (!EnsureNativeEncounterReady(warden)) return false;
            // The host's dummy has left the camera by this point. Complete only
            // its presentation on replicas, preserving arena objects/events.
            foreach (ElderBrotherIntroDummy dummy in UnityEngine.Object.FindObjectsOfType<ElderBrotherIntroDummy>())
                if (dummy.elderBro == warden.Behaviour)
                {
                    dummy.StopAllCoroutines();
                    foreach (Renderer renderer in dummy.GetComponentsInChildren<Renderer>(true)) renderer.enabled = false;
                    if (dummy.animator != null) dummy.animator.enabled = false;
                }
        }
        else if (data.State == BossEncounterState.Finished && !warden.Dead)
        {
            bool previous = Manager.ApplyingRemoteDamage; Manager.ApplyingRemoteDamage = true;
            try { if (!WardenSyncAdapter.For(warden).ApplyDeath()) return false; }
            finally { Manager.ApplyingRemoteDamage = previous; }
        }
        // Received/replayed state must never populate the emission cache: a
        // promoted replica must announce the native state in its new term.
        return true;
    }

    public static void CapturedSpawn(BossAreaSummonAttack attack, GameObject prefab, GameObject spawned, float strength)
    {
        if (ApplyingHazard || !Connected || spawned == null) return;
        BossSpawnedAreaAttack area = spawned.GetComponent<BossSpawnedAreaAttack>();
        ElderBrother owner = area == null ? null : area.WeaponOwner as ElderBrother;
        if (owner == null || owner.Behaviour == null) return;
        WardenAttackKind kind;
        if (owner.Behaviour.areaAttack == attack) kind = WardenAttackKind.Area;
        else if (owner.Behaviour.maceImpact == attack) kind = WardenAttackKind.Mace;
        else return;
        if (!Host) { area.gameObject.SetActive(false); ModLog.Warn("[WardenHazard] Suppressed unexpected guest-native spawn"); return; }
        int index = -1;
        if (prefab != attack.areaPrefab)
        {
            index = attack.areaPrefabs == null ? -1 : attack.areaPrefabs.IndexOf(prefab);
            if (index < 0 || index >= 64) return;
        }
        Pending.Add(new PendingSpawn { Owner = owner, Area = area, Kind = kind, Prefab = (short)index, Strength = strength });
    }

    private static void SendHazard(Entry entry)
    {
        BossSpawnedAreaAttack area = entry.Area;
        BossHazardData data = entry.Data;
        data.Active = true; data.SceneHash = Scene; data.Term = Manager.HostTerm; data.Sequence = ++_sequence;
        Vector3 point = area.transform.position; data.X = point.x; data.Y = point.y; data.Z = point.z; data.Angle = area.transform.eulerAngles.z;
        data.Stage = Convert.ToByte(Stage.GetValue(area)); data.Timer = (float)Timer.GetValue(area);
        data.Preparation = (float)Preparation.GetValue(area); data.Loop = (float)Loop.GetValue(area); data.Damage = area.DamageAmount; data.HostTime = Time.time;
        if (area.SpriteRenderer != null) { data.FlipX = area.SpriteRenderer.flipX; data.SortingOrder = area.SpriteRenderer.sortingOrder; }
        if (area.Animator != null)
        {
            AnimatorStateInfo animation = area.Animator.GetCurrentAnimatorStateInfo(0);
            data.AnimationHash = animation.shortNameHash; data.AnimationTime = Mathf.Max(0, animation.normalizedTime); data.AnimationLength = Mathf.Max(0, animation.length);
        }
        Main.Multiplayer.NetworkManager.SendBossPacket(BossSyncCodec.EncodeHazard(data), true);
    }
    private static void SendInactive(Entry entry)
    {
        if (!Hazards.Remove(entry.Data.Id)) return;
        RememberRetired(entry.Data.Id);
        if (entry.Area != null) ByInstance.Remove(entry.Area.GetInstanceID());
        entry.Data.Active = false; entry.Data.Term = Manager.HostTerm; entry.Data.SceneHash = Scene; entry.Data.Sequence = ++_sequence;
        Main.Multiplayer.NetworkManager.SendBossPacket(BossSyncCodec.EncodeHazard(entry.Data), true);
        ModLog.Info("[WardenHazard] Despawned " + entry.Data.Id);
    }
    public static bool BeforeRecycle(BossSpawnedAreaAttack area)
    {
        if (!ByInstance.TryGetValue(area.GetInstanceID(), out var entry) || ApplyingHazard) return true;
        if (!Host && entry.Replica) return false;
        if (Host) SendInactive(entry);
        return true;
    }
    public static bool AllowDamage(BossSpawnedAreaAttack area)
    { return !ByInstance.ContainsKey(area.GetInstanceID()) || Host || !Connected; }

    private static void RememberRetired(ulong id)
    {
        if (!RetiredHazards.Add(id)) return;
        RetiredOrder.Enqueue(id);
        while (RetiredOrder.Count > BossSyncCodec.MaxHazards * 2) RetiredHazards.Remove(RetiredOrder.Dequeue());
    }

    private static bool ApplyHazard(BossHazardData data, float deferredAge = 0f)
    {
        if (!data.Active)
        {
            RememberRetired(data.Id);
            PendingHazards.Remove(data.Id);
            if (Hazards.TryGetValue(data.Id, out var dead)) { RecycleReplica(dead); Hazards.Remove(data.Id); if (dead.Area != null) ByInstance.Remove(dead.Area.GetInstanceID()); }
            return true;
        }
        if (RetiredHazards.Contains(data.Id)) return true;
        if (Hazards.TryGetValue(data.Id, out var known) && data.Sequence == 0 && known.ReceivedSequence != 0)
            return true; // Cached replay cannot rewind a live or promoted area.
        float age = data.Sequence == 0 ? Mathf.Max(0, data.HostTime + deferredAge)
            : _clockKnown ? Mathf.Max(0, Time.time - _clockOffset - data.HostTime) : 0;
        if (age > BossSyncCodec.HazardReplayLifetime(data)) return true;
        ElderBrother owner = FindWarden(data.SpawnKey);
        if (owner == null || owner.Behaviour == null) return false;
        if (!Hazards.TryGetValue(data.Id, out var entry))
        {
            if (Hazards.Count >= BossSyncCodec.MaxHazards) return true;
            BossAreaSummonAttack attack = data.Kind == WardenAttackKind.Area ? owner.Behaviour.areaAttack : owner.Behaviour.maceImpact;
            if (attack == null || (data.PrefabIndex >= 0 && (attack.areaPrefabs == null || data.PrefabIndex >= attack.areaPrefabs.Count))) return false;
            GameObject spawned;
            ApplyingHazard = true;
            try { spawned = data.PrefabIndex < 0 ? attack.SummonAreaOnPoint(new Vector3(data.X, data.Y, data.Z), data.Angle, data.Strength, null)
                : attack.SummonAreaOnPoint(data.PrefabIndex, new Vector3(data.X, data.Y, data.Z), data.Angle, data.Strength, null); }
            finally { ApplyingHazard = false; }
            BossSpawnedAreaAttack area = spawned == null ? null : spawned.GetComponent<BossSpawnedAreaAttack>();
            if (area == null) return false;
            entry = new Entry { Area = area, Data = data, PromotedReplica = Host && data.Sequence == 0 };
            foreach (Collider2D collider in area.GetComponentsInChildren<Collider2D>(true)) entry.Colliders[collider] = collider.enabled;
            Hazards[data.Id] = entry; ByInstance[area.GetInstanceID()] = entry;
            ModLog.Info($"[WardenHazard] Replayed {data.Id} kind={data.Kind} prefab={data.PrefabIndex}");
        }
        if (entry.ReceivedTerm == data.Term && data.Sequence == 0 && entry.ReceivedSequence != 0) return true;
        entry.ReceivedTerm = data.Term; entry.ReceivedSequence = data.Sequence; entry.Data = data;
        SetReplica(entry, !Host);
        if (entry.Area == null) return false;
        entry.Area.SetOwner(owner); entry.Area.SetCallbackOnLoopFinish(null); entry.Area.SetDamageStrength(data.Strength);
        entry.Area.DamageAmount = data.Damage; entry.Area.CreateHit(); entry.Area.SetCustomTimes(data.Preparation, data.Loop);
        entry.Area.transform.position = new Vector3(data.X, data.Y, data.Z); entry.Area.transform.rotation = Quaternion.Euler(0, 0, data.Angle);
        byte stage = data.Stage; float timer = data.Timer + age;
        if (stage == 0 && timer > data.Preparation) { timer -= data.Preparation; stage = 1; }
        if (stage == 1 && timer > data.Loop) { stage = 2; timer = data.Loop; }
        Stage.SetValue(entry.Area, Enum.ToObject(Stage.FieldType, stage)); Timer.SetValue(entry.Area, timer);
        if (entry.Area.SpriteRenderer != null) { entry.Area.SpriteRenderer.flipX = data.FlipX; entry.Area.SpriteRenderer.sortingOrder = data.SortingOrder; }
        if (entry.Area.Animator != null && data.AnimationHash != 0 && entry.Area.Animator.HasState(0, data.AnimationHash))
        {
            entry.Area.Animator.Play(data.AnimationHash, 0, data.AnimationTime + (data.AnimationLength > 0 ? age / data.AnimationLength : 0));
            entry.Area.Animator.Update(0);
        }
        return true;
    }
    private static void SetReplica(Entry entry, bool replica)
    {
        if (!replica && entry.Replica) { entry.PromotedReplica = true; entry.EndingResumed = false; }
        if (entry.Colliders.Count == 0 && entry.Area != null)
            foreach (Collider2D collider in entry.Area.GetComponentsInChildren<Collider2D>(true)) entry.Colliders[collider] = collider.enabled;
        foreach (var state in entry.Colliders) if (state.Key != null) state.Key.enabled = replica ? false : state.Value;
        entry.Replica = replica;
    }
    private static void RecycleReplica(Entry entry)
    {
        if (entry.Area == null) return;
        SetReplica(entry, false); ApplyingHazard = true;
        try { entry.Area.Recycle(); }
        finally { ApplyingHazard = false; }
    }
}

[HarmonyPatch(typeof(BossFightManager), "StartBossFight", new Type[] { typeof(bool) })]
internal static class Warden_EncounterStart_Patch
{ private static void Postfix(BossFightManager __instance) { WardenNetwork.EncounterChanged(__instance); } }
[HarmonyPatch(typeof(BossFightManager), "OnDeathBoss")]
internal static class Warden_EncounterFinish_Patch
{ private static void Postfix(BossFightManager __instance) { WardenNetwork.EncounterChanged(__instance); } }
[HarmonyPatch(typeof(BossAreaSummonAttack), "InstantiateArea")]
internal static class Warden_HazardSpawn_Patch
{
    private static void Postfix(BossAreaSummonAttack __instance, GameObject toInstantiate, float damageStrength, GameObject __result)
    { WardenNetwork.CapturedSpawn(__instance, toInstantiate, __result, damageStrength); }
}
[HarmonyPatch(typeof(BossSpawnedAreaAttack), "Recycle")]
internal static class Warden_HazardRecycle_Patch
{ private static bool Prefix(BossSpawnedAreaAttack __instance) => WardenNetwork.BeforeRecycle(__instance); }
[HarmonyPatch(typeof(BossSpawnedAreaAttack), "Attack")]
internal static class Warden_HazardDamage_Patch
{ private static bool Prefix(BossSpawnedAreaAttack __instance) => WardenNetwork.AllowDamage(__instance); }

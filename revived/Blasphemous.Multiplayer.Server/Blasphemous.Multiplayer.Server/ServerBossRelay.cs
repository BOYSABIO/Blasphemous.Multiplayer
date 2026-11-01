using System;
using System.Collections.Generic;
using System.Linq;
using Blasphemous.Multiplayer.Common.Enums;
using Blasphemous.Multiplayer.Common.Networking;

namespace Blasphemous.Multiplayer.Server;

public partial class Server
{
    private sealed class BossCache
    {
        public uint Term, LastSequence;
        public readonly Dictionary<int, byte[]> Encounters = new Dictionary<int, byte[]>();
        public readonly Dictionary<ulong, HazardCache> Hazards = new Dictionary<ulong, HazardCache>();
    }
    private sealed class HazardCache { public byte[] Body; public DateTime Received, Expires; }
    private readonly Dictionary<string, BossCache> _bossScenes = new Dictionary<string, BossCache>();

    private void ReceiveBossRelay(string ip, byte[] body, NetworkType type)
    {
        if (!connectedPlayers.TryGetValue(ip, out var player) || !TryGetSceneHost(player, out var authority)
            || player.name != authority.HostName) return;
        BossEncounterData encounter = null;
        BossHazardData hazard = null;
        bool valid = type == NetworkType.BossEncounter
            ? BossSyncCodec.TryReadEncounter(body, 0, out encounter)
            : BossSyncCodec.TryReadHazard(body, 0, out hazard);
        BossEncounterData header = encounter ?? hazard;
        if (!valid || header == null || header.SceneHash != EnemySyncCodec.SceneHash(player.sceneName)
            || header.Term != authority.Term || header.Sequence == 0) return;
        string key = SceneKey(player.room, player.sceneName);
        if (!_bossScenes.TryGetValue(key, out var cache)) { cache = new BossCache(); _bossScenes.Add(key, cache); }
        if (cache.Term != authority.Term) { cache.Term = authority.Term; cache.LastSequence = 0; }
        if (header.Sequence <= cache.LastSequence) return;
        ExpireBossHazards(cache);
        if (encounter != null)
        {
            if (!cache.Encounters.ContainsKey(encounter.SpawnKey) && cache.Encounters.Count >= BossSyncCodec.MaxEncounters) return;
            cache.Encounters[encounter.SpawnKey] = (byte[])body.Clone();
        }
        else if (hazard.Active)
        {
            if (!cache.Hazards.ContainsKey(hazard.Id) && cache.Hazards.Count >= BossSyncCodec.MaxHazards) return;
            DateTime received = DateTime.UtcNow;
            cache.Hazards[hazard.Id] = new HazardCache { Body = (byte[])body.Clone(), Received = received,
                Expires = received.AddSeconds(BossSyncCodec.HazardReplayLifetime(hazard)) };
        }
        else cache.Hazards.Remove(hazard.Id);
        cache.LastSequence = header.Sequence;
        byte[] named = EnemySyncCodec.PrefixName(player.name, body);
        foreach (var peer in connectedPlayers)
            if (peer.Key != ip && player.isInSameScene(peer.Value)) Send(peer.Key, named, type);
    }

    private static void ExpireBossHazards(BossCache cache)
    {
        foreach (ulong id in cache.Hazards.Where(pair => pair.Value.Expires <= DateTime.UtcNow).Select(pair => pair.Key).ToArray())
            cache.Hazards.Remove(id);
    }

    private void ReplayBossScene(string room, string scene, string targetIp)
    {
        string key = SceneKey(room, scene);
        if (!_bossScenes.TryGetValue(key, out var cache) || !_sceneHosts.TryGetValue(key, out var authority)) return;
        if (cache.Term != authority.Term) { cache.Term = authority.Term; cache.LastSequence = 0; }
        ExpireBossHazards(cache);
        foreach (var peer in connectedPlayers)
        {
            if ((targetIp != null && peer.Key != targetIp) || peer.Value.room != room || peer.Value.sceneName != scene) continue;
            foreach (byte[] body in cache.Encounters.Values)
                Send(peer.Key, EnemySyncCodec.PrefixName(authority.HostName, BossSyncCodec.RebaseForReplay(body, authority.Term)), NetworkType.BossEncounter);
            foreach (HazardCache hazard in cache.Hazards.Values)
                if (BossSyncCodec.TryBuildHazardReplay(hazard.Body, authority.Term, (float)(DateTime.UtcNow - hazard.Received).TotalSeconds, out byte[] replay))
                    Send(peer.Key, EnemySyncCodec.PrefixName(authority.HostName, replay), NetworkType.BossHazard);
        }
    }
}

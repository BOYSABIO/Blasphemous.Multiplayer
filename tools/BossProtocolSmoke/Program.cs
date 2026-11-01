using System.Collections;
using System.Reflection;
using System.Text.Json;
using Blasphemous.Multiplayer.Common.Enums;
using Blasphemous.Multiplayer.Common.Networking;
using Blasphemous.Multiplayer.Server;
using Blasphemous.Multiplayer.Server.Models;

var passed = new List<string>();
void Check(bool condition, string name) { if (!condition) throw new Exception("FAIL: " + name); passed.Add(name); }
var scene = EnemySyncCodec.SceneHash("D17Z01S11");
BossEncounterData Encounter(uint sequence = 1) => new() { SceneHash = scene, Term = 7, SpawnKey = 42, Sequence = sequence, State = BossEncounterState.Started };
BossHazardData Hazard(ulong id = 1, uint sequence = 1) => new() { SceneHash = scene, Term = 7, SpawnKey = 42, Sequence = sequence, Id = id, Active = true,
    Kind = WardenAttackKind.Area, PrefabIndex = 2, Stage = 1, X = -790, Y = 9.1f, Z = 0, Angle = 0, Strength = 1.5f, Damage = 20,
    Preparation = .4f, Loop = 2, Timer = .2f, HostTime = 150, AnimationHash = 123, AnimationTime = .25f, AnimationLength = 1.2f, FlipX = true, SortingOrder = 2 };
var encounter = BossSyncCodec.EncodeEncounter(Encounter());
Check(encounter.Length == 19 && BossSyncCodec.TryReadEncounter(encounter, 0, out var e) && e.SpawnKey == 42 && e.State == BossEncounterState.Started, "encounter exact wire roundtrip");
var hazard = BossSyncCodec.EncodeHazard(Hazard());
Check(hazard.Length == 88 && BossSyncCodec.TryReadHazard(hazard, 0, out var h) && h.PrefabIndex == 2 && h.X == -790 && h.FlipX && h.Damage == 20, "hazard exact wire roundtrip");
var prefixed = new byte[hazard.Length + 5]; Array.Copy(hazard, 0, prefixed, 5, hazard.Length);
Check(BossSyncCodec.TryReadHazard(prefixed, 5, out h) && h.Id == 1, "nonzero payload offset");
Check(!BossSyncCodec.TryReadEncounter(null!, 0, out _) && !BossSyncCodec.TryReadHazard(null!, 0, out _), "null rejected");
Check(!BossSyncCodec.TryReadHazard(hazard, -1, out _) && !BossSyncCodec.TryReadHazard(hazard, int.MaxValue, out _), "invalid offsets rejected");
Check(Enumerable.Range(0, 88).All(length => !BossSyncCodec.TryReadHazard(hazard.Take(length).ToArray(), 0, out _)), "every truncated hazard rejected");
Check(!BossSyncCodec.TryReadHazard(hazard.Concat(new byte[1]).ToArray(), 0, out _), "trailing hazard bytes rejected");
Check(Enumerable.Range(0, 19).All(length => !BossSyncCodec.TryReadEncounter(encounter.Take(length).ToArray(), 0, out _)), "every truncated encounter rejected");
Check(!BossSyncCodec.TryReadEncounter(encounter.Concat(new byte[1]).ToArray(), 0, out _), "trailing encounter bytes rejected");
byte[] Mutate(byte[] body, int offset, byte value) { var result = (byte[])body.Clone(); result[offset] = value; return result; }
Check(!BossSyncCodec.TryReadEncounter(Mutate(encounter, 18, 3), 0, out _), "unknown encounter state rejected");
Check(!BossSyncCodec.TryReadHazard(Mutate(hazard, 26, 2), 0, out _), "noncanonical active boolean rejected");
Check(!BossSyncCodec.TryReadHazard(Mutate(hazard, 83, 2), 0, out _), "noncanonical flip boolean rejected");
Check(!BossSyncCodec.TryReadHazard(Mutate(hazard, 27, 2), 0, out _), "unknown attack kind rejected");
Check(!BossSyncCodec.TryReadHazard(Mutate(hazard, 30, 3), 0, out _), "unknown lifecycle stage rejected");
Check(!BossSyncCodec.TryReadHazard(Mutate(hazard, 8, 2), 0, out _), "unsupported generation rejected");
var noIdentity = (byte[])hazard.Clone(); Array.Clear(noIdentity, 18, 8);
Check(!BossSyncCodec.TryReadHazard(noIdentity, 0, out _), "zero hazard identity rejected");
var badPrefab = (byte[])hazard.Clone(); Array.Copy(BitConverter.GetBytes((short)64), 0, badPrefab, 28, 2);
Check(!BossSyncCodec.TryReadHazard(badPrefab, 0, out _), "out-of-range prefab rejected");
var nan = (byte[])hazard.Clone(); Array.Copy(BitConverter.GetBytes(float.NaN), 0, nan, 31, 4);
Check(!BossSyncCodec.TryReadHazard(nan, 0, out _), "NaN world coordinate rejected");
var infinity = (byte[])hazard.Clone(); Array.Copy(BitConverter.GetBytes(float.PositiveInfinity), 0, infinity, 51, 4);
Check(!BossSyncCodec.TryReadHazard(infinity, 0, out _), "infinite damage rejected");
var replay = BossSyncCodec.RebaseForReplay(hazard, 8);
Check(BossSyncCodec.TryReadHazard(replay, 0, out h) && h.Term == 8 && h.Sequence == 0 && h.Id == 1 && h.PrefabIndex == 2 && h.Timer == .2f, "migration replay rebases only term/sequence");
Check(BitConverter.ToUInt32(hazard, 4) == 7 && BitConverter.ToUInt32(hazard, 10) == 1, "replay does not mutate cached source");
Check(BossSyncCodec.TryBuildHazardReplay(hazard, 8, .75f, out var timedReplay)
    && BossSyncCodec.TryReadHazard(timedReplay, 0, out h) && h.Sequence == 0 && h.Term == 8 && h.HostTime == .75f && h.Timer == .2f && h.Id == 1,
    "cached hazard carries receipt age across host clock changes");
Check(BitConverter.ToSingle(hazard, 67) == 150, "timed replay preserves cached absolute clock body");
var ending = Hazard(); ending.Stage = 2; ending.AnimationLength = 1.2f; ending.AnimationTime = .75f;
Check(Math.Abs(BossSyncCodec.HazardReplayLifetime(ending) - .55f) < .0001f, "ending cache lifetime uses remaining animation time");
Check(!BossSyncCodec.TryBuildHazardReplay(BossSyncCodec.EncodeHazard(ending), 8, 1, out _), "expired ending hazard cannot be replayed active");
ending.AnimationTime = 1.2f;
Check(BossSyncCodec.HazardReplayLifetime(ending) == .25f, "completed animation only retains transport grace");
var looping = Hazard(); looping.Preparation = 50; looping.Loop = 2; looping.Timer = 1.9f;
Check(Math.Abs(BossSyncCodec.HazardReplayLifetime(looping) - 10.1f) < .0001f, "loop cache excludes already elapsed preparation and timer");
Check(!BossSyncCodec.TryBuildHazardReplay(hazard, 8, float.NaN, out _) && !BossSyncCodec.TryBuildHazardReplay(hazard, 8, -1, out _), "invalid cache age rejected");
var retired = Hazard(); retired.Active = false;
Check(!BossSyncCodec.TryBuildHazardReplay(BossSyncCodec.EncodeHazard(retired), 8, 0, out _), "inactive hazard never replayed active");
var random = new Random(1700);
for (int i = 0; i < 300; i++)
{
    var sample = Hazard((ulong)i + 1, (uint)i + 1); sample.PrefabIndex = (short)random.Next(-1, 64); sample.Kind = (WardenAttackKind)random.Next(2); sample.Stage = (byte)random.Next(3);
    sample.X = (float)(random.NextDouble() * 20000 - 10000); sample.Loop = (float)random.NextDouble() * 100; sample.Timer = (float)random.NextDouble() * 100;
    var body = BossSyncCodec.EncodeHazard(sample);
    if (!BossSyncCodec.TryReadHazard(body, 0, out var roundtrip) || !body.SequenceEqual(BossSyncCodec.EncodeHazard(roundtrip))) throw new Exception("random roundtrip failed");
}
Check(true, "300 deterministic randomized binary roundtrips");
for (int i = 0; i < 10000; i++) { var fuzz = new byte[random.Next(120)]; random.NextBytes(fuzz); BossSyncCodec.TryReadEncounter(fuzz, random.Next(-1, 122), out _); BossSyncCodec.TryReadHazard(fuzz, random.Next(-1, 122), out _); }
Check(true, "10000 deterministic malformed packets never throw");

// Invoke production acceptance/cache logic without opening sockets. Valid packets
// have a single scene member, so the actual relay loop has no network recipient.
var type = typeof(Server);
const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
object Field(object target, string name) => target.GetType().GetField(name, Hidden | BindingFlags.Public)!.GetValue(target)!;
void SetField(object target, string name, object value) => target.GetType().GetField(name, Hidden | BindingFlags.Public)!.SetValue(target, value);
void Call(object target, string name, params object?[] arguments) => target.GetType().GetMethod(name, Hidden)!.Invoke(target, arguments);
var server = new Server(4, "");
var players = new Dictionary<string, PlayerInfo> { ["host"] = new("host", 0, "party") { sceneName = "D17Z01S11" }, ["guest"] = new("guest", 0, "party") { sceneName = "elsewhere" } };
SetField(server, "connectedPlayers", players);
var hosts = (IDictionary)Field(server, "_sceneHosts");
var hostType = type.GetNestedType("SceneHostState", BindingFlags.NonPublic)!;
void Authority(string name, uint term) { var authority = Activator.CreateInstance(hostType)!; SetField(authority, "HostName", name); SetField(authority, "Term", term); hosts["party|D17Z01S11"] = authority; }
Authority("host", 7);
var scenes = (IDictionary)Field(server, "_bossScenes");
void Receive(string sender, byte[] body, NetworkType packetType) => Call(server, "ReceiveBossRelay", sender, body, packetType);
Receive("unknown", encounter, NetworkType.BossEncounter);
Receive("guest", encounter, NetworkType.BossEncounter);
Check(scenes.Count == 0, "unknown and nonauthority cannot create boss cache");
var wrongScene = Encounter(); wrongScene.SceneHash = 123;
Receive("host", BossSyncCodec.EncodeEncounter(wrongScene), NetworkType.BossEncounter);
var wrongTerm = Encounter(); wrongTerm.Term = 6;
Receive("host", BossSyncCodec.EncodeEncounter(wrongTerm), NetworkType.BossEncounter);
var replaySpoof = Encounter(0);
Receive("host", BossSyncCodec.EncodeEncounter(replaySpoof), NetworkType.BossEncounter);
Check(scenes.Count == 0, "wrong scene/term and replay sequence rejected at server");
Receive("host", encounter, NetworkType.BossEncounter);
var cache = scenes["party|D17Z01S11"]!;
var encounters = (IDictionary)Field(cache, "Encounters"); var hazards = (IDictionary)Field(cache, "Hazards");
Check(encounters.Count == 1 && (uint)Field(cache, "LastSequence") == 1, "current authority can cache encounter");
var staleEncounter = Encounter(); staleEncounter.State = BossEncounterState.Finished;
Receive("host", BossSyncCodec.EncodeEncounter(staleEncounter), NetworkType.BossEncounter);
Check(BossSyncCodec.TryReadEncounter((byte[])encounters[42]!, 0, out e) && e.State == BossEncounterState.Started, "duplicate sequence cannot overwrite encounter");
var active = Hazard(1, 2); Receive("host", BossSyncCodec.EncodeHazard(active), NetworkType.BossHazard);
Check(hazards.Count == 1 && (uint)Field(cache, "LastSequence") == 2, "current authority active hazard cached");
active.Active = false; active.Sequence = 3; Receive("host", BossSyncCodec.EncodeHazard(active), NetworkType.BossHazard);
Check(hazards.Count == 0, "inactive hazard removes replay cache");
Receive("host", hazard, NetworkType.BossHazard);
Check(hazards.Count == 0, "stale active cannot resurrect despawned hazard");
active.Active = true; active.Sequence = 4; Receive("host", BossSyncCodec.EncodeHazard(active), NetworkType.BossHazard);
var cachedHazard = hazards[1UL]!; SetField(cachedHazard, "Expires", DateTime.UtcNow.AddSeconds(-1));
Receive("host", BossSyncCodec.EncodeEncounter(Encounter(5)), NetworkType.BossEncounter);
Check(hazards.Count == 0, "expired hazards removed before acceptance");
uint seq = 6;
for (ulong id = 1; id <= BossSyncCodec.MaxHazards; id++) Receive("host", BossSyncCodec.EncodeHazard(Hazard(id, seq++)), NetworkType.BossHazard);
Receive("host", BossSyncCodec.EncodeHazard(Hazard(129, seq++)), NetworkType.BossHazard);
Check(hazards.Count == 128, "server hazard cache bounded to 128");
var updateAtCapacity = Hazard(1, seq++); updateAtCapacity.X = 25;
Receive("host", BossSyncCodec.EncodeHazard(updateAtCapacity), NetworkType.BossHazard);
Check(BossSyncCodec.TryReadHazard((byte[])Field(hazards[1UL]!, "Body"), 0, out h) && h.X == 25, "existing hazard remains updatable at capacity");
for (int key = 43; key < 50; key++) { var sample = Encounter(seq++); sample.SpawnKey = key; Receive("host", BossSyncCodec.EncodeEncounter(sample), NetworkType.BossEncounter); }
var overflow = Encounter(seq++); overflow.SpawnKey = 50; Receive("host", BossSyncCodec.EncodeEncounter(overflow), NetworkType.BossEncounter);
Check(encounters.Count == 8, "server encounter cache bounded to 8");
Authority("host", 8);
var promoted = Encounter(1); promoted.Term = 8; promoted.State = BossEncounterState.Finished;
Receive("host", BossSyncCodec.EncodeEncounter(promoted), NetworkType.BossEncounter);
Check((uint)Field(cache, "LastSequence") == 1 && BossSyncCodec.TryReadEncounter((byte[])encounters[42]!, 0, out e) && e.Term == 8 && e.State == BossEncounterState.Finished, "new authority term accepts reset sequence");
Receive("host", BossSyncCodec.EncodeEncounter(Encounter(1000)), NetworkType.BossEncounter);
Check((uint)Field(cache, "LastSequence") == 1, "old term rejected even with larger sequence");
Authority("guest", 9);
Receive("host", BossSyncCodec.EncodeEncounter(promoted), NetworkType.BossEncounter);
Check((uint)Field(cache, "Term") == 8, "demoted host cannot mutate boss cache");
Call(server, "ReplayBossScene", "party", "D17Z01S11", "no-connected-peer");
Check((uint)Field(cache, "Term") == 9 && (uint)Field(cache, "LastSequence") == 0, "migration replay resets acceptance epoch");
// The returning preferred player has a fresh world. It must not take ownership
// from the surviving scene owner before receiving that owner's live state.
scenes.Clear();
players["host"].sceneName = "D17Z01S11"; players["guest"].sceneName = "D17Z01S11";
SetField(server, "_sessionEnemyHostName", "host");
Call(server, "RecalculateSceneHost", "party", "D17Z01S11");
var elected = hosts["party|D17Z01S11"]!;
Check((string)Field(elected, "HostName") == "guest" && (uint)Field(elected, "Term") == 9, "returning preferred member preserves incumbent and term");
players["guest"].sceneName = "elsewhere";
Call(server, "RecalculateSceneHost", "party", "D17Z01S11"); elected = hosts["party|D17Z01S11"]!;
Check((string)Field(elected, "HostName") == "host" && (uint)Field(elected, "Term") == 10, "preferred member replaces absent incumbent in new term");
hosts.Clear(); players.Clear();
players["guest"] = new("guest", 0, "party") { sceneName = "D17Z01S11" };
players["host"] = new("host", 0, "party") { sceneName = "D17Z01S11" };
Call(server, "RecalculateSceneHost", "party", "D17Z01S11"); elected = hosts["party|D17Z01S11"]!;
Check((string)Field(elected, "HostName") == "host" && (uint)Field(elected, "Term") == 1, "initial election still prefers session owner over insertion order");
var report = new { passed = passed.Count, failed = 0, checks = passed, scope = "Production codec and reflected server cache; no sockets, no Unity, no game side effects" };
File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "results.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine(JsonSerializer.Serialize(report));

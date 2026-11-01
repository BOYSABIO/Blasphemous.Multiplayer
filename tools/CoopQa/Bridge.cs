using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using BepInEx;
using Framework.Managers;
using Gameplay.GameControllers.Entities;
using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using UObject = UnityEngine.Object;

namespace Blasphemous.CoopQA;

// Test-only bridge for coop-anything/tools/bridge/PROTOCOL.md. It is inert in a
// normal game and is not part of the shipping client. All game actions run in Update.
[BepInPlugin("Blasphemous.CoopQA", "Co-op QA bridge", "0.1.0")]
[BepInDependency("Blasphemous.Multiplayer.Client")]
public sealed class Bridge : BaseUnityPlugin
{
    sealed class Request
    {
        public JObject Message;
        public JObject Reply;
        public readonly ManualResetEvent Done = new(false);
        public bool Cancelled;
    }

    const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    readonly Queue<Request> pending = new();
    readonly object gate = new();
    TcpListener listener;
    volatile bool stopping;
    long frames, lastFrame;
    int processId;
    string root;
    string instance;
    string[] methods = { "ping", "version", "methods", "events", "state", "exec", "screenshot",
        "bot.landing", "bot.newgame", "bot.selectslot", "bot.loadscene", "bot.connect", "bot.disconnect",
        "bot.teleport", "bot.walk", "bot.hit", "bot.swordhit", "bot.heal", "bot.bossstart", "bot.wardenaction", "bot.flag", "bot.pause" };

    void Awake()
    {
        if (!Environment.GetCommandLineArgs().Contains("-coopqa")) { enabled = false; return; }
        try { Initialize(); }
        catch (Exception error)
        {
            // Unity catches Awake exceptions and would otherwise continue into
            // gameplay. A failed isolation guard must stop this owned test copy.
            Logger.LogError("QA isolation guard failed: " + error);
            Environment.Exit(70);
        }
    }

    void Initialize()
    {
        root = Environment.GetEnvironmentVariable("COOPQA_ROOT");
        instance = Environment.GetEnvironmentVariable("COOPQA_INSTANCE");
        if (string.IsNullOrEmpty(root) || (instance != "0" && instance != "1"))
            throw new InvalidOperationException("QA requires a private slot root and instance 0 or 1");
        root = Path.GetFullPath(root);
        new Harmony("Blasphemous.CoopQA").PatchAll(typeof(Bridge).Assembly);
        Application.runInBackground = true;
        string cwd = Path.GetFullPath(Environment.CurrentDirectory).TrimEnd('\\', '/');
        string manifestPath = Path.Combine(cwd, "qa-isolation.json");
        if (!root.StartsWith(cwd + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || !File.Exists(manifestPath))
            throw new InvalidOperationException("QA isolation marker missing or root outside this game copy");
        var manifest = JObject.Parse(File.ReadAllText(manifestPath));
        string saveRoot = Path.GetFullPath((string)manifest["SaveRoot"]);
        if (!saveRoot.StartsWith(cwd + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("QA save root is outside this game copy");
        if (Application.productName != "BlasCoopQA" + instance)
            throw new InvalidOperationException("Native Unity product identity was not isolated: " + Application.productName);
        string saveProbe = Path.GetFullPath(PersistentManager.GetPathAppSettings("qa-probe"));
        if (!saveProbe.StartsWith(saveRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Game save path escaped the private QA directory: " + saveProbe);
        Directory.CreateDirectory(root);
        processId = Process.GetCurrentProcess().Id;
        listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        File.WriteAllText(Path.Combine(root, "bridge-endpoint.json"), JsonConvert.SerializeObject(new {
            protocol = 1, game = "Blasphemous", pid = processId,
            endpoints = new[] { "tcp://127.0.0.1:" + port }
        }));
        File.WriteAllText(Path.Combine(root, "isolation-runtime.json"), JsonConvert.SerializeObject(new {
            product = Application.productName, company = Application.companyName,
            saveProbe, persistentPath = Application.persistentDataPath, cwd, processId
        }, Formatting.Indented));
        new Thread(Accept) { IsBackground = true, Name = "CoopQA accept" }.Start();
        Logger.LogInfo("QA bridge started for isolated slot " + instance);
    }

    void Accept()
    {
        while (!stopping)
        {
            try { TcpClient client = listener.AcceptTcpClient(); ThreadPool.QueueUserWorkItem(_ => Serve(client)); }
            catch (SocketException) { if (!stopping) Logger.LogError("QA listener stopped unexpectedly"); return; }
        }
    }

    JObject Result(JToken id, object value) => new() { ["id"] = id, ["result"] = JToken.FromObject(value) };
    JObject Error(JToken id, int code, string message) => new() { ["id"] = id, ["error"] = JObject.FromObject(new { code, message }) };

    void Serve(TcpClient client)
    {
        using (client)
        using (var stream = client.GetStream())
        using (var reader = new StreamReader(stream, Encoding.UTF8))
        using (var writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true })
        {
            client.ReceiveTimeout = 60000;
            client.SendTimeout = 10000;
            try
            {
                while (!stopping)
                {
                    string line = reader.ReadLine();
                    if (line == null) return;
                    if (line.Length == 0) continue;
                    if (line.Length > 4 * 1024 * 1024) { writer.WriteLine(Error(null, -32600, "Request too large")); return; }
                    JObject message;
                    try { message = JObject.Parse(line); }
                    catch (JsonException ex) { writer.WriteLine(Error(null, -32700, ex.Message).ToString(Formatting.None)); continue; }
                    string method = (string)message["method"];
                    JToken id = message["id"];
                    if (method == "ping")
                    {
                        long tick = Interlocked.Read(ref lastFrame);
                        writer.WriteLine(Result(id, new { pong = true, protocol = 1, game = "Blasphemous", pid = processId,
                            frames = Interlocked.Read(ref frames), mainLoopAgeMs = tick == 0 ? -1 : (DateTime.UtcNow.Ticks - tick) / 10000,
                            ready = tick != 0, loadedCount = 0 }).ToString(Formatting.None));
                        continue;
                    }
                    if (method == "version") { writer.WriteLine(Result(id, new { protocol = 1, game = "Blasphemous", build = "qa-0.1", server = "coopbridge-csharp", pid = processId }).ToString(Formatting.None)); continue; }
                    if (method == "methods") { writer.WriteLine(Result(id, new { methods }).ToString(Formatting.None)); continue; }
                    if (method == "events") { writer.WriteLine(Result(id, new { events = new object[0], next = 0, dropped = 0, last = 0 }).ToString(Formatting.None)); continue; }
                    int timeout = (int?)message["params"]?["timeoutMs"] ?? 30000;
                    timeout = Math.Max(1, Math.Min(180000, timeout));
                    var request = new Request { Message = message };
                    lock (gate)
                    {
                        if (pending.Count >= 256) { writer.WriteLine(Error(id, -32003, "QA queue full").ToString(Formatting.None)); continue; }
                        pending.Enqueue(request);
                    }
                    if (!request.Done.WaitOne(timeout + 2000))
                    {
                        lock (gate) request.Cancelled = true;
                        writer.WriteLine(Error(id, -32001, "No game frame completed the request").ToString(Formatting.None));
                    }
                    else writer.WriteLine(request.Reply.ToString(Formatting.None));
                }
            }
            catch (IOException) { }
            catch (ObjectDisposedException) { }
        }
    }

    void Update()
    {
        if (listener == null) return;
        Application.runInBackground = true;
        Application.targetFrameRate = 60;
        Interlocked.Increment(ref frames);
        Interlocked.Exchange(ref lastFrame, DateTime.UtcNow.Ticks);
        for (int i = 0; i < 16; i++)
        {
            Request request;
            lock (gate) { if (pending.Count == 0) break; request = pending.Dequeue(); if (request.Cancelled) continue; }
            try { request.Reply = Result(request.Message["id"], Dispatch((string)request.Message["method"], request.Message["params"] as JObject ?? new JObject())); }
            catch (Exception ex) { request.Reply = Error(request.Message["id"], -32000, (ex.InnerException ?? ex).ToString()); }
            request.Done.Set();
        }
    }

    static Type GameType(string name) => typeof(Enemy).Assembly.GetType(name, true);
    static object Find(string name) => UObject.FindObjectOfType(GameType(name));
    static object Read(object obj, string name)
    {
        if (obj == null) return null;
        Type type = obj as Type ?? obj.GetType();
        object target = obj is Type ? null : obj;
        return type.GetProperty(name, Flags)?.GetValue(target, null) ?? type.GetField(name, Flags)?.GetValue(target);
    }
    static object Call(object obj, string name, params object[] args)
    {
        if (obj == null) throw new InvalidOperationException("No target for " + name);
        Type type = obj as Type ?? obj.GetType();
        var choices = type.GetMethods(Flags).Where(m => m.Name == name && m.GetParameters().Length == args.Length).ToArray();
        foreach (var method in choices)
        {
            var parameters = method.GetParameters();
            try
            {
                object[] converted = args.Select((arg, i) => arg == null || parameters[i].ParameterType.IsInstanceOfType(arg)
                    ? arg : Convert.ChangeType(arg, parameters[i].ParameterType)).ToArray();
                return method.Invoke(obj is Type ? null : obj, converted);
            }
            catch (ArgumentException) { }
            catch (InvalidCastException) { }
        }
        throw new MissingMethodException(type.FullName, name);
    }
    static object Multiplayer
    {
        get
        {
            var assembly = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "BlasClient");
            var type = assembly?.GetType("Blasphemous.Multiplayer.Client.Main");
            return type == null ? null : Read(type, "Multiplayer");
        }
    }
    static float Number(JObject p, string key, float fallback = 0) => (float?)p[key] ?? fallback;
    static object Position(Transform t) => t == null ? null : new { x = t.position.x, y = t.position.y, z = t.position.z };

    static object[] EventCallbacks(object value)
    {
        var evt = value as UnityEngine.Events.UnityEventBase;
        if (evt == null) return new object[0];
        return Enumerable.Range(0, evt.GetPersistentEventCount()).Select(i => {
            var target = evt.GetPersistentTarget(i);
            return (object)new { target = target == null ? null : target.name,
                type = target == null ? null : target.GetType().FullName, method = evt.GetPersistentMethodName(i) };
        }).ToArray();
    }

    static object[] FsmState()
    {
        var type = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("PlayMakerFSM")).FirstOrDefault(t => t != null);
        if (type == null) return new object[0];
        return UObject.FindObjectsOfType(type).Select(fsm => (object)new {
            instance = fsm.GetInstanceID(), gameObject = (fsm as Component)?.gameObject.name,
            name = Read(fsm, "FsmName"), state = Read(fsm, "ActiveStateName"), enabled = (fsm as Behaviour)?.enabled
        }).ToArray();
    }

    static object[] HazardState()
    {
        var type = Multiplayer?.GetType().Assembly.GetType("Blasphemous.Multiplayer.Client.Enemies.WardenNetwork");
        var dictionary = Read(type, "Hazards") as IDictionary;
        if (dictionary == null) return new object[0];
        var result = new List<object>();
        foreach (DictionaryEntry item in dictionary)
        {
            var area = Read(item.Value, "Area") as Component;
            var data = Read(item.Value, "Data");
            var animator = Read(area, "Animator") as Animator;
            result.Add(new {
                id = item.Key.ToString(), instance = area == null ? 0 : area.GetInstanceID(),
                kind = Read(data, "Kind")?.ToString(), prefab = Read(data, "PrefabIndex"),
                replica = Read(item.Value, "Replica"), position = Position(area?.transform),
                active = area != null && area.gameObject.activeInHierarchy,
                stage = Read(area, "_state")?.ToString(), timer = Read(area, "timer"),
                animation = animator?.GetCurrentAnimatorStateInfo(0).shortNameHash,
                animationTime = animator?.GetCurrentAnimatorStateInfo(0).normalizedTime,
                colliders = area == null ? new object[0] : area.GetComponentsInChildren<Collider2D>(true)
                    .Select(c => (object)new { enabled = c.enabled, active = c.gameObject.activeInHierarchy }).ToArray()
            });
        }
        return result.ToArray();
    }

    object State()
    {
        var multiplayer = Multiplayer;
        var manager = Read(multiplayer, "EnemyManager");
        var tracked = Read(manager, "_enemies") as IDictionary;
        var records = tracked == null ? new object[0] : tracked.Values.Cast<object>().ToArray();
        Func<Enemy, object> syncInfo = enemy => records.FirstOrDefault(record => (Read(record, "Enemy") as Enemy) == enemy);
        var enemies = UObject.FindObjectsOfType<Enemy>().Select(enemy => new {
            instance = enemy.GetInstanceID(), id = enemy.Id, type = enemy.GetType().FullName,
            syncSequence = Read(syncInfo(enemy), "LastSequence"), syncNetId = Read(syncInfo(enemy), "NetId"),
            syncSpawnKey = Read(syncInfo(enemy), "SpawnKey"), syncGeneration = Read(syncInfo(enemy), "Generation"),
            position = Position(enemy.transform), life = enemy.CurrentLife, maximumLife = enemy.Stats.Life.Base, dead = enemy.Dead,
            active = enemy.gameObject.activeInHierarchy,
            ai = enemy.GetComponent<Gameplay.GameControllers.Enemies.Framework.IA.EnemyBehaviour>()?.enabled,
            controller = (Read(enemy, "Controller") as Behaviour)?.enabled,
            treeRunning = Read(Read(enemy.GetComponent<Gameplay.GameControllers.Enemies.Framework.IA.EnemyBehaviour>(), "BehaviourTree"), "isRunning"),
            animation = (Read(enemy, "Animator") as Animator)?.GetCurrentAnimatorStateInfo(0).shortNameHash,
            animationTime = (Read(enemy, "Animator") as Animator)?.GetCurrentAnimatorStateInfo(0).normalizedTime,
            damageCollider = (Read(Read(enemy, "DamageArea"), "DamageAreaCollider") as Collider2D)?.enabled,
            behaviourState = Read(Read(enemy, "Behaviour"), "currentState")?.ToString(),
            lastAttack = Read(Read(enemy, "Behaviour"), "lastAttack")?.ToString(),
            target = enemy.Target == null ? null : enemy.Target.name
        }).ToArray();
        var player = Core.Logic?.Penitent;
        var roster = Read(Read(multiplayer, "OtherPlayerManager"), "AllConnectedPlayers") as IEnumerable;
        var names = roster == null ? new string[0] : roster.Cast<object>().Select(p => (string)Read(p, "Name")).ToArray();
        var mainMenu = Find("Gameplay.UI.Others.MenuLogic.NewMainMenu");
        return new {
            pid = processId, frames = Interlocked.Read(ref frames), scene = SceneManager.GetActiveScene().name,
            level = Read(Read(Core.LevelManager, "currentLevel"), "LevelName"),
            inLevel = Read(multiplayer, "CurrentlyInLevel"),
            product = Application.productName, savePath = PersistentManager.GetPathAppSettings("qa-probe"),
            multiplayerVersion = multiplayer?.GetType().Assembly.GetName().Version.ToString(),
            connected = Read(Read(multiplayer, "NetworkManager"), "IsConnected"),
            playerName = Read(multiplayer, "PlayerName"), sceneHost = Read(manager, "IsSceneHost"),
            hostTerm = Read(manager, "HostTerm"), peers = names,
            player = player == null ? null : new { position = Position(player.transform), life = player.CurrentLife, dead = player.Dead, active = player.gameObject.activeInHierarchy },
            enemies,
            bossFights = UObject.FindObjectsOfType<Gameplay.GameControllers.Bosses.BossFight.BossFightManager>()
                .Select(f => new { id = f.Boss == null ? null : f.Boss.Id, started = f.IsFightStarted, phase = f.CurrentBossPhaseId,
                    finishCallbacks = EventCallbacks(Read(f, "onFinishBossFight")),
                    phases = f.Phases == null ? new object[0] : f.Phases.Select(phase => (object)new {
                        id = phase.Id, minimum = phase.PhaseInterval.x, maximum = phase.PhaseInterval.y }).ToArray() }).ToArray(),
            timeScale = Time.timeScale, gameTime = Time.time,
            inputBlocked = Core.Input != null && Core.Input.InputBlocked,
            inputBlockers = (Read(Core.Input, "inputBlockers") as IEnumerable)?.Cast<object>().Select(x => x.ToString()).ToArray(),
            wardenHazards = HazardState(),
            wardenIntroDummy = Position((Find("ElderBrotherIntroDummy") as Component)?.transform),
            wardenIntroVisible = (Find("ElderBrotherIntroDummy") as Component)?.GetComponentsInChildren<Renderer>(true)
                .Any(r => r.enabled && r.gameObject.activeInHierarchy),
            wardenDefeated = Core.Events != null && Core.Events.GetFlag("D17Z01_BOSSDEAD"),
            wardenRoomDefeated = Core.Events != null && Core.Events.GetFlag("D17Z01S11_BOSSDEAD"),
            fsms = FsmState(),
            menu = new { main = mainMenu != null, state = Read(mainMenu, "currentState")?.ToString(),
                slotsShowing = Read(Read(mainMenu, "selectSaveSlotsWidget"), "IsShowing") }
        };
    }

    object Dispatch(string method, JObject p)
    {
        var multiplayer = Multiplayer;
        switch (method)
        {
            case "state": return State();
            case "bot.landing":
                object landing = Find("Gameplay.UI.Others.MenuLogic.Landing");
                if (landing == null) return new { accepted = true, state = "already past landing" };
                var preload = Read(landing, "preloadMenu") as AsyncOperation;
                if (preload == null) return new { accepted = false, state = "waiting for native preload" };
                // This is the title screen's own completed fade-out action. Keep
                // its normal preloaded menu/core initialization, without fake input.
                preload.allowSceneActivation = true;
                FieldInfo landingState = landing.GetType().GetField("currentState", Flags);
                landingState.SetValue(landing, Enum.Parse(landingState.FieldType, "NOTHING"));
                Time.timeScale = 1;
                return new { accepted = true, state = "activated preloaded menu" };
            case "bot.newgame":
                var menu = Find("Gameplay.UI.Others.MenuLogic.NewMainMenu");
                string menuState = Read(menu, "currentState")?.ToString();
                if (menuState != "MENU") return new { accepted = false, state = menuState ?? "not ready" };
                Call(menu, "OptionCampain");
                return new { accepted = (bool)(Read(Read(menu, "selectSaveSlotsWidget"), "IsShowing") ?? false), state = "campaign slots" };
            case "bot.selectslot":
                // Find by simple name as namespace differs between game versions.
                Type slotType = typeof(Enemy).Assembly.GetTypes().First(t => t.Name == "SelectSaveSlots");
                object slots = UObject.FindObjectOfType(slotType);
                if (!(bool)(Read(slots, "IsShowing") ?? false)) throw new InvalidOperationException("Save slot panel is not showing");
                Call(slots, "OnAcceptSlots", 0);
                return new { accepted = true };
            case "bot.loadscene":
                string scene = (string)p["scene"];
                if (string.IsNullOrEmpty(scene) || !scene.StartsWith("D", StringComparison.Ordinal)) throw new ArgumentException("A game scene is required");
                Core.SpawnManager.PrepareForCommandSpawn(scene);
                Core.LevelManager.ChangeLevel(scene, false, true, true);
                return new { accepted = true };
            case "bot.connect":
                if (multiplayer == null) throw new InvalidOperationException("Multiplayer is not initialized");
                object network = Read(multiplayer, "NetworkManager");
                string name = (string)p["name"] ?? (instance == "0" ? "QAHost" : "QAGuest");
                int port = (int?)p["port"] ?? 27051;
                return new { accepted = Call(network, "Connect", "127.0.0.1", port, "a", name, (string)p["password"] ?? "", (byte)1) };
            case "bot.disconnect": Call(Read(multiplayer, "NetworkManager"), "Disconnect"); return new { accepted = true };
            case "bot.teleport":
                var teleportTarget = new Vector2(Number(p, "x"), Number(p, "y"));
                Core.Logic.Penitent.Teleport(teleportTarget);
                var smartCollider = Core.Logic.Penitent.GetComponent("SmartPlatformCollider");
                if (smartCollider != null) Call(smartCollider, "TeleportTo", new Vector3(teleportTarget.x, teleportTarget.y, 0));
                Core.Logic.Penitent.PlatformCharacterController.ResetPreviousPosition();
                return new { accepted = true };
            case "bot.walk":
                Core.Logic.Penitent.PlatformCharacterInput.Move(Mathf.Clamp(Number(p, "x"), -1, 1), Mathf.Clamp(Number(p, "seconds", 1), 0.05f, 10));
                return new { accepted = true };
            case "bot.heal":
                Core.Logic.Penitent.Stats.Life.SetToCurrentMax();
                return new { accepted = true, life = Core.Logic.Penitent.CurrentLife };
            case "bot.hit":
                int id = (int?)p["instance"] ?? 0;
                Enemy target = UObject.FindObjectsOfType<Enemy>().FirstOrDefault(e => e.GetInstanceID() == id);
                if (target == null) throw new ArgumentException("Enemy instance not found");
                var damageable = target as IDamageable;
                if (damageable == null) throw new InvalidOperationException("Enemy has no normal IDamageable path");
                damageable.Damage(new Hit { AttackingEntity = Core.Logic.Penitent.gameObject,
                    DamageAmount = Number(p, "damage", 10), Force = 1,
                    DamageType = (DamageArea.DamageType)0, DamageElement = (DamageArea.DamageElement)0 });
                return new { accepted = true, lifeAfter = target.CurrentLife };
            case "bot.swordhit":
                // Exercise the real weapon's overlap/collision path. Unlike
                // bot.hit, this has no enemy ID and cannot damage a distant actor.
                var attack = Read(Core.Logic.Penitent, "EntityAttack");
                Call(attack, "ClearHitEntityList");
                Call(attack, "CurrentWeaponAttack", DamageArea.DamageType.Normal, true);
                return new { accepted = true };
            case "bot.bossstart":
                object fight = Find("Gameplay.GameControllers.Bosses.BossFight.BossFightManager");
                if (Read(fight, "SpawnPoint") != null) Call(fight, "SetPenitentOnSpawnPoint");
                if (!(bool)(Read(fight, "IsFightStarted") ?? false)) Call(fight, "StartBossFight");
                return new { accepted = true };
            case "bot.wardenaction":
                var warden = Find("Gameplay.GameControllers.Bosses.ElderBrother.ElderBrother");
                var behaviour = Read(warden, "Behaviour");
                if (warden == null || !(bool)(Read(Read(multiplayer, "EnemyManager"), "IsSceneHost") ?? false))
                    throw new InvalidOperationException("A live authoritative Warden is required");
                string action = (string)p["action"];
                if (action != "AREA" && action != "JUMP") throw new ArgumentException("Expected AREA or JUMP");
                Type actionType = GameType("Gameplay.GameControllers.Bosses.ElderBrother.ElderBrotherBehaviour+ELDER_BROTHER_ATTACKS");
                Call(behaviour, "LaunchAction", Enum.Parse(actionType, action), true);
                return new { accepted = true, action };
            case "bot.flag":
                Core.Events.SetFlag((string)p["name"], (bool?)p["value"] ?? true);
                return new { accepted = true };
            case "bot.pause": Time.timeScale = Number(p, "scale", 1); return new { accepted = true };
            case "screenshot":
                string screenshot = Path.Combine(root, "capture-" + DateTime.UtcNow.Ticks + ".png");
                ScreenCapture.CaptureScreenshot(screenshot);
                return new { ok = true, path = screenshot };
            case "exec":
                if ((string)p["cmd"] == "state") return new { ok = true, output = JsonConvert.SerializeObject(State()) };
                throw new ArgumentException("Only 'state' is supported by exec; use typed bot methods");
            default: throw new MissingMethodException("Unknown QA method: " + method);
        }
    }

    void OnDestroy()
    {
        stopping = true;
        listener?.Stop();
        lock (gate)
        {
            while (pending.Count > 0)
            {
                var request = pending.Dequeue();
                request.Reply = Error(request.Message["id"], -32004, "QA bridge stopping");
                request.Done.Set();
            }
        }
    }
}

// Both native menu scripts force background execution off in Awake, after the
// loader can already have initialized plugins. A hidden client would then never
// get an Update in which the bot could unpause it. Run this postfix first.
[HarmonyPatch]
internal static class BackgroundMenus
{
    static IEnumerable<MethodBase> TargetMethods()
    {
        yield return AccessTools.Method(typeof(Gameplay.UI.Others.MenuLogic.Landing), "Awake");
        yield return AccessTools.Method(typeof(Gameplay.UI.Others.MenuLogic.NewMainMenu), "Awake");
    }

    static void Postfix() => Application.runInBackground = true;
}

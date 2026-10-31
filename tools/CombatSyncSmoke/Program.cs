using Blasphemous.Multiplayer.Client;
using Blasphemous.Multiplayer.Client.Enemies;
using Blasphemous.Multiplayer.Client.Players;
using Blasphemous.Multiplayer.Common.Combat;
using Framework.Managers;
using Gameplay.GameControllers.Entities;
using Tools.Level.Layout;
using UnityEngine;
using ClientMain = Blasphemous.Multiplayer.Client.Main;
using UObject = UnityEngine.Object;

static class Program
{
    static int checks;
    static void Check(bool condition, string label)
    {
        if (!condition) throw new Exception(label);
        checks++;
    }
    sealed class Peer
    {
        public Multiplayer Client = new();
        public List<GameObject> World = new();
        public Entity Player;
        public Peer(string name, string other)
        {
            Client.PlayerName = name;
            Use();
            Player = new GameObject("Penitent").AddComponent<Gameplay.GameControllers.Penitent.Penitent>();
            Client.OtherPlayerManager.AllConnectedPlayers.Add(new PlayerStatus { Name = other, CurrentScene = "room" });
        }
        public void Use() { UObject.World = World; ClientMain.Multiplayer = Client; Core.Logic.Penitent = Player; }
        public FakeEnemy Enemy(bool boss = false, string spawnName = "spawn")
        {
            Use();
            FakeEnemy enemy = boss ? new GameObject("Boss").AddComponent<Gameplay.GameControllers.Bosses.Test.Boss>()
                : new GameObject("Enemy").AddComponent<FakeEnemy>();
            enemy.Animator = enemy.gameObject.AddComponent<Animator>();
            enemy.SpriteRenderer = enemy.gameObject.AddComponent<SpriteRenderer>();
            enemy.EnemyBehaviour = enemy.gameObject.AddComponent<EnemyBehaviour>();
            enemy.Controller = enemy.gameObject.AddComponent<Behaviour>();
            enemy.gameObject.AddComponent<Collider2D>();
            enemy.gameObject.AddComponent<ContactDamage>();
            var spawn = new GameObject(spawnName).AddComponent<EnemySpawnPoint>();
            spawn.SpawnedEnemy = enemy;
            return enemy;
        }
        public void Load() { Use(); Client.EnemyManager.LevelLoaded("room"); }
        public void Apply(Snapshot snapshot)
        {
            Use(); Client.EnemyManager.ReceiveSnapshot("Host", snapshot.Id, snapshot.Type, snapshot.Position,
                snapshot.Life, snapshot.Alive, snapshot.Facing, snapshot.Hash, snapshot.Time);
        }
    }
    static void Main()
    {
        var host = new Peer("Host", "Guest");
        var guest = new Peer("Guest", "Host");
        var hostEnemy = host.Enemy(); var guestEnemy = guest.Enemy();
        hostEnemy.Stats.Defense.Final = guestEnemy.Stats.Defense.Final = 3f;
        host.Load(); guest.Load();
        Snapshot first = host.Client.NetworkManager.Snapshots.Last();
        guest.Apply(first);
        Check(host.Client.EnemyManager.IsSceneHost && !guest.Client.EnemyManager.IsSceneHost, "both peers agree on authority");
        Check(hostEnemy.EnemyBehaviour.Starts == 0, "host scans do not restart AI");
        Check(!guestEnemy.EnemyBehaviour.enabled && !guestEnemy.Controller.enabled, "guest AI and movement are suppressed");
        Check(!Enemy_PuppetUpdate_Patch.Prefix(guestEnemy), "guest update cannot restart AI");
        guestEnemy.Damage(new Hit { DamageAmount = 20, Force = 4, DamageElement = DamageArea.DamageElement.Fire,
            AttackingEntity = guest.Player.gameObject });
        Check(guestEnemy.CurrentLife == 100 && guestEnemy.HitCalls == 0, "guest hit has no speculative local side effects");
        Check(guest.Client.NetworkManager.Hits.Count == 1, "one swing sends exactly one hit");
        var forwarded = guest.Client.NetworkManager.Hits.Single();
        host.Use();
        host.Client.EnemyManager.ReceiveDamage(forwarded.Id, forwarded.Hit, "Guest", forwarded.Position, forwarded.Type);
        Check(hostEnemy.CurrentLife == 83 && hostEnemy.HitCalls == 1 && hostEnemy.DamageCalls == 1, "host replays normal hit and defense once");
        hostEnemy.Damage(new Hit { DamageAmount = 10, AttackingEntity = host.Player.gameObject });
        Check(hostEnemy.CurrentLife == 76, "host attacks still deal damage");
        host.Client.EnemyManager.SendAllSnapshots(); guest.Apply(host.Client.NetworkManager.Snapshots.Last());
        Check(guestEnemy.CurrentLife == 76, "both screens share enemy HP");
        guest.Use();
        guest.Client.EnemyManager.ReceiveState("Intruder", first.Id, new Vector2(20,20), 1, true, false, 0, 0);
        Check(guestEnemy.CurrentLife == 76, "non-authority state rejected");
        guest.Client.EnemyManager.ReceiveState("Host", 987654, new Vector2(20,20), 1, true, false, 0, 0);
        Check(guestEnemy.CurrentLife == 76, "unknown delta does not bind an unrelated enemy");
        host.Use(); hostEnemy.Kill(); UObject.Destroy(hostEnemy.gameObject);
        host.Client.EnemyManager.Update(); host.Client.EnemyManager.SendAllSnapshots();
        Snapshot tombstone = host.Client.NetworkManager.Snapshots.Last();
        Check(!tombstone.Alive && tombstone.Life == 0, "destroyed host corpse retains a death snapshot");
        guest.Apply(tombstone); Check(guestEnemy.Dead, "guest applies host death");
        guest.Use(); UObject.Destroy(guestEnemy.gameObject);
        var neighbour = guest.Enemy(spawnName: "neighbour"); guest.Apply(tombstone);
        Check(!neighbour.Dead, "repeated death never rebinds onto a neighbour");
        var replacement = host.Enemy(); host.Use(); host.Client.EnemyManager.ForceRescan();
        var replacementState = host.Client.NetworkManager.Snapshots.Last(s => s.Alive);
        Check(replacementState.Id != first.Id, "respawn gets a new ID while old death stays reserved");

        host = new Peer("Host", "Guest"); guest = new Peer("Guest", "Host");
        var hostBoss = host.Enemy(boss: true); var guestBoss = guest.Enemy(boss: true);
        host.Load(); guest.Load(); guest.Apply(host.Client.NetworkManager.Snapshots.Last());
        Check(EnemyManager.IsSyncableEnemy(guestBoss), "bosses participate in shared combat");
        Check(guestBoss.EnemyBehaviour.enabled && guestBoss.Controller.enabled && Enemy_PuppetUpdate_Patch.Prefix(guestBoss), "boss scripts remain active");
        guest.Use(); guestBoss.Damage(new Hit { DamageAmount = 30, AttackingEntity = guest.Player.gameObject });
        var bossHit = guest.Client.NetworkManager.Hits.Single(); host.Use();
        host.Client.EnemyManager.ReceiveDamage(bossHit.Id, bossHit.Hit, "Guest", bossHit.Position, bossHit.Type);
        Check(hostBoss.CurrentLife == 70 && hostBoss.HitCalls == 1, "guest can damage host boss through normal boss method");
        host.Client.EnemyManager.SendAllSnapshots(); guest.Apply(host.Client.NetworkManager.Snapshots.Last());
        Check(guestBoss.CurrentLife == 70, "boss HP matches");
        var phase = host.Client.NetworkManager.Snapshots.Last();
        guest.Apply(phase with { Life = 0, Alive = true });
        Check(!guestBoss.Dead && guestBoss.BossDeathSequence == 0, "zero HP during a live boss phase does not force death");
        guest.Apply(phase);
        host.Use(); hostBoss.Damage(new Hit { DamageAmount = 80, AttackingEntity = host.Player.gameObject });
        host.Client.EnemyManager.SendAllSnapshots(); var deadBoss = host.Client.NetworkManager.Snapshots.Last();
        guest.Apply(deadBoss); guest.Apply(deadBoss);
        Check(guestBoss.Dead && guestBoss.BossDeathSequence == 1, "guest boss death logic runs exactly once");
        Check(guestBoss.gameObject.activeSelf && guestBoss.SpriteRenderer.enabled, "boss not hard-hidden before its cutscene");

        guest = new Peer("Guest", "Host"); var unbound = guest.Enemy(); guest.Load();
        Check(!unbound.EnemyBehaviour.enabled, "unbound AI is suppressed");
        guest.Client.EnemyManager.OnDisconnect();
        Check(unbound.EnemyBehaviour.enabled && unbound.Controller.enabled && unbound.GetComponent<Collider2D>().enabled,
            "disconnect restores unbound behaviour, movement and colliders");
        Check(unbound.Target == guest.Player.gameObject, "disconnect restores local AI target");

        host = new Peer("Host", "Guest"); guest = new Peer("Guest", "Host");
        host.Enemy(); var takeover = guest.Enemy(); host.Load(); guest.Load();
        guest.Apply(host.Client.NetworkManager.Snapshots.Last());
        guest.Client.OtherPlayerManager.AllConnectedPlayers.Clear();
        guest.Client.EnemyManager.OnPlayersInSceneChanged();
        Check(guest.Client.EnemyManager.IsSceneHost && takeover.EnemyBehaviour.enabled
            && takeover.Controller.enabled && takeover.GetComponent<Collider2D>().enabled, "host migration restores AI, movement and attack colliders");
        int starts = takeover.EnemyBehaviour.Starts;
        guest.Client.EnemyManager.ForceRescan(); guest.Client.EnemyManager.ForceRescan();
        Check(takeover.EnemyBehaviour.Starts == starts, "repeated host scans do not restart behaviour");

        var remote = new GameObject("remote").AddComponent<OtherPlayerScript>();
        var remoteCollider = remote.gameObject.AddComponent<BoxCollider2D>();
        takeover.Target = remote.gameObject;
        var attack = takeover.gameObject.AddComponent<Gameplay.GameControllers.Enemies.Framework.Attack.EnemyAttack>();
        var contact = takeover.GetComponent<Collider2D>();
        takeover.GetComponent<ContactDamage>().ContactDamageCollider = contact;
        guest.Player.EntityDamageArea = guest.Player.gameObject.AddComponent<DamageArea>();
        var localCollider = guest.Player.gameObject.AddComponent<Collider2D>();
        guest.Player.EntityDamageArea.DamageAreaCollider = localCollider;
        IDamageable victim = (IDamageable)guest.Player;
        contact.Touching.Add(remoteCollider);
        Check(Enemy_ContactAttack_Patch.Prefix(attack,ref victim) && ReferenceEquals(victim,remote), "hardcoded contact hit follows actual remote overlap");
        contact.Touching.Add(localCollider); victim = (IDamageable)guest.Player;
        Check(Enemy_ContactAttack_Patch.Prefix(attack,ref victim) && ReferenceEquals(victim,guest.Player), "host still takes contact hits when overlapping");
        contact.Touching.Clear(); victim = (IDamageable)guest.Player;
        Check(!Enemy_ContactAttack_Patch.Prefix(attack,ref victim), "contact hit cannot damage distant host or guest");

        guest = new Peer("Guest", "Host"); var survivor = guest.Enemy(); guest.Load();
        guest.Apply(new Snapshot(123456,EnemyManager.GetEnemyTypeId(survivor),new Vector2(),0,false,false,0,0));
        guest.Client.EnemyManager.ForceRescan();
        Check(!survivor.Dead, "unknown death cannot use proximity to kill a living neighbour");

        var animated = new SyncedEnemy(9,EnemyManager.GetEnemyTypeId(survivor),survivor);
        survivor.Animator.State = new AnimatorStateInfo { fullPathHash = 45, normalizedTime = 1.25f, loop = false };
        animated.ApplyAnim(45,1.6f);
        Check(survivor.Animator.State.normalizedTime == 1.6f, "completed non-looping attacks do not rewind to their start");

        var data = new EnemyHitData { Damage = 15, Force = 3, Flags = 0, SourceX = 4, SourceY = 5 };
        var restored = CombatHit.Restore(data, guest.Player.gameObject);
        Check(!restored.Unnavoidable && !restored.Unparriable && !restored.Unblockable,
            "shared enemy hits preserve dodge, parry and block options");
        Check(Enemy_Hit_Patch.TargetMethods().Any(m => m.DeclaringType == typeof(FakeEnemy)), "hit patch discovers enemy Damage(Hit)");
        Console.WriteLine($"CombatSyncSmoke: PASS ({checks} production combat checks; headless game doubles)");
    }
}

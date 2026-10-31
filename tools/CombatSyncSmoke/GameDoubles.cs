// Minimal headless game doubles. Combat logic is linked from the production client.
using Blasphemous.Multiplayer.Client.Enemies;
using Blasphemous.Multiplayer.Common.Combat;
using Gameplay.GameControllers.Entities;
using UnityEngine;

namespace UnityEngine
{
    public class Object
    {
        public static List<GameObject> World = new();
        public bool Destroyed;
        public static bool operator ==(Object a, Object b) => (ReferenceEquals(a, null) || a.Destroyed) ? ReferenceEquals(b, null) || b.Destroyed : ReferenceEquals(a, b);
        public static bool operator !=(Object a, Object b) => !(a == b);
        public override bool Equals(object obj) => ReferenceEquals(this, obj);
        public override int GetHashCode() => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);
        public static T[] FindObjectsOfType<T>() where T : Component => World.Where(g => g.activeSelf && !g.Destroyed).SelectMany(g => g.Components).OfType<T>().ToArray();
        public static void Destroy(GameObject obj) { obj.Destroyed = true; foreach (var c in obj.Components) c.Destroyed = true; }
    }
    public class GameObject : Object
    {
        public string name;
        public bool activeSelf = true;
        public Transform transform;
        public List<Component> Components = new();
        public GameObject(string name = "object") { this.name = name; transform = AddComponent<Transform>(); World.Add(this); }
        public T AddComponent<T>() where T : Component, new() { var c = new T { gameObject = this }; Components.Add(c); return c; }
        public T GetComponent<T>() where T : class => Components.OfType<T>().FirstOrDefault();
        public T GetComponentInParent<T>() where T : class => GetComponent<T>() ?? transform.parent?.gameObject.GetComponentInParent<T>();
        public void SetActive(bool active) => activeSelf = active;
        public bool activeInHierarchy => activeSelf;
    }
    public class Component : Object
    {
        public GameObject gameObject;
        public string name => gameObject.name;
        public Transform transform => gameObject.transform;
        public T GetComponentInParent<T>() where T : class => gameObject.GetComponentInParent<T>();
        public T GetComponent<T>() where T : class => gameObject.GetComponent<T>();
        public T[] GetComponentsInChildren<T>(bool inactive = false) where T : class => gameObject.Components.OfType<T>().ToArray();
        public T GetComponentInChildren<T>() where T : class => gameObject.GetComponent<T>();
    }
    public class Behaviour : Component { public bool enabled = true; }
    public class Transform : Component
    {
        public Vector3 position;
        public Transform parent;
        public int SiblingIndex;
        public int GetSiblingIndex() => SiblingIndex;
    }
    public class Collider2D : Behaviour
    {
        public HashSet<Collider2D> Touching = new();
        public bool IsTouching(Collider2D other) => Touching.Contains(other);
    }
    public class BoxCollider2D : Collider2D { }
    public class SpriteRenderer : Behaviour { public bool flipX; }
    public class Animator : Behaviour
    {
        public AnimatorStateInfo State;
        public AnimatorStateInfo GetCurrentAnimatorStateInfo(int layer) => State;
        public void Play(int hash, int layer, float time) { State.fullPathHash = hash; State.normalizedTime = time; }
        public bool HasState(int layer, int hash) => hash != 0;
        public void Update(float delta) { }
    }
    public struct AnimatorStateInfo { public int fullPathHash; public float normalizedTime; public bool loop; }
    public struct Vector2
    {
        public float x, y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
        public static Vector2 zero => new(0, 0);
        public float sqrMagnitude => x * x + y * y;
        public static implicit operator Vector2(Vector3 v) => new(v.x, v.y);
        public static implicit operator Vector3(Vector2 v) => new(v.x, v.y, 0);
        public static float Distance(Vector2 a, Vector2 b) => MathF.Sqrt((a.x-b.x)*(a.x-b.x)+(a.y-b.y)*(a.y-b.y));
    }
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 zero => new(0,0,0);
    }
    public static class Mathf
    {
        public static float Abs(float v) => MathF.Abs(v);
        public static float Round(float v) => MathF.Round(v);
        public static float Ceil(float v) => MathF.Ceiling(v);
        public static float Clamp01(float v) => Math.Clamp(v,0f,1f);
    }
    public static class Time { public static float time; public static float deltaTime = 0.1f; }
}
namespace Framework.FrameworkCore { public enum EntityOrientation { Left, Right } }
namespace Framework.Managers
{
    public static class Core { public static Logic Logic = new(); }
    public class Logic { public Entity Penitent; }
}
namespace Blasphemous.ModdingAPI
{
    public static class ModLog { public static void Info(string text) { } public static void Warn(string text) { } }
}
namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Class)]
    public class HarmonyPatch : Attribute { public HarmonyPatch() { } public HarmonyPatch(Type type, string method) { } }
}
namespace Gameplay.GameControllers.Enemies.Framework.Attack
{
    public class EnemyAttack : Component { public void ContactAttack(IDamageable damageable) { } }
}
namespace Gameplay.GameControllers.Entities
{
    public class Entity : Behaviour
    {
        public float CurrentLife = 100f;
        public bool Dead;
        public int CurrentOutputDamage = 10;
        public Animator Animator;
        public SpriteRenderer SpriteRenderer;
        public EntityStatus Status = new();
        public EntityStats Stats = new();
        public DamageArea EntityDamageArea;
        public int DamageCalls, DeathCalls;
        public void Damage(float amount, string sound)
        {
            if (!Enemy_EntityDamage_Patch.Prefix(this, amount)) return;
            DamageCalls++;
            CurrentLife -= Math.Max(0, amount - Stats.Defense.Final);
            if (CurrentLife <= 0 && !Dead) { Dead = true; DeathCalls++; }
        }
        public void Kill() => Damage(CurrentLife + Stats.Defense.Final, "");
        public void Damage(Hit hit) => Damage(hit.DamageAmount, "");
    }
    public class Enemy : Entity
    {
        public string Id = "EN01";
        public int SpawningId;
        public bool IsAttacking;
        public GameObject Target;
        public EnemyBehaviour EnemyBehaviour;
        public Behaviour Controller;
    }
    public class FakeEnemy : Enemy, IDamageable
    {
        public int HitCalls;
        public int BossDeathSequence;
        public new void Damage(Hit hit)
        {
            if (!Enemy_Hit_Patch.Prefix(this, hit)) return;
            HitCalls++;
            // Real enemy damage methods own their death side effects, beyond Entity.Damage.
            Damage(hit.DamageAmount, "");
            if (Dead) BossDeathSequence++;
        }
    }
    public class EntityStatus { public Framework.FrameworkCore.EntityOrientation Orientation; }
    public class EntityStats { public Defense Defense = new(); }
    public class Defense { public float Final; }
    public class EnemyBehaviour : Behaviour
    {
        public Tree BehaviourTree = new();
        public int Starts;
        public void StopBehaviour() => BehaviourTree.isRunning = false;
        public void StopMovement() { }
        public void StopAllCoroutines() { }
        public void StartBehaviour() { Starts++; BehaviourTree.isRunning = true; }
    }
    public class Tree { public bool isRunning = true; }
    public interface IDamageable { void Damage(Hit hit); }
    public class DamageArea : Component
    {
        public Collider2D DamageAreaCollider;
        public enum DamageType { Normal, Heavy }
        public enum DamageElement { Normal, Fire, Toxic, Magic, Lightning, Contact }
    }
    public class ContactDamage : Component { public Collider2D ContactDamageCollider; }
    public class AttackArea : Component { }
    public struct Hit
    {
        public GameObject AttackingEntity;
        public float DamageAmount, Force;
        public DamageArea.DamageType DamageType;
        public DamageArea.DamageElement DamageElement;
        public bool Unnavoidable, Unparriable, Unblockable, ThrowbackDirByOwnerPosition,
            ForceGuardSlideDirection, DestroysProjectiles, DontSpawnBlood, forceGuardslide, CheckOrientationsForGuardslide;
    }
}
namespace Gameplay.GameControllers.Penitent { public class Penitent : Entity, IDamageable { } }
namespace Gameplay.GameControllers.Bosses.Test { public class Boss : FakeEnemy { } }
namespace Tools.Level.Layout
{
    public class EnemySpawnPoint : Component
    {
        public Enemy SpawnedEnemy;
        public Vector3 Position => transform.position;
        public void CreateEnemy() { }
    }
}
namespace Blasphemous.Multiplayer.Client.Players
{
    public class PlayerStatus { public string Name, CurrentScene; }
    public class OtherPlayerScript : Component, IDamageable { public string PlayerName; public bool IsDead; public void Damage(Hit hit) { } }
    public class OtherPlayerManager
    {
        public List<PlayerStatus> AllConnectedPlayers = new();
        public List<OtherPlayerScript> AllActivePlayers = new();
    }
}
namespace Blasphemous.Multiplayer.Client
{
    public static class Main { public static Multiplayer Multiplayer; }
    public class Multiplayer
    {
        public bool CurrentlyInLevel = true;
        public string PlayerName;
        public EnemyManager EnemyManager = new();
        public FakeNetwork NetworkManager = new();
        public Players.OtherPlayerManager OtherPlayerManager = new();
    }
    public record Snapshot(int Id, string Type, Vector2 Position, float Life, bool Alive, bool Facing, int Hash, float Time);
    public class FakeNetwork
    {
        public bool IsConnected = true;
        public string SessionEnemyHostName = "Host";
        public List<Snapshot> Snapshots = new();
        public List<(int Id, EnemyHitData Hit, Vector2 Position, string Type)> Hits = new();
        public List<EnemyHitData> PlayerHits = new();
        public int Requests;
        public void SendEnemySnapshot(int id, string type, Vector2 p, float life, bool alive, bool facing, int hash, float time) => Snapshots.Add(new(id,type,p,life,alive,facing,hash,time));
        public void SendEnemyState(int id, Vector2 p, float life, bool alive, bool facing, int hash, float time) { }
        public void SendEnemyDeath(int id, Vector2 p, string type) { }
        public void SendEnemyDamage(int id, EnemyHitData hit, Vector2 p, string type) => Hits.Add((id,hit,p,type));
        public void SendEnemyPlayerHit(string name, EnemyHitData hit) => PlayerHits.Add(hit);
        public void SendEnemySyncRequest() => Requests++;
    }
}

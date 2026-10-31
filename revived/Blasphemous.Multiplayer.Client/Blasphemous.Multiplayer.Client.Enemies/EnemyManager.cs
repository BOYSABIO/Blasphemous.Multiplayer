using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using Blasphemous.ModdingAPI;
using Blasphemous.Multiplayer.Client.Players;
using Blasphemous.Multiplayer.Common.Networking;
using Framework.FrameworkCore;
using Framework.Managers;
using Gameplay.GameControllers.Combat;
using Gameplay.GameControllers.Enemies.Framework.Damage;
using Gameplay.GameControllers.Enemies.Framework.IA;
using Gameplay.GameControllers.Entities;
using Gameplay.GameControllers.Penitent;
using HarmonyLib;
using Tools.Level.Layout;
using UnityEngine;

namespace Blasphemous.Multiplayer.Client.Enemies;

public class EnemyManager
{
	private struct PendingDeath
	{
		public int SpawnKey;

		public string TypeId;

		public Vector2 Position;

		public ushort Generation;

		public uint Sequence;
	}

	private struct Tombstone
	{
		public int SpawnKey;

		public string TypeId;

		public Vector2 Position;

		public ushort Generation;

		public uint Sequence;
	}

	private static readonly FieldInfo EnemyDamagedEventField = AccessTools.Field(typeof(EnemyDamageArea), "OnDamaged");

	private static readonly FieldInfo EnemyDamagedGlobalEventField = AccessTools.Field(typeof(EnemyDamageArea), "OnDamagedGlobal");

	private readonly Dictionary<int, SyncedEnemy> _enemies = new Dictionary<int, SyncedEnemy>();

	private readonly Dictionary<int, Tombstone> _tombstones = new Dictionary<int, Tombstone>();

	private readonly Dictionary<int, PendingDeath> _pendingDeaths = new Dictionary<int, PendingDeath>();

	private readonly Dictionary<string, int> _aliases = new Dictionary<string, int>();

	private readonly Dictionary<string, float> _attackDedup = new Dictionary<string, float>();

	private readonly Dictionary<int, SyncedEnemy> _orphans = new Dictionary<int, SyncedEnemy>();

	private readonly float _positionCutoff = 0.04f;

	private readonly float _lifeCutoff = 0.25f;

	private readonly float _scanInterval = 0.25f;

	private readonly float _snapshotInterval = 0.35f;

	private readonly float _retargetInterval = 0.1f;

	private readonly float _aggroStickyBonus = 2.5f;

	private readonly float _dedupSeconds = 0.35f;

	private readonly float[] _deferredScanAt = new float[4] { 0.25f, 0.75f, 1.5f, 3f };

	private float _retargetTimer;

	private float _scanTimer;

	private float _snapshotTimer;

	private float _requestCooldown;

	private float _levelAge;

	private int _deferredScanIndex;

	private bool _isSceneHost;

	private string _currentScene = string.Empty;

	private string _sceneHostName = string.Empty;

	private uint _hostTerm;

	private uint _sequence;

	private uint _localAttackCounter;

	private bool _applyingRemoteDamage;

	private int _nextOrdinal;

	private bool _allowLocalExecution;

	private float _damageMissResyncCooldown;

	private const float DamageMissResyncSeconds = 2f;

	public const float ContactRange = 1.75f;

	public const float ContactRangeAttacking = 2.4f;

	public bool IsSceneHost => _isSceneHost;

	public uint HostTerm => _hostTerm;

	public bool AllowLocalExecution => _allowLocalExecution;

	public bool ApplyingRemoteDamage
	{
		get
		{
			return _applyingRemoteDamage;
		}
		set
		{
			_applyingRemoteDamage = value;
		}
	}

	public void LevelLoaded(string scene)
	{
		Clear();
		_currentScene = scene ?? string.Empty;
		_levelAge = 0f;
		_deferredScanIndex = 0;
		if (Main.Multiplayer.CurrentlyInLevel && Main.Multiplayer.NetworkManager.IsConnected)
		{
			_isSceneHost = false;
			ScanAndRegisterEnemies(forceSnapshot: false);
			RequestSyncFromHost();
			ModLog.Info($"[EnemySync] Level {scene} — awaiting server host (tracked={_enemies.Count})");
			LogEnemyIdTable("level-load");
		}
	}

	public void LevelUnloaded()
	{
		Clear();
		_currentScene = string.Empty;
		_sceneHostName = string.Empty;
		_isSceneHost = false;
		_hostTerm = 0u;
	}

	public void OnDisconnect()
	{
		ReleaseAllOrphans();
		foreach (SyncedEnemy item in UniqueEnemies())
		{
			if (item.IsValid && item.IsPuppet)
			{
				item.SetPuppet(puppet: false);
			}
		}
		Clear();
		_isSceneHost = false;
		_sceneHostName = string.Empty;
		_hostTerm = 0u;
	}

	public void OnPlayersInSceneChanged()
	{
		if (Main.Multiplayer.CurrentlyInLevel && Main.Multiplayer.NetworkManager.IsConnected)
		{
			ApplyAuthorityToAll();
			if (_isSceneHost)
			{
				ScanAndRegisterEnemies(forceSnapshot: true);
				return;
			}
			SuppressUntrackedEnemyAi();
			RequestSyncFromHost();
		}
	}

	public void ReceiveSceneHost(string scene, string hostName, uint hostTerm)
	{
		if (string.IsNullOrEmpty(scene) || string.IsNullOrEmpty(_currentScene) || !(scene != _currentScene))
		{
			bool isSceneHost = _isSceneHost;
			string sceneHostName = _sceneHostName;
			_sceneHostName = hostName ?? string.Empty;
			_hostTerm = hostTerm;
			string text = Main.Multiplayer.PlayerName ?? string.Empty;
			_isSceneHost = !string.IsNullOrEmpty(_sceneHostName) && _sceneHostName == text;
			if (_isSceneHost && !isSceneHost)
			{
				ApplyTombstonesToLocal();
				ReleaseOrphansExceptTombstones();
				RestorePristineExceptTombstones();
			}
			if (isSceneHost != _isSceneHost || sceneHostName != _sceneHostName)
			{
				ModLog.Info($"[EnemySync] Server scene host → '{_sceneHostName}' term={_hostTerm} localHost={_isSceneHost}");
				ApplyAuthorityToAll();
			}
			if (_isSceneHost)
			{
				ScanAndRegisterEnemies(forceSnapshot: true);
				return;
			}
			SuppressUntrackedEnemyAi();
			RequestSyncFromHost();
		}
	}

	public void Update()
	{
		if (!Main.Multiplayer.CurrentlyInLevel || !Main.Multiplayer.NetworkManager.IsConnected)
		{
			return;
		}
		_levelAge += Time.deltaTime;
		if (_requestCooldown > 0f)
		{
			_requestCooldown -= Time.deltaTime;
		}
		if (_damageMissResyncCooldown > 0f)
		{
			_damageMissResyncCooldown -= Time.deltaTime;
		}
		TickDedup();
		while (_deferredScanIndex < _deferredScanAt.Length && _levelAge >= _deferredScanAt[_deferredScanIndex])
		{
			_deferredScanIndex++;
			ScanAndRegisterEnemies(_isSceneHost);
			if (!_isSceneHost)
			{
				RequestSyncFromHost();
			}
		}
		_scanTimer -= Time.deltaTime;
		if (_scanTimer <= 0f)
		{
			_scanTimer = _scanInterval;
			ScanAndRegisterEnemies(forceSnapshot: false);
			if (!_isSceneHost)
			{
				ApplyAuthorityToAll();
				SuppressUntrackedEnemyAi();
			}
		}
		TickDeferredDeathHides();
		foreach (SyncedEnemy item in UniqueEnemies())
		{
			item.TickInterpolation(Time.deltaTime);
		}
		if (!_isSceneHost)
		{
			return;
		}
		_retargetTimer -= Time.deltaTime;
		if (_retargetTimer <= 0f)
		{
			_retargetTimer = _retargetInterval;
			RetargetEnemiesToNearestPlayer();
		}
		_snapshotTimer -= Time.deltaTime;
		if (_snapshotTimer <= 0f)
		{
			_snapshotTimer = _snapshotInterval;
			SendAllSnapshots();
		}
		foreach (SyncedEnemy item2 in UniqueEnemies())
		{
			if (!item2.IsValid)
			{
				continue;
			}
			if (item2.Enemy.Dead)
			{
				if (item2.LastSentAlive)
				{
					AnnounceDeath(item2);
				}
				StreamHostDeathPresentation(item2);
				continue;
			}
			Vector2 vector = item2.Enemy.transform.position;
			float currentLife = item2.Enemy.CurrentLife;
			bool flag = IsFacingRight(item2.Enemy);
			int num = item2.ReadAnimHash();
			float num2 = item2.ReadAnimTime();
			bool isAttacking = item2.Enemy.IsAttacking;
			bool isParryable = item2.Enemy.IsParryable;
			bool num3 = Mathf.Abs(vector.x - item2.LastSentPosition.x) > _positionCutoff || Mathf.Abs(vector.y - item2.LastSentPosition.y) > _positionCutoff;
			bool flag2 = Mathf.Abs(currentLife - item2.LastSentLife) > _lifeCutoff;
			bool flag3 = flag != item2.LastSentFacingRight;
			bool flag4 = num != 0 && num != item2.LastSentAnimHash;
			bool flag5 = isAttacking && Mathf.Abs(num2 % 1f - item2.LastSentAnimTime % 1f) > 0.08f;
			bool flag6 = isAttacking != item2.LastSentAttacking;
			bool flag7 = isParryable != item2.LastSentParryable;
			if (num3 || flag2 || flag3 || flag4 || flag5 || flag6 || flag7)
			{
				item2.LastSentPosition = vector;
				item2.LastSentLife = currentLife;
				item2.LastSentAlive = true;
				item2.LastSentFacingRight = flag;
				item2.LastSentAnimHash = num;
				item2.LastSentAnimTime = num2;
				item2.LastSentAttacking = isAttacking;
				item2.LastSentParryable = isParryable;
				uint sequence = (item2.LastSequence = NextSequence());
				Main.Multiplayer.NetworkManager.SendEnemyState(item2.NetId, vector, currentLife, alive: true, flag, isAttacking, isParryable, despawned: false, item2.Executable || item2.LastSentExecutable, num, num2, Time.time, item2.SpawnKey, _hostTerm, item2.Generation, sequence, CurrentSceneHash());
			}
		}
	}

	private void StreamHostDeathPresentation(SyncedEnemy synced)
	{
		if (synced.DeathDespawnSent)
		{
			return;
		}
		bool flag = synced.IsHostBodyGone();
		bool isDeathStreaming = synced.IsDeathStreaming;
		if (isDeathStreaming || flag)
		{
			Vector2 position = ((synced.LastSentPosition.sqrMagnitude > 0.0001f) ? synced.LastSentPosition : ((Vector2)synced.Enemy.transform.position));
			int num = (flag ? synced.LastSentAnimHash : synced.ReadAnimHash());
			float num2 = (flag ? synced.LastSentAnimTime : synced.ReadAnimTime());
			bool flag2 = flag || !isDeathStreaming;
			if (flag2)
			{
				synced.DeathDespawnSent = true;
			}
			bool flag3 = num != 0 && num != synced.LastSentAnimHash;
			bool flag4 = Mathf.Abs(num2 % 1f - synced.LastSentAnimTime % 1f) > 0.05f;
			if (flag2 || flag3 || flag4 || synced.LastSentDespawned != flag2)
			{
				synced.LastSentAnimHash = num;
				synced.LastSentAnimTime = num2;
				synced.LastSentDespawned = flag2;
				synced.LastSentAlive = false;
				synced.LastSentLife = 0f;
				uint sequence = (synced.LastSequence = NextSequence());
				Main.Multiplayer.NetworkManager.SendEnemyState(synced.NetId, position, 0f, alive: false, synced.LastSentFacingRight, attacking: false, parryable: false, flag2, synced.Executable || synced.LastSentExecutable, num, num2, Time.time, synced.SpawnKey, _hostTerm, synced.Generation, sequence, CurrentSceneHash());
			}
		}
	}

	public void NotifyRemotePlayerHitByEnemy(string playerName, float damageAmount, Enemy enemy, Hit hit)
	{
		if (!_isSceneHost || string.IsNullOrEmpty(playerName) || damageAmount <= 0f || playerName == Main.Multiplayer.PlayerName)
		{
			return;
		}
		int num = GetTrackedNetId(enemy) ?? ComputeStableNetId(enemy);
		uint num2 = NextLocalAttackId();
		string key = $"eh|{num}|{num2}|{playerName}";
		if (!TryClaimDedup(key))
		{
			return;
		}
		Vector2 direction = Vector2.zero;
		if (enemy != null)
		{
			_ = Core.Logic.Penitent == null;
		}
		if (enemy != null)
		{
			OtherPlayerScript otherPlayerScript = Main.Multiplayer.OtherPlayerManager.FindActivePlayer(playerName);
			if (otherPlayerScript != null)
			{
				direction = ((Vector2)otherPlayerScript.transform.position - (Vector2)enemy.transform.position).normalized;
			}
		}
		byte damageType = (byte)((hit.DamageType != DamageArea.DamageType.Normal) ? ((byte)hit.DamageType) : 0);
		byte damageElement = (byte)hit.DamageElement;
		float force = ((hit.Force > 0f) ? hit.Force : 1f);
		EnemyHitFlags enemyHitFlags = HitFlagsFromHit(hit);
		ModLog.Info($"[EnemySync] Enemy {num} hit remote {playerName} for {damageAmount:0.#} attack={num2} flags={enemyHitFlags}");
		Main.Multiplayer.NetworkManager.SendEnemyPlayerHit(playerName, damageAmount, num, damageType, damageElement, force, num2, direction, enemyHitFlags, _hostTerm, CurrentSceneHash());
	}

	public void NotifyRemotePlayerHitByEnemy(string playerName, float damageAmount)
	{
		NotifyRemotePlayerHitByEnemy(playerName, damageAmount, null, default(Hit));
	}

	public void ReceiveEnemyPlayerHit(string senderName, string receiverName, float damageAmount, int enemyNetId, byte damageType, byte damageElement, float force, uint attackInstanceId, Vector2 direction, EnemyHitFlags hitFlags, int sceneHash, uint hostTerm)
	{
		if (receiverName != Main.Multiplayer.PlayerName || !AcceptAuthority(senderName, sceneHash, hostTerm, 0u) || Core.Logic.Penitent == null || Core.Logic.Penitent.Dead || damageAmount <= 0f)
		{
			return;
		}
		string key = $"eh|{enemyNetId}|{attackInstanceId}|{receiverName}";
		if (TryClaimDedup(key))
		{
			GameObject gameObject = FindEnemyGameObjectByNetId(enemyNetId) ?? FindNearestSyncedEnemyGameObject();
			if (gameObject == null)
			{
				ModLog.Warn($"[EnemySync] Drop enemy→player hit — no puppet for enemy={enemyNetId}");
				return;
			}
			Hit hit = BuildHit(gameObject, damageAmount, (DamageArea.DamageType)damageType, (DamageArea.DamageElement)damageElement, (force > 0f) ? force : 1f, hitFlags);
			Penitent penitent = Core.Logic.Penitent;
			bool flag = penitent.Parry != null && penitent.Parry.IsOnParryChance;
			ModLog.Info($"[EnemySync] Applying enemy hit ({damageAmount:0.#}) enemy={enemyNetId} flags={hitFlags} " + $"from host {senderName} onParryChance={flag} life={penitent.CurrentLife:0.#}");
			penitent.Damage(hit);
		}
	}

	public bool TryInterceptDamage(Enemy enemy, Hit hit)
	{
		if (_applyingRemoteDamage || enemy == null)
		{
			return false;
		}
		if (!IsSyncableEnemy(enemy))
		{
			return false;
		}
		if (!Main.Multiplayer.NetworkManager.IsConnected)
		{
			return false;
		}
		if (_isSceneHost)
		{
			return false;
		}
		if (hit.DamageAmount <= 0f)
		{
			return false;
		}
		if (!IsLocalPlayerAttack(hit))
		{
			ModLog.Info("[EnemySync] Skip intercept — not local player attack on " + enemy.Id + " attacker=" + ((hit.AttackingEntity != null) ? hit.AttackingEntity.name : "null"));
			return false;
		}
		int num = GetTrackedNetId(enemy) ?? ComputeStableNetId(enemy);
		ushort generation = 1;
		if (_enemies.TryGetValue(num, out var value) && value.IsValid)
		{
			generation = value.Generation;
		}
		uint num2 = NextLocalAttackId();
		EnemyHitFlags hitFlags = HitFlagsFromHit(hit);
		ModLog.Info($"[EnemySync] Intercept local hit → host id={num} dmg={hit.DamageAmount:0.#} attack={num2}");
		Main.Multiplayer.NetworkManager.SendEnemyDamage(num, hit.DamageAmount, enemy.transform.position, enemy.Id ?? string.Empty, (byte)hit.DamageType, (byte)hit.DamageElement, (hit.Force > 0f) ? hit.Force : 1f, num2, hitFlags, _hostTerm, generation, CurrentSceneHash());
		return true;
	}

	public void PlayLocalHitPresentation(Enemy enemy, Hit hit)
	{
		if (enemy == null)
		{
			return;
		}
		try
		{
			EnemyDamageArea enemyDamageArea = enemy.GetComponentInChildren<EnemyDamageArea>() ?? (enemy.EntityDamageArea as EnemyDamageArea);
			if (enemyDamageArea != null)
			{
				InvokeDamageEvent(EnemyDamagedEventField, enemyDamageArea, enemy.gameObject, hit);
				InvokeDamageEvent(EnemyDamagedGlobalEventField, enemyDamageArea, enemy.gameObject, hit);
			}
		}
		catch (Exception ex)
		{
			ModLog.Warn("[EnemySync] Local hit FX invoke failed: " + ex.Message);
		}
		if (string.IsNullOrEmpty(hit.HitSoundId) || Core.Audio == null || !(enemy.transform != null))
		{
			return;
		}
		try
		{
			Core.Audio.EventOneShotPanned(hit.HitSoundId, enemy.transform.position);
		}
		catch
		{
		}
	}

	private static void InvokeDamageEvent(FieldInfo field, EnemyDamageArea area, GameObject go, Hit hit)
	{
		if (field != null && !(area == null) && field.GetValue(area) is MulticastDelegate multicastDelegate)
		{
			multicastDelegate?.DynamicInvoke(go, hit);
		}
	}

	public void ReceiveDamage(int netId, float amount, string attackerName, Vector2 hitPos, string enemyTypeId, byte damageType, byte damageElement, float force, uint attackInstanceId, EnemyHitFlags hitFlags, int sceneHash, uint hostTerm, ushort generation)
	{
		if (!_isSceneHost || !AcceptScene(sceneHash))
		{
			return;
		}
		string key = $"pd|{attackerName}|{attackInstanceId}|{netId}|{generation}";
		if (!TryClaimDedup(key))
		{
			return;
		}
		SyncedEnemy syncedEnemy = ResolveDamageTarget(netId, hitPos, enemyTypeId, attackerName);
		if (syncedEnemy == null || !syncedEnemy.IsValid)
		{
			ModLog.Warn($"[EnemySync] Damage miss id={netId} type={enemyTypeId} pos={hitPos} from {attackerName}");
			if (_damageMissResyncCooldown <= 0f)
			{
				ResyncToRequester(attackerName);
			}
		}
		else if (!syncedEnemy.Enemy.Dead && !syncedEnemy.VisuallyDead)
		{
			Hit hit = BuildHit(ResolveAttackerGameObject(attackerName) ?? syncedEnemy.Enemy.gameObject, amount, (DamageArea.DamageType)damageType, (DamageArea.DamageElement)damageElement, (force > 0f) ? force : 1f, hitFlags);
			_applyingRemoteDamage = true;
			try
			{
				ApplyHitToEnemy(syncedEnemy.Enemy, hit);
			}
			finally
			{
				_applyingRemoteDamage = false;
			}
			BroadcastEnemyAfterHit(syncedEnemy);
		}
	}

	public static bool IsLocalPlayerAttack(Hit hit)
	{
		if (hit.AttackingEntity == null)
		{
			return false;
		}
		if (hit.AttackingEntity.GetComponentInParent<Penitent>() != null)
		{
			return true;
		}
		if (Core.Logic.Penitent != null && hit.AttackingEntity.transform.root == Core.Logic.Penitent.transform.root)
		{
			return true;
		}
		_ = hit.AttackingEntity.GetComponentInParent<OtherPlayerScript>() != null;
		return false;
	}

	public static EnemyHitFlags HitFlagsFromHit(Hit hit)
	{
		EnemyHitFlags enemyHitFlags = EnemyHitFlags.None;
		if (hit.Unparriable)
		{
			enemyHitFlags |= EnemyHitFlags.Unparriable;
		}
		if (hit.Unblockable)
		{
			enemyHitFlags |= EnemyHitFlags.Unblockable;
		}
		if (hit.Unnavoidable)
		{
			enemyHitFlags |= EnemyHitFlags.Unnavoidable;
		}
		if (hit.ThrowbackDirByOwnerPosition)
		{
			enemyHitFlags |= EnemyHitFlags.ThrowbackByOwner;
		}
		if (hit.forceGuardslide)
		{
			enemyHitFlags |= EnemyHitFlags.ForceGuardslide;
		}
		if (hit.CheckOrientationsForGuardslide)
		{
			enemyHitFlags |= EnemyHitFlags.CheckOrientationsForGuardslide;
		}
		return enemyHitFlags;
	}

	public static Hit BuildHit(GameObject attacker, float amount, DamageArea.DamageType damageType, DamageArea.DamageElement damageElement, float force, EnemyHitFlags flags)
	{
		return new Hit
		{
			AttackingEntity = attacker,
			DamageAmount = amount,
			DamageType = damageType,
			DamageElement = damageElement,
			Force = ((force > 0f) ? force : 1f),
			ThrowbackDirByOwnerPosition = ((flags & EnemyHitFlags.ThrowbackByOwner) != 0),
			Unparriable = ((flags & EnemyHitFlags.Unparriable) != 0),
			Unblockable = ((flags & EnemyHitFlags.Unblockable) != 0),
			Unnavoidable = ((flags & EnemyHitFlags.Unnavoidable) != 0),
			forceGuardslide = ((flags & EnemyHitFlags.ForceGuardslide) != 0),
			CheckOrientationsForGuardslide = ((flags & EnemyHitFlags.CheckOrientationsForGuardslide) != 0)
		};
	}

	public static void ApplyHitToEnemy(Enemy enemy, Hit hit)
	{
		if (enemy == null)
		{
			return;
		}
		if (enemy is IDamageable damageable)
		{
			damageable.Damage(hit);
			return;
		}
		DamageArea entityDamageArea = enemy.EntityDamageArea;
		if (entityDamageArea != null)
		{
			entityDamageArea.TakeDamage(hit);
		}
		else
		{
			enemy.Damage(hit.DamageAmount, string.Empty);
		}
	}

	public void NotifyLocalParry(Hit hit)
	{
		if (_isSceneHost || hit.AttackingEntity == null)
		{
			return;
		}
		Enemy enemy = hit.AttackingEntity.GetComponentInParent<Enemy>() ?? hit.AttackingEntity.GetComponent<Enemy>();
		if (!(enemy == null) && IsSyncableEnemy(enemy))
		{
			int? trackedNetId = GetTrackedNetId(enemy);
			if (trackedNetId.HasValue)
			{
				ModLog.Info($"[EnemySync] Local parry on puppet {trackedNetId.Value} → host");
				Main.Multiplayer.NetworkManager.SendEnemyParried(trackedNetId.Value, _hostTerm, CurrentSceneHash());
			}
		}
	}

	public void ReceiveEnemyParried(string senderName, int netId, int sceneHash, uint hostTerm)
	{
		if (!_isSceneHost || !AcceptScene(sceneHash) || (hostTerm != 0 && _hostTerm != 0 && hostTerm < _hostTerm))
		{
			return;
		}
		if (!_enemies.TryGetValue(netId, out var value) || !value.IsValid)
		{
			ModLog.Warn($"[EnemySync] Parry miss id={netId} from {senderName}");
		}
		else if (!value.Enemy.Dead && !value.VisuallyDead)
		{
			ModLog.Info($"[EnemySync] Apply remote parry from {senderName} on {netId}");
			try
			{
				value.Enemy.Parry();
			}
			catch (Exception ex)
			{
				ModLog.Warn("[EnemySync] Parry() failed: " + ex.Message);
			}
			BroadcastEnemyAfterHit(value);
		}
	}

	public void ReceiveSnapshot(string senderName, int netId, string enemyTypeId, Vector2 position, float life, bool alive, bool facingRight, bool attacking, bool parryable, bool despawned, bool executable, int animHash, float animTime, float hostTime, int spawnKey, int sceneHash, uint hostTerm, ushort generation, uint sequence)
	{
		if (_isSceneHost || !AcceptAuthority(senderName, sceneHash, hostTerm, sequence))
		{
			return;
		}
		if (!alive || life <= 0.01f)
		{
			ApplyDeathToLocal(netId, position, enemyTypeId, generation, sequence, animHash, animTime, despawned, spawnKey);
			return;
		}
		SyncedEnemy syncedEnemy = BindRemoteEnemy(netId, enemyTypeId, position, generation, spawnKey);
		if (syncedEnemy == null)
		{
			ModLog.Warn($"[EnemySync] Snapshot miss {netId}/{enemyTypeId} @ {position}");
			RequestSyncFromHost();
		}
		else
		{
			ApplyRemoteState(syncedEnemy, position, life, alive: true, facingRight, attacking, parryable, despawned, executable, animHash, animTime, hostTime, spawnKey, generation, sequence, syncedEnemy.LastSequence == 0);
			_pendingDeaths.Remove(netId);
		}
	}

	public void ReceiveState(string senderName, int netId, Vector2 position, float life, bool alive, bool facingRight, bool attacking, bool parryable, bool despawned, bool executable, int animHash, float animTime, float hostTime, int spawnKey, int sceneHash, uint hostTerm, ushort generation, uint sequence)
	{
		if (!_isSceneHost && AcceptAuthority(senderName, sceneHash, hostTerm, sequence))
		{
			SyncedEnemy syncedEnemy = BindRemoteEnemy(netId, null, position, generation, spawnKey);
			if (syncedEnemy != null)
			{
				ApplyRemoteState(syncedEnemy, position, life, alive, facingRight, attacking, parryable, despawned, executable, animHash, animTime, hostTime, spawnKey, generation, sequence, isInitial: false);
			}
		}
	}

	public void ReceiveDeath(string senderName, int netId, Vector2 deathPos, string enemyTypeId, int spawnKey, int sceneHash, uint hostTerm, ushort generation, uint sequence)
	{
		if (AcceptScene(sceneHash))
		{
			if (_isSceneHost)
			{
				ApplyHostDeathFromRelay(netId, deathPos, enemyTypeId, spawnKey, generation, sequence);
			}
			else if (AcceptAuthority(senderName, sceneHash, hostTerm, sequence) && !ApplyDeathToLocal(netId, deathPos, enemyTypeId, generation, sequence, 0, 0f, despawned: false, spawnKey))
			{
				_pendingDeaths[netId] = new PendingDeath
				{
					SpawnKey = spawnKey,
					TypeId = (enemyTypeId ?? string.Empty),
					Position = deathPos,
					Generation = generation,
					Sequence = sequence
				};
				RequestSyncFromHost();
			}
		}
	}

	private void ApplyHostDeathFromRelay(int netId, Vector2 deathPos, string enemyTypeId, int spawnKey, ushort generation, uint sequence)
	{
		SyncedEnemy value = null;
		if ((netId == 0 || !_enemies.TryGetValue(netId, out value) || !value.IsValid) && spawnKey != 0)
		{
			Enemy enemy = FindUntrackedBySpawnKey(spawnKey) ?? FindEnemyBySpawnKeyAny(spawnKey);
			if (enemy != null)
			{
				value = RegisterEnemy(enemy, (netId != 0) ? netId : AssignHostNetId(enemy));
			}
		}
		if (value == null || !value.IsValid)
		{
			Enemy enemy2 = FindNearestLivingEnemy(deathPos, 24f, enemyTypeId);
			if (enemy2 != null)
			{
				value = RegisterEnemy(enemy2, (netId != 0) ? netId : AssignHostNetId(enemy2));
			}
		}
		_tombstones[(netId != 0) ? netId : (value?.NetId ?? 0)] = new Tombstone
		{
			SpawnKey = ((spawnKey != 0) ? spawnKey : (value?.SpawnKey ?? 0)),
			TypeId = (enemyTypeId ?? value?.EnemyTypeId ?? string.Empty),
			Position = deathPos,
			Generation = (ushort)((generation == 0) ? 1 : generation),
			Sequence = sequence
		};
		if (value == null || !value.IsValid)
		{
			return;
		}
		if (value.Enemy.Dead || value.VisuallyDead)
		{
			ForceVisualKill(value);
			value.HardHideBody();
			return;
		}
		ModLog.Info($"[EnemySync] Host applying replayed death {value.NetId} spawnKey={value.SpawnKey}");
		_applyingRemoteDamage = true;
		try
		{
			value.Enemy.Kill();
		}
		catch (Exception ex)
		{
			ModLog.Warn("[EnemySync] Host Kill() failed: " + ex.Message);
			ForceVisualKill(value);
		}
		finally
		{
			_applyingRemoteDamage = false;
		}
		ForceVisualKill(value);
		value.HardHideBody();
	}

	private bool ApplyDeathToLocal(int netId, Vector2 deathPos, string enemyTypeId, ushort generation, uint sequence, int animHash, float animTime, bool despawned, int spawnKey = 0)
	{
		SyncedEnemy value = null;
		if (!_enemies.TryGetValue(netId, out value) || !value.IsValid)
		{
			value = BindRemoteEnemy(netId, enemyTypeId, deathPos, generation, spawnKey);
		}
		if (value == null || !value.IsValid)
		{
			_tombstones[netId] = new Tombstone
			{
				SpawnKey = spawnKey,
				TypeId = (enemyTypeId ?? string.Empty),
				Position = deathPos,
				Generation = generation,
				Sequence = sequence
			};
			return false;
		}
		if (value.LastSequence > sequence && value.VisuallyDead && value.BodyHidden)
		{
			return true;
		}
		ForceVisualKill(value);
		if (animHash != 0)
		{
			value.ApplyAnim(animHash, animTime, allowWhileDead: true);
			ModLog.Info($"[EnemySync] Death anim hash={animHash} on replica {value.NetId}");
		}
		if (despawned)
		{
			value.HardHideBody();
		}
		value.LastSequence = sequence;
		value.Generation = generation;
		if (spawnKey != 0)
		{
			value.SpawnKey = spawnKey;
		}
		_tombstones[netId] = new Tombstone
		{
			SpawnKey = value.SpawnKey,
			TypeId = value.EnemyTypeId,
			Position = deathPos,
			Generation = generation,
			Sequence = sequence
		};
		_pendingDeaths.Remove(netId);
		return true;
	}

	private void ForceVisualKill(SyncedEnemy synced)
	{
		if (synced == null || !synced.IsValid)
		{
			return;
		}
		try
		{
			synced.DestroyLocalExecutionPrompt();
			if (synced.ForceVisualDeath())
			{
				ModLog.Info($"[EnemySync] Applied visual death {synced.NetId} ({synced.EnemyTypeId}) gen={synced.Generation}");
			}
		}
		catch (Exception ex)
		{
			ModLog.Warn($"[EnemySync] ForceVisualKill failed {synced.NetId}: {ex.Message}");
			try
			{
				synced.HardHideBody();
			}
			catch
			{
			}
		}
	}

	private void AnnounceDeath(SyncedEnemy synced)
	{
		synced.LastSentAlive = false;
		synced.BeginDeathStream();
		Vector2 vector = ((synced.LastSentPosition.sqrMagnitude > 0.0001f) ? synced.LastSentPosition : ((Vector2)synced.Enemy.transform.position));
		uint sequence = (synced.LastSequence = NextSequence());
		int num2 = synced.ReadAnimHash();
		float num3 = synced.ReadAnimTime();
		synced.LastSentAnimHash = num2;
		synced.LastSentAnimTime = num3;
		ModLog.Info($"[EnemySync] Death {synced.NetId} ({synced.EnemyTypeId}) @ {vector} anim={num2}");
		Main.Multiplayer.NetworkManager.SendEnemyDeath(synced.NetId, vector, synced.EnemyTypeId, synced.SpawnKey, _hostTerm, synced.Generation, sequence, CurrentSceneHash());
		uint sequence2 = (synced.LastSequence = NextSequence());
		Main.Multiplayer.NetworkManager.SendEnemyState(synced.NetId, vector, 0f, alive: false, synced.LastSentFacingRight, attacking: false, parryable: false, despawned: false, synced.Executable || synced.LastSentExecutable, num2, num3, Time.time, synced.SpawnKey, _hostTerm, synced.Generation, sequence2, CurrentSceneHash());
		synced.Executable = false;
		synced.LastSentExecutable = false;
		_tombstones[synced.NetId] = new Tombstone
		{
			SpawnKey = synced.SpawnKey,
			TypeId = synced.EnemyTypeId,
			Position = vector,
			Generation = synced.Generation,
			Sequence = sequence
		};
	}

	private void ApplyRemoteState(SyncedEnemy synced, Vector2 position, float life, bool alive, bool facingRight, bool attacking, bool parryable, bool despawned, bool executable, int animHash, float animTime, float hostTime, int spawnKey, ushort generation, uint sequence, bool isInitial)
	{
		if (!synced.IsValid)
		{
			return;
		}
		if (spawnKey != 0)
		{
			synced.SpawnKey = spawnKey;
		}
		if (synced.VisuallyDead)
		{
			if (!alive || !(life > 0.01f) || generation <= synced.Generation)
			{
				if (despawned)
				{
					synced.HardHideBody();
					synced.LastSequence = sequence;
					return;
				}
				if (animHash != 0)
				{
					synced.ApplyAnim(animHash, animTime, allowWhileDead: true);
				}
				synced.LastSequence = sequence;
				return;
			}
			synced.Generation = generation;
			synced.Bind(synced.Enemy);
		}
		if (sequence != 0 && synced.LastSequence != 0 && sequence < synced.LastSequence)
		{
			return;
		}
		if (!alive || life <= 0.01f)
		{
			ForceVisualKill(synced);
			if (animHash != 0)
			{
				synced.ApplyAnim(animHash, animTime, allowWhileDead: true);
				ModLog.Info($"[EnemySync] Death anim hash={animHash} on replica {synced.NetId}");
			}
			if (despawned)
			{
				synced.HardHideBody();
			}
			synced.LastSequence = sequence;
			synced.Generation = generation;
			return;
		}
		float lastSentLife = synced.LastSentLife;
		bool num = !isInitial && lastSentLife > 0.01f && life < lastSentLife - 0.1f;
		synced.SetPuppet(puppet: true);
		synced.PushRemotePose(hostTime, position, facingRight);
		synced.SetParryable(parryable);
		if (Mathf.Abs(synced.Enemy.CurrentLife - life) > 0.1f)
		{
			synced.Enemy.CurrentLife = life;
		}
		if (num)
		{
			synced.ApplyVisualHitFlash();
		}
		synced.ApplyAnim(animHash, animTime, allowWhileDead: false, attacking);
		bool executable2 = synced.Executable;
		synced.Executable = executable;
		synced.LastSentExecutable = executable;
		if (executable && !executable2)
		{
			SpawnLocalExecutionPrompt(synced);
		}
		else if (!executable && (executable2 || synced.LocalPromptSpawned))
		{
			synced.DestroyLocalExecutionPrompt();
		}
		synced.LastSentPosition = position;
		synced.LastSentLife = life;
		synced.LastSentAlive = true;
		synced.LastSentFacingRight = facingRight;
		synced.LastSentAnimHash = animHash;
		synced.LastSentAnimTime = animTime;
		synced.LastSentAttacking = attacking;
		synced.LastSentParryable = parryable;
		synced.LastSentDespawned = false;
		synced.LastSequence = sequence;
		synced.Generation = generation;
	}

	public void ReceiveSyncRequest(string requesterName)
	{
		if (!_isSceneHost)
		{
			return;
		}
		ModLog.Info("[EnemySync] Sync request from " + requesterName);
		ScanAndRegisterEnemies(forceSnapshot: false);
		SendAllSnapshots();
		foreach (KeyValuePair<int, Tombstone> tombstone in _tombstones)
		{
			Main.Multiplayer.NetworkManager.SendEnemyDeath(tombstone.Key, tombstone.Value.Position, tombstone.Value.TypeId, tombstone.Value.SpawnKey, _hostTerm, tombstone.Value.Generation, tombstone.Value.Sequence, CurrentSceneHash());
		}
		SendSyncDone();
	}

	private void SendSyncDone()
	{
		int num = 0;
		foreach (SyncedEnemy item in UniqueEnemies())
		{
			if (item.IsValid && !item.Enemy.Dead && !item.VisuallyDead)
			{
				num++;
			}
		}
		Main.Multiplayer.NetworkManager.SendEnemySyncDone(num, _hostTerm, CurrentSceneHash());
	}

	private void ResyncToRequester(string requesterName)
	{
		if (_isSceneHost && !(_damageMissResyncCooldown > 0f))
		{
			_damageMissResyncCooldown = 2f;
			ModLog.Info("[EnemySync] Damage-miss resync for " + requesterName);
			SendAllSnapshots();
			SendSyncDone();
		}
	}

	public void ReceiveEnemySyncDone(string senderName, int liveCount, int sceneHash, uint hostTerm)
	{
		if (!_isSceneHost && AcceptAuthority(senderName, sceneHash, hostTerm, 0u))
		{
			ModLog.Info($"[EnemySync] SyncDone from {senderName}: hostLive={liveCount} localTracked={_enemies.Count}");
			SweepOrphans();
		}
	}

	private void SweepOrphans()
	{
		if (_isSceneHost)
		{
			return;
		}
		Enemy[] array = UnityEngine.Object.FindObjectsOfType<Enemy>();
		int num = 0;
		Enemy[] array2 = array;
		foreach (Enemy enemy in array2)
		{
			if (IsSyncableEnemy(enemy) && !enemy.Dead && !GetTrackedNetId(enemy).HasValue)
			{
				int instanceID = enemy.GetInstanceID();
				if (!_orphans.ContainsKey(instanceID))
				{
					EnemyPristineRegistry.CaptureIfNeeded(enemy);
					SyncedEnemy syncedEnemy = new SyncedEnemy(0, enemy.Id ?? string.Empty, enemy, 1);
					syncedEnemy.SoftHide();
					_orphans[instanceID] = syncedEnemy;
					num++;
					ModLog.Info($"[EnemySync] Orphan soft-hidden {enemy.Id} @ {enemy.transform.position}");
				}
			}
		}
		if (num > 0)
		{
			ModLog.Info($"[EnemySync] SweepOrphans hid {num} (total orphans={_orphans.Count})");
		}
	}

	private void ReleaseOrphan(Enemy enemy)
	{
		if (enemy == null)
		{
			return;
		}
		int instanceID = enemy.GetInstanceID();
		if (_orphans.TryGetValue(instanceID, out var value))
		{
			_orphans.Remove(instanceID);
			if (value != null && value.SoftHidden)
			{
				value.SoftUnhide();
			}
			ModLog.Info("[EnemySync] Orphan released " + enemy.Id);
		}
	}

	private void ReleaseAllOrphans()
	{
		foreach (SyncedEnemy value in _orphans.Values)
		{
			if (value != null && value.SoftHidden)
			{
				value.SoftUnhide();
			}
		}
		_orphans.Clear();
	}

	private void ApplyTombstonesToLocal()
	{
		if (_tombstones.Count == 0)
		{
			return;
		}
		int num = 0;
		foreach (KeyValuePair<int, Tombstone> tombstone in _tombstones)
		{
			Tombstone value = tombstone.Value;
			SyncedEnemy value2 = null;
			if (tombstone.Key == 0 || !_enemies.TryGetValue(tombstone.Key, out value2) || !value2.IsValid)
			{
				Enemy enemy = ((value.SpawnKey != 0) ? (FindUntrackedBySpawnKey(value.SpawnKey) ?? FindEnemyBySpawnKeyAny(value.SpawnKey)) : null);
				if (enemy == null)
				{
					enemy = FindNearestLivingEnemy(value.Position, 24f, value.TypeId);
				}
				if (enemy == null)
				{
					continue;
				}
				int netId = ((tombstone.Key != 0) ? tombstone.Key : (GetTrackedNetId(enemy) ?? AssignHostNetId(enemy)));
				value2 = RegisterEnemy(enemy, netId);
			}
			if (value2 != null && value2.IsValid)
			{
				ForceVisualKill(value2);
				value2.HardHideBody();
				num++;
			}
		}
		ModLog.Info($"[EnemySync] ApplyTombstonesToLocal applied={num}/{_tombstones.Count}");
	}

	private void ReleaseOrphansExceptTombstones()
	{
		if (_orphans.Count == 0)
		{
			return;
		}
		List<int> list = new List<int>();
		List<int> list2 = new List<int>();
		foreach (KeyValuePair<int, SyncedEnemy> orphan in _orphans)
		{
			SyncedEnemy value = orphan.Value;
			if (value == null || !value.IsValid)
			{
				list2.Add(orphan.Key);
				continue;
			}
			int spawnKey = ((value.SpawnKey != 0) ? value.SpawnKey : ComputeStableNetId(value.Enemy));
			if (IsTombstonedSpawnKey(spawnKey) || IsTombstonedEnemy(value.Enemy))
			{
				ForceVisualKill(value);
				value.HardHideBody();
				list.Add(orphan.Key);
			}
			else
			{
				list2.Add(orphan.Key);
			}
		}
		foreach (int item in list2)
		{
			if (_orphans.TryGetValue(item, out var value2) && value2 != null && value2.SoftHidden)
			{
				value2.SoftUnhide();
			}
			_orphans.Remove(item);
		}
		foreach (int item2 in list)
		{
			_orphans.Remove(item2);
		}
		ModLog.Info($"[EnemySync] ReleaseOrphansExceptTombstones released={list2.Count} keptDead={list.Count}");
	}

	private void RestorePristineExceptTombstones()
	{
		Enemy[] array = UnityEngine.Object.FindObjectsOfType<Enemy>();
		foreach (Enemy enemy in array)
		{
			if (enemy == null || !IsSyncableEnemy(enemy) || enemy.Dead)
			{
				continue;
			}
			int spawnKey = ComputeStableNetId(enemy);
			if (IsTombstonedSpawnKey(spawnKey) || IsTombstonedEnemy(enemy))
			{
				continue;
			}
			int? trackedNetId = GetTrackedNetId(enemy);
			if (trackedNetId.HasValue)
			{
				int valueOrDefault = trackedNetId.GetValueOrDefault();
				if (_enemies.TryGetValue(valueOrDefault, out var value) && (value.VisuallyDead || value.BodyHidden))
				{
					continue;
				}
			}
			EnemyPristineRegistry.Restore(enemy);
		}
	}

	private bool IsTombstonedSpawnKey(int spawnKey)
	{
		if (spawnKey == 0)
		{
			return false;
		}
		foreach (KeyValuePair<int, Tombstone> tombstone in _tombstones)
		{
			if (tombstone.Value.SpawnKey == spawnKey || tombstone.Key == spawnKey)
			{
				return true;
			}
		}
		return false;
	}

	private bool IsTombstonedEnemy(Enemy enemy)
	{
		if (enemy == null)
		{
			return false;
		}
		int spawnKey = ComputeStableNetId(enemy);
		if (IsTombstonedSpawnKey(spawnKey))
		{
			return true;
		}
		int? trackedNetId = GetTrackedNetId(enemy);
		if (trackedNetId.HasValue)
		{
			return _tombstones.ContainsKey(trackedNetId.Value);
		}
		return false;
	}

	private int? FindTombstoneNetId(int spawnKey)
	{
		if (spawnKey == 0)
		{
			return null;
		}
		if (_tombstones.ContainsKey(spawnKey))
		{
			return spawnKey;
		}
		foreach (KeyValuePair<int, Tombstone> tombstone in _tombstones)
		{
			if (tombstone.Value.SpawnKey == spawnKey)
			{
				return tombstone.Key;
			}
		}
		return null;
	}

	private void SpawnLocalExecutionPrompt(SyncedEnemy synced)
	{
		if (synced == null || synced.LocalPromptSpawned || !synced.IsValid || synced.Enemy == null)
		{
			return;
		}
		_allowLocalExecution = true;
		try
		{
			synced.Enemy.EnemyBehaviour?.Execution();
			synced.LocalPromptSpawned = true;
		}
		catch (Exception ex)
		{
			ModLog.Warn($"[EnemySync] Local Execution() failed on {synced.NetId}: {ex.Message}");
		}
		finally
		{
			_allowLocalExecution = false;
		}
	}

	public void MarkExecutable(Enemy enemy)
	{
		if (enemy == null || !IsSyncableEnemy(enemy))
		{
			return;
		}
		int? trackedNetId = GetTrackedNetId(enemy);
		SyncedEnemy value = null;
		if (trackedNetId.HasValue)
		{
			_enemies.TryGetValue(trackedNetId.Value, out value);
		}
		if ((value == null || !value.IsValid) && _isSceneHost)
		{
			value = RegisterEnemy(enemy, trackedNetId ?? AssignHostNetId(enemy));
		}
		if (value != null && value.IsValid)
		{
			value.Executable = true;
			value.LastSentExecutable = true;
			if (_isSceneHost && Main.Multiplayer.NetworkManager.IsConnected)
			{
				BroadcastEnemyAfterHit(value);
			}
		}
	}

	public bool ShouldBlockEnemyExecution(Enemy enemy)
	{
		if (IsSyncableEnemy(enemy) && Main.Multiplayer.NetworkManager.IsConnected && !_isSceneHost)
		{
			return !_allowLocalExecution;
		}
		return false;
	}

	public void NotifyLocalExecution(Enemy enemy)
	{
		if (!_isSceneHost && !(enemy == null) && IsSyncableEnemy(enemy) && Main.Multiplayer.NetworkManager.IsConnected)
		{
			int num = GetTrackedNetId(enemy) ?? ComputeStableNetId(enemy);
			ModLog.Info($"[EnemySync] Local execution on puppet {num} → host");
			Main.Multiplayer.NetworkManager.SendEnemyExecuted(num, _hostTerm, CurrentSceneHash());
		}
	}

	public void NotifyHostLocalExecution(Enemy enemy)
	{
		if (!_isSceneHost || enemy == null || !IsSyncableEnemy(enemy))
		{
			return;
		}
		int? trackedNetId = GetTrackedNetId(enemy);
		SyncedEnemy value = null;
		if (trackedNetId.HasValue)
		{
			_enemies.TryGetValue(trackedNetId.Value, out value);
		}
		if (value == null || !value.IsValid)
		{
			value = RegisterEnemy(enemy, trackedNetId ?? AssignHostNetId(enemy));
		}
		if (value != null && value.IsValid)
		{
			value.Executable = false;
			value.LastSentExecutable = false;
			ModLog.Info($"[EnemySync] Host local execution on {value.NetId}");
			if (enemy.Dead && value.LastSentAlive)
			{
				AnnounceDeath(value);
			}
			else if (Main.Multiplayer.NetworkManager.IsConnected)
			{
				BroadcastEnemyAfterHit(value);
			}
		}
	}

	public void ReceiveEnemyExecuted(string senderName, int netId, int sceneHash, uint hostTerm)
	{
		if (!_isSceneHost || !AcceptScene(sceneHash) || (hostTerm != 0 && _hostTerm != 0 && hostTerm < _hostTerm))
		{
			return;
		}
		if (!_enemies.TryGetValue(netId, out var value) || !value.IsValid)
		{
			ModLog.Warn($"[EnemySync] Execution miss id={netId} from {senderName}");
		}
		else
		{
			if (value.Enemy.Dead || value.VisuallyDead)
			{
				return;
			}
			ModLog.Info($"[EnemySync] Remote execution from {senderName} on {netId}");
			value.Executable = false;
			value.LastSentExecutable = false;
			try
			{
				EntityExecution componentInChildren = value.Enemy.GetComponentInChildren<EntityExecution>(includeInactive: true);
				if (componentInChildren != null)
				{
					componentInChildren.DestroyExecution();
				}
			}
			catch (Exception ex)
			{
				ModLog.Warn("[EnemySync] DestroyExecution failed: " + ex.Message);
			}
			_applyingRemoteDamage = true;
			try
			{
				value.Enemy.Kill();
			}
			catch (Exception ex2)
			{
				ModLog.Warn("[EnemySync] Kill() after execution failed: " + ex2.Message);
			}
			finally
			{
				_applyingRemoteDamage = false;
			}
			BroadcastEnemyAfterHit(value);
		}
	}

	public void NotifyLocalExecutionAnim(Enemy enemy, int stateHash, float normalizedTime)
	{
		if (!(enemy == null) && Main.Multiplayer.NetworkManager.IsConnected)
		{
			int netId = GetTrackedNetId(enemy) ?? ComputeStableNetId(enemy);
			Main.Multiplayer.NetworkManager.SendPlayerExecutionAnim(netId, stateHash, normalizedTime, _hostTerm, CurrentSceneHash());
		}
	}

	public void ReceivePlayerExecutionAnim(string senderName, int netId, int stateHash, float normalizedTime, int sceneHash, uint hostTerm)
	{
		if (AcceptScene(sceneHash) && !(senderName == Main.Multiplayer.PlayerName))
		{
			OtherPlayerScript otherPlayerScript = Main.Multiplayer.OtherPlayerManager.FindActivePlayer(senderName);
			if (!(otherPlayerScript == null))
			{
				otherPlayerScript.PlayExecutionAnimation(netId, stateHash, normalizedTime);
			}
		}
	}

	public void ForceRescan()
	{
		if (Main.Multiplayer.CurrentlyInLevel && Main.Multiplayer.NetworkManager.IsConnected)
		{
			ScanAndRegisterEnemies(_isSceneHost);
			if (!_isSceneHost)
			{
				SuppressUntrackedEnemyAi();
			}
		}
	}

	public void SendAllSnapshots()
	{
		if (!_isSceneHost)
		{
			return;
		}
		int num = 0;
		int num2 = 0;
		foreach (SyncedEnemy item in UniqueEnemies())
		{
			if (item.IsValid)
			{
				bool flag = !item.Enemy.Dead && !item.VisuallyDead;
				Vector2 vector = (flag ? ((Vector2)item.Enemy.transform.position) : ((item.LastSentPosition.sqrMagnitude > 0.0001f) ? item.LastSentPosition : ((Vector2)item.Enemy.transform.position)));
				float num3 = (flag ? item.Enemy.CurrentLife : 0f);
				bool flag2 = IsFacingRight(item.Enemy);
				int num4 = item.ReadAnimHash();
				float num5 = item.ReadAnimTime();
				bool flag3 = flag && item.Enemy.IsAttacking;
				bool flag4 = flag && item.Enemy.IsParryable;
				bool flag5 = !flag && (item.DeathDespawnSent || item.IsHostBodyGone());
				bool executable = flag && (item.Executable || item.LastSentExecutable);
				item.LastSentPosition = vector;
				item.LastSentLife = num3;
				item.LastSentAlive = flag;
				item.LastSentFacingRight = flag2;
				item.LastSentAnimHash = num4;
				item.LastSentAnimTime = num5;
				item.LastSentAttacking = flag3;
				item.LastSentParryable = flag4;
				item.LastSentDespawned = flag5;
				uint sequence = (item.LastSequence = NextSequence());
				Main.Multiplayer.NetworkManager.SendEnemySnapshot(item.NetId, item.EnemyTypeId, vector, num3, flag, flag2, flag3, flag4, flag5, executable, num4, num5, Time.time, item.SpawnKey, _hostTerm, item.Generation, sequence, CurrentSceneHash());
				num++;
				if (!flag)
				{
					num2++;
				}
			}
		}
		foreach (KeyValuePair<int, Tombstone> tombstone in _tombstones)
		{
			Main.Multiplayer.NetworkManager.SendEnemyDeath(tombstone.Key, tombstone.Value.Position, tombstone.Value.TypeId, tombstone.Value.SpawnKey, _hostTerm, tombstone.Value.Generation, tombstone.Value.Sequence, CurrentSceneHash());
			num2++;
		}
		if (num > 0)
		{
			ModLog.Info($"[EnemySync] Snapshots x{num} (dead={num2}, host={Main.Multiplayer.PlayerName})");
		}
	}

	private void BroadcastEnemyAfterHit(SyncedEnemy synced)
	{
		Vector2 vector = synced.Enemy.transform.position;
		float currentLife = synced.Enemy.CurrentLife;
		bool flag = IsFacingRight(synced.Enemy);
		bool flag2 = !synced.Enemy.Dead;
		synced.LastSentPosition = vector;
		synced.LastSentLife = currentLife;
		synced.LastSentAlive = flag2;
		synced.LastSentFacingRight = flag;
		synced.LastSentParryable = synced.Enemy.IsParryable;
		if (!flag2)
		{
			AnnounceDeath(synced);
			return;
		}
		uint sequence = (synced.LastSequence = NextSequence());
		Main.Multiplayer.NetworkManager.SendEnemyState(synced.NetId, vector, currentLife, alive: true, flag, synced.Enemy.IsAttacking, synced.Enemy.IsParryable, despawned: false, synced.Executable || synced.LastSentExecutable, synced.ReadAnimHash(), synced.ReadAnimTime(), Time.time, synced.SpawnKey, _hostTerm, synced.Generation, sequence, CurrentSceneHash());
	}

	private void RetargetEnemiesToNearestPlayer()
	{
		List<GameObject> list = CollectPlayerTargets();
		if (list.Count == 0)
		{
			return;
		}
		foreach (SyncedEnemy item in UniqueEnemies())
		{
			if (item.IsValid && !item.Enemy.Dead && !item.IsPuppet && !item.VisuallyDead && (!(item.Enemy.Target != null) || IsPlayerTarget(item.Enemy.Target)))
			{
				GameObject gameObject = PickNearestTarget(item.Enemy.transform.position, list, item.Enemy.Target);
				if (gameObject != null && item.Enemy.Target != gameObject)
				{
					item.Enemy.Target = gameObject;
					ModLog.Info($"[EnemySync] Retarget {item.NetId} → {gameObject.name}");
				}
			}
		}
	}

	private static bool IsPlayerTarget(GameObject go)
	{
		if (go == null)
		{
			return false;
		}
		if (go.GetComponentInParent<Penitent>() != null)
		{
			return true;
		}
		if (go.GetComponentInParent<OtherPlayerScript>() != null)
		{
			return true;
		}
		return false;
	}

	private List<GameObject> CollectPlayerTargets()
	{
		List<GameObject> list = new List<GameObject>(4);
		if (Core.Logic.Penitent != null && !Core.Logic.Penitent.Dead)
		{
			list.Add(Core.Logic.Penitent.gameObject);
		}
		foreach (OtherPlayerScript allActivePlayer in Main.Multiplayer.OtherPlayerManager.AllActivePlayers)
		{
			if (!(allActivePlayer == null) && allActivePlayer.gameObject.activeInHierarchy && !allActivePlayer.IsDead)
			{
				list.Add(allActivePlayer.gameObject);
			}
		}
		return list;
	}

	private GameObject PickNearestTarget(Vector2 from, List<GameObject> candidates, GameObject current)
	{
		GameObject result = null;
		float num = float.MaxValue;
		foreach (GameObject candidate in candidates)
		{
			if (!(candidate == null))
			{
				float num2 = Vector2.Distance(from, candidate.transform.position);
				if (current != null && candidate == current)
				{
					num2 -= _aggroStickyBonus;
				}
				if (num2 < num)
				{
					num = num2;
					result = candidate;
				}
			}
		}
		return result;
	}

	private GameObject FindNearestSyncedEnemyGameObject()
	{
		if (Core.Logic.Penitent == null)
		{
			return null;
		}
		Vector2 a = Core.Logic.Penitent.transform.position;
		GameObject result = null;
		float num = float.MaxValue;
		foreach (SyncedEnemy item in UniqueEnemies())
		{
			if (item.IsValid && !item.Enemy.Dead && !item.VisuallyDead)
			{
				float num2 = Vector2.Distance(a, item.Enemy.transform.position);
				if (num2 < num)
				{
					num = num2;
					result = item.Enemy.gameObject;
				}
			}
		}
		return result;
	}

	private GameObject FindEnemyGameObjectByNetId(int netId)
	{
		if (netId != 0 && _enemies.TryGetValue(netId, out var value) && value.IsValid)
		{
			return value.Enemy.gameObject;
		}
		return null;
	}

	private void ScanAndRegisterEnemies(bool forceSnapshot)
	{
		Enemy[] array = UnityEngine.Object.FindObjectsOfType<Enemy>();
		foreach (Enemy enemy in array)
		{
			if (!IsSyncableEnemy(enemy))
			{
				continue;
			}
			EnemyPristineRegistry.CaptureIfNeeded(enemy);
			if (IsTombstonedEnemy(enemy))
			{
				int num = ComputeStableNetId(enemy);
				int netId = GetTrackedNetId(enemy) ?? FindTombstoneNetId(num) ?? num;
				SyncedEnemy syncedEnemy = RegisterEnemy(enemy, netId);
				if (syncedEnemy != null && syncedEnemy.IsValid && !syncedEnemy.BodyHidden)
				{
					ForceVisualKill(syncedEnemy);
					syncedEnemy.HardHideBody();
					syncedEnemy.SetPuppet(!_isSceneHost);
				}
				continue;
			}
			if (!_isSceneHost)
			{
				int? trackedNetId = GetTrackedNetId(enemy);
				if (trackedNetId.HasValue && _enemies.TryGetValue(trackedNetId.Value, out var value))
				{
					if (value.Enemy != enemy)
					{
						value.Bind(enemy);
					}
					value.SetPuppet(puppet: true);
				}
				continue;
			}
			int num2 = GetTrackedNetId(enemy) ?? AssignHostNetId(enemy);
			if (_enemies.TryGetValue(num2, out var value2))
			{
				if (value2.Enemy != enemy)
				{
					value2.Bind(enemy);
				}
				value2.SetPuppet(puppet: false);
			}
			else
			{
				RegisterEnemy(enemy, num2);
			}
		}
		List<int> list = new List<int>();
		foreach (KeyValuePair<int, SyncedEnemy> enemy2 in _enemies)
		{
			if (!enemy2.Value.IsValid)
			{
				list.Add(enemy2.Key);
			}
		}
		foreach (int item in list)
		{
			_enemies.Remove(item);
		}
		if (_orphans.Count > 0)
		{
			List<int> list2 = new List<int>();
			foreach (KeyValuePair<int, SyncedEnemy> orphan in _orphans)
			{
				if (orphan.Value == null || !orphan.Value.IsValid)
				{
					list2.Add(orphan.Key);
				}
			}
			foreach (int item2 in list2)
			{
				_orphans.Remove(item2);
			}
		}
		if (!_isSceneHost)
		{
			SuppressUntrackedEnemyAi();
			ApplyPendingDeaths();
		}
		if (forceSnapshot && _isSceneHost)
		{
			SendAllSnapshots();
		}
	}

	private int AssignHostNetId(Enemy enemy)
	{
		int num = ComputeStableNetId(enemy);
		int num2 = _nextOrdinal++;
		return StableHash($"{num}|ord|{num2}|{_currentScene}");
	}

	private SyncedEnemy RegisterEnemy(Enemy enemy, int netId)
	{
		List<int> list = new List<int>();
		foreach (KeyValuePair<int, SyncedEnemy> enemy2 in _enemies)
		{
			if (enemy2.Key != netId && enemy2.Value.IsValid && enemy2.Value.Enemy == enemy)
			{
				list.Add(enemy2.Key);
			}
		}
		foreach (int item in list)
		{
			_enemies.Remove(item);
		}
		if (_enemies.TryGetValue(netId, out var value))
		{
			value.Bind(enemy);
			value.SpawnKey = ComputeStableNetId(enemy);
			value.SetPuppet(!_isSceneHost);
			return value;
		}
		SyncedEnemy syncedEnemy = new SyncedEnemy(netId, enemy.Id ?? string.Empty, enemy, 1);
		syncedEnemy.SpawnKey = ComputeStableNetId(enemy);
		syncedEnemy.SetPuppet(!_isSceneHost);
		_enemies[netId] = syncedEnemy;
		ModLog.Info($"[EnemySync] Register {netId} {syncedEnemy.EnemyTypeId} puppet={syncedEnemy.IsPuppet}");
		return syncedEnemy;
	}

	private SyncedEnemy BindRemoteEnemy(int netId, string enemyTypeId, Vector2 position, ushort generation, int spawnKey = 0)
	{
		if (_enemies.TryGetValue(netId, out var value) && value.IsValid)
		{
			if (spawnKey != 0)
			{
				value.SpawnKey = spawnKey;
			}
			if (value.VisuallyDead || value.Enemy.Dead)
			{
				if (generation > value.Generation)
				{
					Enemy enemy = ((spawnKey != 0) ? FindUntrackedBySpawnKey(spawnKey) : null);
					if (enemy != null && enemy.Dead)
					{
						enemy = null;
					}
					if (enemy == null)
					{
						enemy = FindNearestUntrackedEnemy(position, 24f, enemyTypeId, allowDead: false);
					}
					if (enemy != null)
					{
						ReleaseOrphan(enemy);
						value.Generation = generation;
						value.Bind(enemy);
						value.SetPuppet(puppet: true);
						return value;
					}
				}
				value.SetPuppet(puppet: true);
				return value;
			}
			value.SetPuppet(puppet: true);
			return value;
		}
		if (_tombstones.TryGetValue(netId, out var value2) && generation <= value2.Generation)
		{
			return null;
		}
		Enemy enemy2 = ((spawnKey != 0) ? FindUntrackedBySpawnKey(spawnKey) : null);
		if (enemy2 == null)
		{
			enemy2 = FindEnemyByStableId(netId);
		}
		if (enemy2 == null)
		{
			enemy2 = FindNearestUntrackedEnemy(position, 24f, enemyTypeId, allowDead: true);
		}
		if (enemy2 == null && !string.IsNullOrEmpty(enemyTypeId))
		{
			enemy2 = FindNearestUntrackedEnemy(position, 40f, enemyTypeId, allowDead: true);
		}
		if (enemy2 == null && !string.IsNullOrEmpty(enemyTypeId))
		{
			enemy2 = FindSoleUntrackedOfType(enemyTypeId);
		}
		if (enemy2 == null)
		{
			return null;
		}
		ModLog.Info($"[EnemySync] Bind {netId}/{enemyTypeId} → local {enemy2.Id} @{enemy2.transform.position} spawnKey={spawnKey}");
		ReleaseOrphan(enemy2);
		SyncedEnemy syncedEnemy = RegisterEnemy(enemy2, netId);
		if (spawnKey != 0)
		{
			syncedEnemy.SpawnKey = spawnKey;
		}
		syncedEnemy.Generation = (ushort)((generation == 0) ? 1 : generation);
		return syncedEnemy;
	}

	private SyncedEnemy ResolveDamageTarget(int netId, Vector2 hitPos, string enemyTypeId, string attackerName)
	{
		string key = AliasKey(attackerName, netId);
		if (_aliases.TryGetValue(key, out var value) && _enemies.TryGetValue(value, out var value2) && value2.IsValid && !value2.Enemy.Dead && !value2.VisuallyDead)
		{
			return value2;
		}
		if (netId != 0 && _enemies.TryGetValue(netId, out var value3) && value3.IsValid)
		{
			bool num = !value3.Enemy.Dead && !value3.VisuallyDead;
			bool flag = hitPos.sqrMagnitude < 0.0001f || Vector2.Distance(value3.Enemy.transform.position, hitPos) <= 8f;
			if (num && flag)
			{
				if (!string.IsNullOrEmpty(attackerName))
				{
					_aliases[key] = value3.NetId;
				}
				return value3;
			}
		}
		Enemy enemy = null;
		if (!string.IsNullOrEmpty(enemyTypeId))
		{
			enemy = FindNearestLivingEnemy(hitPos, 20f, enemyTypeId);
		}
		if (enemy == null)
		{
			return null;
		}
		int netId2 = GetTrackedNetId(enemy) ?? AssignHostNetId(enemy);
		SyncedEnemy syncedEnemy = RegisterEnemy(enemy, netId2);
		if (!string.IsNullOrEmpty(attackerName) && netId != 0)
		{
			_aliases[key] = syncedEnemy.NetId;
		}
		return syncedEnemy;
	}

	private static string AliasKey(string sender, int senderNetId)
	{
		return (sender ?? "?") + "|" + senderNetId;
	}

	private void ApplyAuthorityToAll()
	{
		foreach (SyncedEnemy item in UniqueEnemies())
		{
			if (!item.IsValid)
			{
				continue;
			}
			if (item.VisuallyDead || !item.LastSentAlive || item.Enemy.Dead)
			{
				if (!_isSceneHost || item.VisuallyDead)
				{
					ForceVisualKill(item);
				}
				else
				{
					item.SetPuppet(puppet: true);
				}
			}
			else
			{
				item.SetPuppet(!_isSceneHost);
			}
		}
	}

	private void SuppressUntrackedEnemyAi()
	{
		if (_isSceneHost)
		{
			return;
		}
		Enemy[] array = UnityEngine.Object.FindObjectsOfType<Enemy>();
		foreach (Enemy enemy in array)
		{
			if (!IsSyncableEnemy(enemy) || enemy.Dead || GetTrackedNetId(enemy).HasValue)
			{
				continue;
			}
			EnemyPristineRegistry.CaptureIfNeeded(enemy);
			EnemyBehaviour enemyBehaviour = enemy.EnemyBehaviour;
			if (enemyBehaviour != null)
			{
				enemyBehaviour.StopBehaviour();
				enemyBehaviour.StopMovement();
				enemyBehaviour.enabled = false;
			}
			Collider2D[] componentsInChildren = enemy.GetComponentsInChildren<Collider2D>(includeInactive: true);
			foreach (Collider2D collider2D in componentsInChildren)
			{
				if (!(collider2D == null) && (collider2D.GetComponentInParent<ContactDamage>() != null || collider2D.GetComponentInParent<AttackArea>() != null))
				{
					collider2D.enabled = false;
				}
			}
			if (enemy.Animator != null)
			{
				enemy.Animator.fireEvents = false;
			}
		}
	}

	private void ApplyPendingDeaths()
	{
		if (_isSceneHost || _pendingDeaths.Count == 0)
		{
			return;
		}
		foreach (int item in _pendingDeaths.Keys.ToList())
		{
			PendingDeath pendingDeath = _pendingDeaths[item];
			ApplyDeathToLocal(item, pendingDeath.Position, pendingDeath.TypeId, pendingDeath.Generation, pendingDeath.Sequence, 0, 0f, despawned: false, pendingDeath.SpawnKey);
		}
	}

	private void TickDeferredDeathHides()
	{
		foreach (SyncedEnemy item in UniqueEnemies())
		{
			item?.TickDeferredHide();
		}
	}

	private void RequestSyncFromHost()
	{
		if (!_isSceneHost && !(_requestCooldown > 0f))
		{
			_requestCooldown = 0.4f;
			Main.Multiplayer.NetworkManager.SendEnemySyncRequest();
		}
	}

	private Enemy FindSoleUntrackedOfType(string enemyTypeId)
	{
		Enemy enemy = null;
		Enemy[] array = UnityEngine.Object.FindObjectsOfType<Enemy>();
		foreach (Enemy enemy2 in array)
		{
			if (IsSyncableEnemy(enemy2) && !enemy2.Dead && !(enemy2.Id != enemyTypeId) && !GetTrackedNetId(enemy2).HasValue)
			{
				if (enemy != null)
				{
					return null;
				}
				enemy = enemy2;
			}
		}
		return enemy;
	}

	private Enemy FindUntrackedBySpawnKey(int spawnKey)
	{
		if (spawnKey == 0)
		{
			return null;
		}
		Enemy[] array = UnityEngine.Object.FindObjectsOfType<Enemy>();
		foreach (Enemy enemy in array)
		{
			if (IsSyncableEnemy(enemy) && !GetTrackedNetId(enemy).HasValue && ComputeStableNetId(enemy) == spawnKey)
			{
				return enemy;
			}
		}
		return null;
	}

	private Enemy FindEnemyBySpawnKeyAny(int spawnKey)
	{
		if (spawnKey == 0)
		{
			return null;
		}
		foreach (SyncedEnemy item in UniqueEnemies())
		{
			if (item.IsValid && (item.SpawnKey == spawnKey || ComputeStableNetId(item.Enemy) == spawnKey))
			{
				return item.Enemy;
			}
		}
		Enemy[] array = UnityEngine.Object.FindObjectsOfType<Enemy>();
		foreach (Enemy enemy in array)
		{
			if (IsSyncableEnemy(enemy) && ComputeStableNetId(enemy) == spawnKey)
			{
				return enemy;
			}
		}
		return null;
	}

	private Enemy FindEnemyByStableId(int netId)
	{
		if (netId == 0)
		{
			return null;
		}
		Enemy[] array = UnityEngine.Object.FindObjectsOfType<Enemy>();
		foreach (Enemy enemy in array)
		{
			if (IsSyncableEnemy(enemy) && ComputeStableNetId(enemy) == netId)
			{
				return enemy;
			}
		}
		return null;
	}

	private Enemy FindNearestUntrackedEnemy(Vector2 hintPos, float maxDist, string enemyTypeId, bool allowDead)
	{
		Enemy[] array = UnityEngine.Object.FindObjectsOfType<Enemy>();
		Enemy result = null;
		float num = maxDist;
		Enemy[] array2 = array;
		foreach (Enemy enemy in array2)
		{
			if (!IsSyncableEnemy(enemy) || (!allowDead && enemy.Dead) || (!string.IsNullOrEmpty(enemyTypeId) && enemy.Id != enemyTypeId) || GetTrackedNetId(enemy).HasValue)
			{
				continue;
			}
			float num2 = Vector2.Distance(enemy.transform.position, hintPos);
			EnemySpawnPoint componentInParent = enemy.GetComponentInParent<EnemySpawnPoint>();
			if (componentInParent != null)
			{
				float num3 = Vector2.Distance(componentInParent.Position, hintPos);
				if (num3 < num2)
				{
					num2 = num3;
				}
			}
			if (num2 <= num)
			{
				num = num2;
				result = enemy;
			}
		}
		return result;
	}

	private Enemy FindNearestLivingEnemy(Vector2 hintPos, float maxDist, string enemyTypeId)
	{
		Enemy[] array = UnityEngine.Object.FindObjectsOfType<Enemy>();
		Enemy result = null;
		float num = maxDist;
		bool flag = hintPos.sqrMagnitude > 0.0001f;
		Enemy[] array2 = array;
		foreach (Enemy enemy in array2)
		{
			if (!IsSyncableEnemy(enemy) || enemy.Dead || (!string.IsNullOrEmpty(enemyTypeId) && enemy.Id != enemyTypeId))
			{
				continue;
			}
			int? trackedNetId = GetTrackedNetId(enemy);
			if (!trackedNetId.HasValue || !_enemies.TryGetValue(trackedNetId.Value, out var value) || !value.VisuallyDead)
			{
				float num2 = (flag ? Vector2.Distance(enemy.transform.position, hintPos) : 0f);
				if (num2 <= num)
				{
					num = num2;
					result = enemy;
				}
			}
		}
		return result;
	}

	private GameObject ResolveAttackerGameObject(string attackerName)
	{
		if (!string.IsNullOrEmpty(attackerName))
		{
			OtherPlayerScript otherPlayerScript = Main.Multiplayer.OtherPlayerManager.FindActivePlayer(attackerName);
			if (otherPlayerScript != null)
			{
				return otherPlayerScript.gameObject;
			}
		}
		return null;
	}

	private int? GetTrackedNetId(Enemy enemy)
	{
		foreach (KeyValuePair<int, SyncedEnemy> enemy2 in _enemies)
		{
			if (enemy2.Value.IsValid && enemy2.Value.Enemy == enemy)
			{
				return enemy2.Value.NetId;
			}
		}
		return null;
	}

	private IEnumerable<SyncedEnemy> UniqueEnemies()
	{
		return _enemies.Values.Distinct();
	}

	private bool AcceptScene(int sceneHash)
	{
		if (sceneHash == 0)
		{
			return true;
		}
		return sceneHash == CurrentSceneHash();
	}

	private bool AcceptAuthority(string senderName, int sceneHash, uint hostTerm, uint sequence)
	{
		if (!AcceptScene(sceneHash))
		{
			return false;
		}
		if (!string.IsNullOrEmpty(_sceneHostName) && senderName != _sceneHostName)
		{
			ModLog.Warn("[EnemySync] Reject packet from '" + senderName + "' (host=" + _sceneHostName + ")");
			return false;
		}
		if (_hostTerm != 0 && hostTerm != 0 && hostTerm < _hostTerm)
		{
			return false;
		}
		if (hostTerm > _hostTerm)
		{
			_hostTerm = hostTerm;
		}
		return true;
	}

	private int CurrentSceneHash()
	{
		return EnemySyncCodec.SceneHash(_currentScene);
	}

	private uint NextSequence()
	{
		return ++_sequence;
	}

	private uint NextLocalAttackId()
	{
		return ++_localAttackCounter;
	}

	private bool TryClaimDedup(string key)
	{
		TickDedup();
		if (_attackDedup.ContainsKey(key))
		{
			return false;
		}
		_attackDedup[key] = Time.time + _dedupSeconds;
		return true;
	}

	private void TickDedup()
	{
		if (_attackDedup.Count == 0)
		{
			return;
		}
		float now = Time.time;
		foreach (string item in (from kv in _attackDedup
			where kv.Value <= now
			select kv.Key).ToList())
		{
			_attackDedup.Remove(item);
		}
	}

	public void LogEnemyIdTable(string reason)
	{
		ModLog.Info($"[EnemySync] IdTable ({reason}) host={_isSceneHost} sceneHost={_sceneHostName} term={_hostTerm} scene={_currentScene}");
		foreach (SyncedEnemy item in UniqueEnemies())
		{
			if (item.IsValid)
			{
				Vector2 vector = item.Enemy.transform.position;
				ModLog.Info($"[EnemySync]   id={item.NetId} gen={item.Generation} type={item.EnemyTypeId} life={item.Enemy.CurrentLife:0.#} " + $"dead={item.Enemy.Dead} visualDead={item.VisuallyDead} puppet={item.IsPuppet} pos=({vector.x:0.0},{vector.y:0.0})");
			}
		}
		foreach (KeyValuePair<int, Tombstone> tombstone in _tombstones)
		{
			ModLog.Info($"[EnemySync]   TOMB id={tombstone.Key} gen={tombstone.Value.Generation} type={tombstone.Value.TypeId}");
		}
	}

	public string DebugFakeHit(float amount)
	{
		if (amount <= 0f)
		{
			amount = 25f;
		}
		Enemy enemy = FindNearestLivingEnemy((Core.Logic.Penitent != null) ? ((Vector2)Core.Logic.Penitent.transform.position) : Vector2.zero, 40f, null);
		if (enemy == null)
		{
			return "No living enemy nearby";
		}
		int netId = GetTrackedNetId(enemy) ?? AssignHostNetId(enemy);
		SyncedEnemy syncedEnemy = RegisterEnemy(enemy, netId);
		Hit hit = BuildHit((Core.Logic.Penitent != null) ? Core.Logic.Penitent.gameObject : enemy.gameObject, amount, DamageArea.DamageType.Normal, DamageArea.DamageElement.Normal, 1f, EnemyHitFlags.Unparriable | EnemyHitFlags.Unblockable | EnemyHitFlags.Unnavoidable | EnemyHitFlags.ThrowbackByOwner);
		_applyingRemoteDamage = true;
		try
		{
			ApplyHitToEnemy(syncedEnemy.Enemy, hit);
		}
		finally
		{
			_applyingRemoteDamage = false;
		}
		if (_isSceneHost && Main.Multiplayer.NetworkManager.IsConnected)
		{
			BroadcastEnemyAfterHit(syncedEnemy);
		}
		string text = $"Fake hit {amount:0.#} → id={syncedEnemy.NetId} life={syncedEnemy.Enemy.CurrentLife:0.#} dead={syncedEnemy.Enemy.Dead}";
		ModLog.Info("[EnemySync] " + text);
		return text;
	}

	public string DebugFakeDeath()
	{
		Enemy enemy = FindNearestLivingEnemy((Core.Logic.Penitent != null) ? ((Vector2)Core.Logic.Penitent.transform.position) : Vector2.zero, 40f, null);
		if (enemy == null)
		{
			return "No living enemy nearby";
		}
		int netId = GetTrackedNetId(enemy) ?? AssignHostNetId(enemy);
		SyncedEnemy syncedEnemy = RegisterEnemy(enemy, netId);
		ForceVisualKill(syncedEnemy);
		string text = $"Fake visual death → id={syncedEnemy.NetId} visualDead={syncedEnemy.VisuallyDead}";
		ModLog.Info("[EnemySync] " + text);
		return text;
	}

	public static int ComputeStableNetId(Enemy enemy)
	{
		if (enemy == null)
		{
			return 0;
		}
		EnemySpawnPoint componentInParent = enemy.GetComponentInParent<EnemySpawnPoint>();
		if (componentInParent != null)
		{
			Vector3 position = componentInParent.Position;
			return StableHash($"{enemy.Id}|sp|{Round(position.x)}|{Round(position.y)}");
		}
		if (enemy.SpawningId != 0)
		{
			return enemy.SpawningId;
		}
		string text = ((enemy.gameObject != null) ? enemy.gameObject.name : "unk");
		int num = text.IndexOf("(Clone)", StringComparison.Ordinal);
		if (num >= 0)
		{
			text = text.Substring(0, num).Trim();
		}
		return StableHash(enemy.Id + "|go|" + text);
	}

	public static int ResolveNetId(Enemy enemy)
	{
		return ComputeStableNetId(enemy);
	}

	private static float Round(float v)
	{
		return Mathf.Round(v * 2f) / 2f;
	}

	private void Clear()
	{
		_enemies.Clear();
		_tombstones.Clear();
		_pendingDeaths.Clear();
		_orphans.Clear();
		_allowLocalExecution = false;
		_damageMissResyncCooldown = 0f;
		_aliases.Clear();
		_attackDedup.Clear();
		EnemyPristineRegistry.Clear();
		_scanTimer = 0f;
		_snapshotTimer = 0f;
		_retargetTimer = 0f;
		_requestCooldown = 0f;
		_levelAge = 0f;
		_deferredScanIndex = 0;
		_sequence = 0u;
		_nextOrdinal = 0;
	}

	private static bool IsFacingRight(Enemy enemy)
	{
		if (enemy.Status != null)
		{
			return enemy.Status.Orientation == EntityOrientation.Right;
		}
		return true;
	}

	public static bool IsSyncableEnemy(Enemy enemy)
	{
		if (enemy == null || enemy.gameObject == null)
		{
			return false;
		}
		if (string.IsNullOrEmpty(enemy.Id))
		{
			return false;
		}
		if (enemy.Id == "EV09")
		{
			return false;
		}
		Type type = enemy.GetType();
		if ((type.Namespace ?? string.Empty).StartsWith("Gameplay.GameControllers.Bosses", StringComparison.Ordinal))
		{
			return false;
		}
		if (type.Name.IndexOf("Boss", StringComparison.OrdinalIgnoreCase) >= 0)
		{
			return false;
		}
		return true;
	}

	private static int StableHash(string value)
	{
		int num = 23;
		byte[] bytes = Encoding.UTF8.GetBytes(value);
		for (int i = 0; i < bytes.Length; i++)
		{
			num = num * 31 + bytes[i];
		}
		if (num != 0)
		{
			return num;
		}
		return 1;
	}
}

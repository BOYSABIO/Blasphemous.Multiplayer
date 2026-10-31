using System.Collections.Generic;
using Framework.FrameworkCore;
using Gameplay.GameControllers.Combat;
using Gameplay.GameControllers.Effects.Entity.BlobShadow;
using Gameplay.GameControllers.Enemies.Framework.IA;
using Gameplay.GameControllers.Entities;
using UnityEngine;

namespace Blasphemous.Multiplayer.Client.Enemies;

public class SyncedEnemy
{
	private struct PoseSample
	{
		public float HostTime;

		public Vector2 Position;

		public bool FacingRight;
	}

	private struct ColliderSnapshot
	{
		public Collider2D Collider;

		public bool Enabled;
	}

	private struct RendererSnap
	{
		public Renderer Renderer;

		public bool Enabled;
	}

	public const float DeathHideDelaySeconds = 3f;

	public const float DeathStreamSeconds = 2.5f;

	public const float RenderDelaySeconds = 0.1f;

	public const float MaxExtrapolateSeconds = 0.15f;

	public const float SnapDistance = 4f;

	public const int MaxPoseSamples = 16;

	private readonly List<PoseSample> _poseBuffer = new List<PoseSample>();

	private float _latestHostTime;

	private float _hostTimeOffset;

	private bool _hasHostClock;

	private readonly List<ColliderSnapshot> _colliderSnapshots = new List<ColliderSnapshot>();

	private bool _recordedColliders;

	private bool _recordedFireEvents;

	private bool _originalFireEvents = true;

	private readonly List<RendererSnap> _rendererSnaps = new List<RendererSnap>();

	private bool _recordedRenderers;

	public int NetId { get; }

	public string EnemyTypeId { get; }

	public int SpawnKey { get; set; }

	public ushort Generation { get; set; }

	public uint LastSequence { get; set; }

	public Enemy Enemy { get; private set; }

	public Vector2 LastSentPosition { get; set; }

	public float LastSentLife { get; set; }

	public bool LastSentAlive { get; set; }

	public bool LastSentFacingRight { get; set; }

	public int LastSentAnimHash { get; set; }

	public float LastSentAnimTime { get; set; }

	public bool LastSentAttacking { get; set; }

	public bool LastSentParryable { get; set; }

	public bool LastSentDespawned { get; set; }

	public bool LastSentExecutable { get; set; }

	public bool IsPuppet { get; private set; }

	public bool VisuallyDead { get; private set; }

	public bool BodyHidden { get; private set; }

	public bool SoftHidden { get; private set; }

	public bool Executable { get; set; }

	public bool LocalPromptSpawned { get; set; }

	public float HideBodyAt { get; private set; }

	public float DeathStreamUntil { get; private set; }

	public bool DeathDespawnSent { get; set; }

	public bool IsDeathStreaming
	{
		get
		{
			if (DeathStreamUntil > 0f && Time.time < DeathStreamUntil)
			{
				return !DeathDespawnSent;
			}
			return false;
		}
	}

	public bool IsValid
	{
		get
		{
			if (Enemy != null)
			{
				return Enemy.gameObject != null;
			}
			return false;
		}
	}

	public SyncedEnemy(int netId, string enemyTypeId, Enemy enemy, ushort generation = 1, int spawnKey = 0)
	{
		NetId = netId;
		EnemyTypeId = enemyTypeId ?? string.Empty;
		SpawnKey = spawnKey;
		Generation = generation;
		Bind(enemy);
	}

	public void Bind(Enemy enemy)
	{
		Enemy = enemy;
		VisuallyDead = false;
		BodyHidden = false;
		SoftHidden = false;
		Executable = false;
		LocalPromptSpawned = false;
		HideBodyAt = 0f;
		DeathStreamUntil = 0f;
		DeathDespawnSent = false;
		_poseBuffer.Clear();
		_hasHostClock = false;
		_recordedColliders = false;
		_recordedFireEvents = false;
		_recordedRenderers = false;
		_colliderSnapshots.Clear();
		_rendererSnaps.Clear();
		if (!(enemy == null))
		{
			EnemyPristineRegistry.CaptureIfNeeded(enemy);
			if (SpawnKey == 0)
			{
				SpawnKey = EnemyManager.ComputeStableNetId(enemy);
			}
			LastSentPosition = enemy.transform.position;
			LastSentLife = enemy.CurrentLife;
			LastSentAlive = !enemy.Dead;
			LastSentFacingRight = enemy.Status == null || enemy.Status.Orientation == EntityOrientation.Right;
			LastSentAnimHash = ReadAnimHash();
			LastSentAnimTime = ReadAnimTime();
			LastSentAttacking = enemy.IsAttacking;
			LastSentParryable = enemy.IsParryable;
			LastSentDespawned = false;
			LastSentExecutable = false;
			RecordColliderState();
			RecordAnimatorFireEvents();
			RecordRendererState();
		}
	}

	public void BeginDeathStream()
	{
		DeathStreamUntil = Time.time + 2.5f;
		DeathDespawnSent = false;
		Executable = false;
		LastSentExecutable = false;
	}

	public bool IsHostBodyGone()
	{
		if (Enemy == null || Enemy.gameObject == null)
		{
			return true;
		}
		if (!Enemy.gameObject.activeInHierarchy)
		{
			return true;
		}
		if (Enemy.SpriteRenderer != null && !Enemy.SpriteRenderer.enabled)
		{
			return true;
		}
		return false;
	}

	public int ReadAnimHash()
	{
		if (Enemy == null || Enemy.Animator == null)
		{
			return 0;
		}
		return Enemy.Animator.GetCurrentAnimatorStateInfo(0).shortNameHash;
	}

	public float ReadAnimTime()
	{
		if (Enemy == null || Enemy.Animator == null)
		{
			return 0f;
		}
		return Enemy.Animator.GetCurrentAnimatorStateInfo(0).normalizedTime;
	}

	public void ApplyAnim(int animHash, float normalizedTime, bool allowWhileDead = false, bool attacking = false)
	{
		if (BodyHidden || SoftHidden || (VisuallyDead && !allowWhileDead) || (!LastSentAlive && !allowWhileDead && !VisuallyDead) || animHash == 0 || Enemy == null || Enemy.Animator == null)
		{
			return;
		}
		Animator animator = Enemy.Animator;
		if (!animator.enabled)
		{
			animator.enabled = true;
		}
		AnimatorStateInfo currentAnimatorStateInfo = animator.GetCurrentAnimatorStateInfo(0);
		if (currentAnimatorStateInfo.shortNameHash != animHash)
		{
			animator.Play(animHash, 0, Mathf.Clamp01(normalizedTime % 1f));
			animator.Update(0f);
		}
		else
		{
			float num = currentAnimatorStateInfo.normalizedTime % 1f;
			float num2 = normalizedTime % 1f;
			float num3 = Mathf.Abs(num - num2);
			float num4 = (attacking ? 0.12f : 0.3f);
			if (num3 > num4)
			{
				animator.Play(animHash, 0, num2);
				animator.Update(0f);
			}
		}
		LastSentAnimHash = animHash;
		LastSentAnimTime = normalizedTime;
	}

	public void PushRemotePose(float hostTime, Vector2 position, bool facingRight)
	{
		if (!(Enemy == null))
		{
			_latestHostTime = hostTime;
			_hostTimeOffset = Time.time - hostTime;
			_hasHostClock = true;
			while (_poseBuffer.Count > 0 && _poseBuffer[_poseBuffer.Count - 1].HostTime > hostTime + 0.0001f)
			{
				_poseBuffer.RemoveAt(_poseBuffer.Count - 1);
			}
			_poseBuffer.Add(new PoseSample
			{
				HostTime = hostTime,
				Position = position,
				FacingRight = facingRight
			});
			while (_poseBuffer.Count > 16)
			{
				_poseBuffer.RemoveAt(0);
			}
			if (Enemy.Status != null)
			{
				Enemy.Status.Orientation = ((!facingRight) ? EntityOrientation.Left : EntityOrientation.Right);
			}
			if (Enemy.SpriteRenderer != null)
			{
				Enemy.SpriteRenderer.flipX = !facingRight;
			}
		}
	}

	public void TickInterpolation(float dt)
	{
		if (Enemy == null || VisuallyDead || BodyHidden || SoftHidden || !_hasHostClock || _poseBuffer.Count == 0)
		{
			return;
		}
		float num = Time.time - _hostTimeOffset - 0.1f;
		PoseSample poseSample = _poseBuffer[0];
		PoseSample poseSample2 = _poseBuffer[_poseBuffer.Count - 1];
		if (num <= poseSample.HostTime)
		{
			SetWorldPos(poseSample.Position);
			return;
		}
		if (num >= poseSample2.HostTime)
		{
			float num2 = num - poseSample2.HostTime;
			if (num2 > 0.15f || _poseBuffer.Count < 2)
			{
				SetWorldPos(poseSample2.Position);
				return;
			}
			PoseSample poseSample3 = _poseBuffer[_poseBuffer.Count - 2];
			float num3 = Mathf.Max(0.001f, poseSample2.HostTime - poseSample3.HostTime);
			Vector2 vector = (poseSample2.Position - poseSample3.Position) / num3;
			SetWorldPos(poseSample2.Position + vector * Mathf.Min(num2, 0.15f));
			return;
		}
		for (int i = 0; i < _poseBuffer.Count - 1; i++)
		{
			PoseSample poseSample4 = _poseBuffer[i];
			PoseSample poseSample5 = _poseBuffer[i + 1];
			if (!(num < poseSample4.HostTime) && !(num > poseSample5.HostTime))
			{
				float t = (num - poseSample4.HostTime) / Mathf.Max(0.001f, poseSample5.HostTime - poseSample4.HostTime);
				SetWorldPos(Vector2.Lerp(poseSample4.Position, poseSample5.Position, t));
				break;
			}
		}
	}

	private void SetWorldPos(Vector2 pos)
	{
		Vector3 position = Enemy.transform.position;
		Vector2.Distance(position, pos);
		_ = 4f;
		Enemy.transform.position = new Vector3(pos.x, pos.y, position.z);
	}

	public void ApplyVisualHitFlash()
	{
		if (!(Enemy == null) && !VisuallyDead && !BodyHidden && !SoftHidden && Enemy.Animator != null && Enemy.Animator.enabled)
		{
			Enemy.Animator.Update(0f);
		}
	}

	public void SetParryable(bool parryable)
	{
		if (!(Enemy == null))
		{
			Enemy.IsParryable = parryable;
			LastSentParryable = parryable;
		}
	}

	public void SoftHide()
	{
		if (Enemy == null || SoftHidden)
		{
			return;
		}
		SoftHidden = true;
		RecordColliderState();
		RecordRendererState();
		DisableAllColliders();
		Renderer[] componentsInChildren = Enemy.GetComponentsInChildren<Renderer>(includeInactive: true);
		for (int i = 0; i < componentsInChildren.Length; i++)
		{
			if (componentsInChildren[i] != null)
			{
				componentsInChildren[i].enabled = false;
			}
		}
		EnemyBehaviour enemyBehaviour = Enemy.EnemyBehaviour;
		if (enemyBehaviour != null)
		{
			enemyBehaviour.StopBehaviour();
			enemyBehaviour.StopMovement();
			enemyBehaviour.enabled = false;
		}
		if (Enemy.Animator != null)
		{
			Enemy.Animator.fireEvents = false;
		}
		try
		{
			HideBlobShadow();
		}
		catch
		{
		}
	}

	public void SoftUnhide()
	{
		if (Enemy == null || !SoftHidden || VisuallyDead || Enemy.Dead || !LastSentAlive)
		{
			return;
		}
		SoftHidden = false;
		RestoreColliderState();
		for (int i = 0; i < _rendererSnaps.Count; i++)
		{
			if (_rendererSnaps[i].Renderer != null)
			{
				_rendererSnaps[i].Renderer.enabled = _rendererSnaps[i].Enabled;
			}
		}
		ShowBlobShadow();
	}

	public void SetPuppet(bool puppet)
	{
		if (VisuallyDead || (Enemy != null && Enemy.Dead) || !LastSentAlive)
		{
			IsPuppet = true;
			ApplyPuppetState(puppet: true);
			if (BodyHidden)
			{
				HardHideBody();
			}
			else
			{
				SuppressOutboundGameplay();
			}
		}
		else if (IsPuppet == puppet && Enemy != null)
		{
			ApplyPuppetState(puppet);
		}
		else
		{
			IsPuppet = puppet;
			ApplyPuppetState(puppet);
		}
	}

	public bool ForceVisualDeath()
	{
		if (Enemy == null)
		{
			return false;
		}
		if (VisuallyDead)
		{
			if (!BodyHidden)
			{
				try
				{
					HideBlobShadow();
				}
				catch
				{
				}
				if (HideBodyAt > 0f && Time.time >= HideBodyAt)
				{
					HardHideBody();
				}
			}
			return false;
		}
		VisuallyDead = true;
		LastSentAlive = false;
		LastSentLife = 0f;
		Executable = false;
		LastSentExecutable = false;
		IsPuppet = true;
		BodyHidden = false;
		SoftHidden = false;
		HideBodyAt = Time.time + 3f;
		_poseBuffer.Clear();
		DestroyLocalExecutionPrompt();
		ApplyPuppetState(puppet: true);
		SuppressOutboundGameplay();
		DisableReceiveHurtboxes();
		try
		{
			HideBlobShadow();
		}
		catch
		{
		}
		TryPlayDeathClipVisual();
		if (Enemy.Animator != null)
		{
			Enemy.Animator.enabled = true;
			Enemy.Animator.fireEvents = false;
		}
		return true;
	}

	public void DestroyLocalExecutionPrompt()
	{
		LocalPromptSpawned = false;
		if (Enemy == null)
		{
			return;
		}
		try
		{
			EntityExecution componentInChildren = Enemy.GetComponentInChildren<EntityExecution>(includeInactive: true);
			if (componentInChildren != null)
			{
				componentInChildren.DestroyExecution();
			}
		}
		catch
		{
		}
	}

	public void TickDeferredHide()
	{
		if (VisuallyDead && !BodyHidden && !(HideBodyAt <= 0f) && !(Time.time < HideBodyAt))
		{
			if (Enemy == null || !Enemy.gameObject.activeInHierarchy || (Enemy.SpriteRenderer != null && !Enemy.SpriteRenderer.enabled))
			{
				BodyHidden = true;
				HideBodyAt = 0f;
			}
			else
			{
				HardHideBody();
			}
		}
	}

	public void HardHideBody()
	{
		if (!(Enemy == null))
		{
			BodyHidden = true;
			HideBodyAt = 0f;
			DeathDespawnSent = true;
			LastSentDespawned = true;
			DestroyLocalExecutionPrompt();
			DisableAllColliders();
			try
			{
				HideBlobShadow();
			}
			catch
			{
			}
			if (Enemy.Animator != null)
			{
				Enemy.Animator.enabled = false;
			}
			if (Enemy.SpriteRenderer != null)
			{
				Enemy.SpriteRenderer.enabled = false;
			}
			SpriteRenderer[] componentsInChildren = Enemy.GetComponentsInChildren<SpriteRenderer>(includeInactive: true);
			for (int i = 0; i < componentsInChildren.Length; i++)
			{
				componentsInChildren[i].enabled = false;
			}
			if (Enemy.gameObject != null && Enemy.gameObject.activeSelf)
			{
				Enemy.gameObject.SetActive(value: false);
			}
		}
	}

	private void HideBlobShadow()
	{
		if (Enemy == null)
		{
			return;
		}
		if (Enemy.Status != null)
		{
			Enemy.Status.CastShadow = false;
		}
		EntityShadow componentInChildren = Enemy.GetComponentInChildren<EntityShadow>(includeInactive: true);
		if (componentInChildren != null)
		{
			componentInChildren.enabled = false;
		}
		BlobShadow blobShadow = Enemy.Shadow;
		if (blobShadow == null && componentInChildren != null)
		{
			blobShadow = componentInChildren.BlobShadow;
		}
		if (!(blobShadow == null))
		{
			blobShadow.SetShadowAlpha(0f);
			blobShadow.enabled = false;
			foreach (SpriteRenderer renderer in blobShadow.GetComponentsInChildren<SpriteRenderer>(true))
			{
				renderer.enabled = false;
			}
			if (blobShadow.gameObject != null)
			{
				blobShadow.gameObject.SetActive(value: false);
			}
		}
	}

	private void ShowBlobShadow()
	{
		if (Enemy == null || VisuallyDead || Enemy.Dead)
		{
			return;
		}
		if (Enemy.Status != null)
		{
			Enemy.Status.CastShadow = true;
		}
		EntityShadow componentInChildren = Enemy.GetComponentInChildren<EntityShadow>(includeInactive: true);
		if (componentInChildren != null)
		{
			componentInChildren.enabled = true;
		}
		BlobShadow blobShadow = Enemy.Shadow;
		if (blobShadow == null && componentInChildren != null)
		{
			blobShadow = componentInChildren.BlobShadow;
		}
		if (!(blobShadow == null))
		{
			if (blobShadow.gameObject != null)
			{
				blobShadow.gameObject.SetActive(value: true);
			}
			blobShadow.enabled = true;
			blobShadow.SetShadowAlpha(1f);
			foreach (SpriteRenderer renderer in blobShadow.GetComponentsInChildren<SpriteRenderer>(true))
			{
				renderer.enabled = true;
			}
		}
	}

	private void TryPlayDeathClipVisual()
	{
		if (Enemy == null || Enemy.Animator == null)
		{
			return;
		}
		Animator animator = Enemy.Animator;
		string[] array = new string[7] { "Death", "death", "Dead", "Die", "die", "DeathAnimation", "EnemyDeath" };
		for (int i = 0; i < array.Length; i++)
		{
			int num = Animator.StringToHash(array[i]);
			if (animator.HasState(0, num))
			{
				animator.Play(num, 0, 0f);
				animator.Update(0f);
				LastSentAnimHash = num;
				break;
			}
		}
	}

	private void ApplyPuppetState(bool puppet)
	{
		if (Enemy == null)
		{
			return;
		}
		RecordColliderState();
		RecordAnimatorFireEvents();
		EnemyBehaviour enemyBehaviour = Enemy.EnemyBehaviour;
		if (enemyBehaviour != null)
		{
			if (puppet)
			{
				enemyBehaviour.StopBehaviour();
				enemyBehaviour.StopMovement();
				enemyBehaviour.enabled = false;
			}
			else if (!VisuallyDead && !Enemy.Dead)
			{
				enemyBehaviour.enabled = true;
				enemyBehaviour.StartBehaviour();
			}
		}
		if (Enemy.Animator != null && !BodyHidden)
		{
			Enemy.Animator.enabled = true;
			Enemy.Animator.fireEvents = !puppet && _originalFireEvents;
		}
		if (puppet && !VisuallyDead)
		{
			SuppressOutboundGameplay();
		}
		else if (!puppet)
		{
			RestoreColliderState();
		}
	}

	private void RecordColliderState()
	{
		if (Enemy == null || _recordedColliders)
		{
			return;
		}
		if (EnemyPristineRegistry.TryGetColliders(Enemy, out var snaps))
		{
			_colliderSnapshots.Clear();
			for (int i = 0; i < snaps.Count; i++)
			{
				_colliderSnapshots.Add(new ColliderSnapshot
				{
					Collider = snaps[i].Collider,
					Enabled = snaps[i].Enabled
				});
			}
			_recordedColliders = true;
			return;
		}
		_colliderSnapshots.Clear();
		Collider2D[] componentsInChildren = Enemy.GetComponentsInChildren<Collider2D>(includeInactive: true);
		for (int j = 0; j < componentsInChildren.Length; j++)
		{
			if (!(componentsInChildren[j] == null))
			{
				_colliderSnapshots.Add(new ColliderSnapshot
				{
					Collider = componentsInChildren[j],
					Enabled = componentsInChildren[j].enabled
				});
			}
		}
		_recordedColliders = true;
	}

	private void RecordAnimatorFireEvents()
	{
		if (!(Enemy == null) && !(Enemy.Animator == null) && !_recordedFireEvents)
		{
			if (EnemyPristineRegistry.TryGetFireEvents(Enemy, out var fireEvents))
			{
				_originalFireEvents = fireEvents;
			}
			else
			{
				_originalFireEvents = true;
			}
			_recordedFireEvents = true;
		}
	}

	private void RecordRendererState()
	{
		if (Enemy == null || _recordedRenderers)
		{
			return;
		}
		_rendererSnaps.Clear();
		Renderer[] componentsInChildren = Enemy.GetComponentsInChildren<Renderer>(includeInactive: true);
		for (int i = 0; i < componentsInChildren.Length; i++)
		{
			if (!(componentsInChildren[i] == null))
			{
				_rendererSnaps.Add(new RendererSnap
				{
					Renderer = componentsInChildren[i],
					Enabled = componentsInChildren[i].enabled
				});
			}
		}
		_recordedRenderers = true;
	}

	private void RestoreColliderState()
	{
		for (int i = 0; i < _colliderSnapshots.Count; i++)
		{
			ColliderSnapshot colliderSnapshot = _colliderSnapshots[i];
			if (colliderSnapshot.Collider != null)
			{
				colliderSnapshot.Collider.enabled = colliderSnapshot.Enabled;
			}
		}
		if (Enemy != null && Enemy.Animator != null && _recordedFireEvents)
		{
			Enemy.Animator.fireEvents = _originalFireEvents;
		}
	}

	private void SuppressOutboundGameplay()
	{
		if (Enemy == null)
		{
			return;
		}
		Collider2D[] componentsInChildren = Enemy.GetComponentsInChildren<Collider2D>(includeInactive: true);
		foreach (Collider2D collider2D in componentsInChildren)
		{
			if (!(collider2D == null))
			{
				if (collider2D.GetComponentInParent<ContactDamage>() != null || collider2D.GetComponentInParent<AttackArea>() != null)
				{
					collider2D.enabled = false;
				}
				else
				{
					_ = collider2D.GetComponentInParent<DamageArea>() != null;
				}
			}
		}
	}

	private void DisableReceiveHurtboxes()
	{
		if (Enemy == null)
		{
			return;
		}
		Collider2D[] componentsInChildren = Enemy.GetComponentsInChildren<Collider2D>(includeInactive: true);
		foreach (Collider2D collider2D in componentsInChildren)
		{
			if (collider2D != null && collider2D.GetComponentInParent<DamageArea>() != null)
			{
				collider2D.enabled = false;
			}
		}
	}

	private void DisableAllColliders()
	{
		if (Enemy == null)
		{
			return;
		}
		Collider2D[] componentsInChildren = Enemy.GetComponentsInChildren<Collider2D>(includeInactive: true);
		for (int i = 0; i < componentsInChildren.Length; i++)
		{
			if (componentsInChildren[i] != null)
			{
				componentsInChildren[i].enabled = false;
			}
		}
	}
}


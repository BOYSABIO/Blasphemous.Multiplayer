using System.Linq;
using Blasphemous.ModdingAPI;
using Blasphemous.Multiplayer.Client.Data;
using Blasphemous.Multiplayer.Client.Enemies;
using Blasphemous.Multiplayer.Client.PvP.Models;
using Framework.Managers;
using Gameplay.GameControllers.Entities;
using Tools.Level.Interactables;
using UnityEngine;

namespace Blasphemous.Multiplayer.Client.Players;

public class OtherPlayerScript : MonoBehaviour, IDamageable
{
	public Vector2 LatestNetworkPosition => _hasPositionTarget ? _positionTarget : (Vector2)transform.position;
	private PlayerStatus playerStatus;

	private bool playerDead;

	private Vector2 _positionTarget;

	private bool _hasPositionTarget;

	private bool _snapNextPosition;

	private const float PositionSmooth = 20f;

	private const float PositionSnapDistance = 3f;

	private int _executionNetId;

	private bool _playingExecution;

	private readonly AttackType[] _blockedAttacks = new AttackType[15]
	{
		AttackType.Ranged,
		AttackType.ChargedProjectile,
		AttackType.RangedExplosion,
		AttackType.Gem,
		AttackType.PrayerHit,
		AttackType.Taranto,
		AttackType.Verdiales,
		AttackType.Lorquiana,
		AttackType.Tirana,
		AttackType.PoisonMist,
		AttackType.Shield,
		AttackType.Miriam,
		AttackType.Aubade,
		AttackType.Cherubs,
		AttackType.CanteJondo
	};

	private SpriteRenderer CharacterRenderer { get; set; }

	private Animator CharacterAnim { get; set; }

	public OtherPlayerAttackScript OtherPlayerAttack { get; private set; }

	public string PlayerName => playerStatus?.Name ?? string.Empty;

	public bool IsFacingRight
	{
		get
		{
			if (CharacterRenderer != null)
			{
				return !CharacterRenderer.flipX;
			}
			return false;
		}
	}

	public bool IsDead => playerDead;

	public Vector2 CurrentPosition
	{
		set
		{
			_positionTarget = value;
			if (!_hasPositionTarget || _snapNextPosition || Vector2.Distance(base.transform.position, value) > 3f)
			{
				base.transform.position = value;
				_snapNextPosition = false;
			}
			_hasPositionTarget = true;
		}
	}

	public byte CurrentAnimation
	{
		set
		{
			if (playerDead)
			{
				SetDeathStatus(dead: false);
			}
			if (value < 240)
			{
				if (playerStatus.SpecialAnimation > 0 || _playingExecution)
				{
					CharacterAnim.runtimeAnimatorController = UnityReferences.PlayerAnimator;
					playerStatus.SpecialAnimation = 0;
					_playingExecution = false;
					_executionNetId = 0;
				}
				CharacterAnim.SetBool("IS_CROUCH", value: false);
				PlayerAnimState playerAnimState = AnimationStates.animations[value];
				for (int i = 0; i < playerAnimState.parameterNames.Length; i++)
				{
					CharacterAnim.SetBool(playerAnimState.parameterNames[i], playerAnimState.parameterValues[i]);
				}
				CharacterAnim.Play(playerAnimState.name);
			}
			else if (PlaySpecialAnimation(value))
			{
				playerStatus.SpecialAnimation = value;
				ModLog.Info("Playing special animation for " + base.name);
			}
			else
			{
				ModLog.Warn("Failed to play special animation for " + base.name);
			}
		}
	}

	public bool CurrentDirection
	{
		set
		{
			CharacterRenderer.flipX = value;
		}
	}

	public void SetupPlayer(PlayerStatus status)
	{
		playerStatus = status;
		playerStatus.SkinUpdateStatus = PlayerStatus.SkinStatus.NoUpdate;
		CharacterRenderer = base.gameObject.AddComponent<SpriteRenderer>();
		CharacterRenderer.material = UnityReferences.PlayerMaterial;
		CharacterRenderer.sortingLayerName = "Player";
		CharacterRenderer.enabled = false;
		CharacterAnim = base.gameObject.AddComponent<Animator>();
		CharacterAnim.runtimeAnimatorController = UnityReferences.PlayerAnimator;
		EnsureEnemyInteractHurtbox();
		GameObject gameObject = new GameObject("Sword");
		gameObject.transform.SetParent(base.transform);
		OtherPlayerAttack = gameObject.AddComponent<OtherPlayerAttackScript>();
		OtherPlayerAttack.SetupAttack();
	}

	private void EnsureEnemyInteractHurtbox()
	{
		int num = LayerMask.NameToLayer("Penitent");
		if (num >= 0)
		{
			base.gameObject.layer = num;
		}
		if (GetComponent<Rigidbody2D>() == null)
		{
			Rigidbody2D rigidbody2D = base.gameObject.AddComponent<Rigidbody2D>();
			rigidbody2D.isKinematic = true;
			rigidbody2D.simulated = true;
			rigidbody2D.gravityScale = 0f;
		}
		BoxCollider2D boxCollider2D = GetComponent<BoxCollider2D>();
		if (boxCollider2D == null)
		{
			boxCollider2D = base.gameObject.AddComponent<BoxCollider2D>();
		}
		boxCollider2D.isTrigger = true;
		boxCollider2D.offset = new Vector2(0f, 0.95f);
		boxCollider2D.size = new Vector2(0.85f, 2f);
	}

	public void SnapPosition(Vector2 position)
	{
		_snapNextPosition = true;
		CurrentPosition = position;
	}

	public void PlayExecutionAnimation(int netId, int stateHash, float normalizedTime)
	{
		if (CharacterAnim == null || stateHash == 0)
		{
			return;
		}
		Execution execution = FindNearbyExecutionPrompt(netId);
		if (execution != null)
		{
			Animator animator = null;
			foreach (Transform item in execution.transform)
			{
				if (item != null && item.name.ToLower().Contains("interactor"))
				{
					animator = item.GetComponent<Animator>();
					break;
				}
			}
			if (animator != null && animator.runtimeAnimatorController != null)
			{
				CharacterAnim.runtimeAnimatorController = animator.runtimeAnimatorController;
				SpriteRenderer[] componentsInChildren = execution.GetComponentsInChildren<SpriteRenderer>(includeInactive: true);
				for (int i = 0; i < componentsInChildren.Length; i++)
				{
					if (componentsInChildren[i] != null)
					{
						componentsInChildren[i].enabled = false;
					}
				}
			}
		}
		CharacterAnim.Play(stateHash, 0, Mathf.Clamp01(normalizedTime % 1f));
		CharacterAnim.Update(0f);
		_playingExecution = true;
		_executionNetId = netId;
		if (playerStatus != null)
		{
			playerStatus.SpecialAnimation = 252;
		}
	}

	private Execution FindNearbyExecutionPrompt(int netId)
	{
		Execution[] array = Object.FindObjectsOfType<Execution>();
		if (array == null || array.Length == 0)
		{
			return null;
		}
		Vector3 position = base.transform.position;
		Execution result = null;
		float num = 8f;
		for (int i = 0; i < array.Length; i++)
		{
			if (!(array[i] == null))
			{
				float num2 = Vector3.Distance(array[i].transform.position, position);
				if (num2 < num)
				{
					num = num2;
					result = array[i];
				}
			}
		}
		return result;
	}

	public void ApplySkinTexture()
	{
		CharacterRenderer.enabled = true;
		CharacterRenderer.material.SetTexture("_PaletteTex", playerStatus.SkinTexture);
	}

	private bool PlaySpecialAnimation(byte type)
	{
		switch (type)
		{
		case 240:
		case 241:
		case 242:
		{
			PrieDieu prieDieu = Object.FindObjectOfType<PrieDieu>();
			if (prieDieu == null)
			{
				return false;
			}
			CharacterAnim.runtimeAnimatorController = prieDieu.transform.GetChild(4).GetComponent<Animator>().runtimeAnimatorController;
			switch (type)
			{
			case 240:
				CharacterAnim.SetTrigger("ACTIVATION");
				break;
			case 241:
				CharacterAnim.SetTrigger("KNEE_START");
				break;
			default:
				CharacterAnim.Play("Stand Up");
				break;
			}
			break;
		}
		case 243:
		case 244:
		{
			CollectibleItem collectibleItem = Object.FindObjectOfType<CollectibleItem>();
			if (collectibleItem == null)
			{
				return false;
			}
			CharacterAnim.runtimeAnimatorController = collectibleItem.transform.GetChild(1).GetComponent<Animator>().runtimeAnimatorController;
			CharacterAnim.Play((type == 244) ? "Floor Collection" : "Halfheight Collection");
			break;
		}
		case 245:
		{
			Chest chest = Object.FindObjectOfType<Chest>();
			if (chest == null)
			{
				return false;
			}
			CharacterAnim.runtimeAnimatorController = chest.transform.GetChild(2).GetComponent<Animator>().runtimeAnimatorController;
			CharacterAnim.SetTrigger("USED");
			break;
		}
		case 246:
		{
			Lever lever = Object.FindObjectOfType<Lever>();
			if (lever == null)
			{
				return false;
			}
			CharacterAnim.runtimeAnimatorController = lever.transform.GetChild(2).GetComponent<Animator>().runtimeAnimatorController;
			CharacterAnim.SetTrigger("DOWN");
			break;
		}
		case 247:
		case 248:
		case 249:
		{
			Door door = Object.FindObjectOfType<Door>();
			if (door == null)
			{
				return false;
			}
			CharacterAnim.runtimeAnimatorController = door.transform.GetChild(3).GetComponent<Animator>().runtimeAnimatorController;
			switch (type)
			{
			case 247:
				CharacterAnim.SetTrigger("OPEN_ENTER");
				break;
			case 248:
				CharacterAnim.SetTrigger("CLOSED_ENTER");
				break;
			default:
				CharacterAnim.SetTrigger("KEY_ENTER");
				break;
			}
			break;
		}
		case 250:
		case 251:
		{
			GameObject gameObject = GameObject.Find("LOGIC");
			if (gameObject == null)
			{
				return false;
			}
			CharacterAnim.runtimeAnimatorController = gameObject.transform.GetChild(3).GetComponent<Animator>().runtimeAnimatorController;
			CharacterAnim.Play((type == 250) ? "FakePenitent laydown" : "FakePenitent gettingUp");
			break;
		}
		default:
			return false;
		}
		return true;
	}

	public void FinishSpecialAnimation()
	{
		if (playerStatus.SpecialAnimation >= 247 && playerStatus.SpecialAnimation <= 249)
		{
			CharacterRenderer.enabled = false;
		}
		CurrentAnimation = 0;
	}

	private void Update()
	{
		if (_hasPositionTarget && !playerDead)
		{
			Vector2 a = base.transform.position;
			float num = Vector2.Distance(a, _positionTarget);
			if (num > 3f)
			{
				base.transform.position = _positionTarget;
			}
			else if (num > 0.001f)
			{
				float t = 1f - Mathf.Exp(-20f * Time.deltaTime);
				Vector2 vector = Vector2.Lerp(a, _positionTarget, t);
				base.transform.position = new Vector3(vector.x, vector.y, base.transform.position.z);
			}
		}
		if (!(CharacterAnim == null))
		{
			AnimatorStateInfo currentAnimatorStateInfo = CharacterAnim.GetCurrentAnimatorStateInfo(0);
			bool flag = currentAnimatorStateInfo.IsName("Death") || currentAnimatorStateInfo.IsName("Death Spike") || currentAnimatorStateInfo.IsName("Death Fall") || currentAnimatorStateInfo.IsName("Grounding Over");
			if (!playerDead && flag && currentAnimatorStateInfo.normalizedTime >= 0.95f)
			{
				SetDeathStatus(dead: true);
				playerDead = true;
			}
			else if (playerDead && !flag && currentAnimatorStateInfo.normalizedTime > 0f && (currentAnimatorStateInfo.IsName("Idle") || currentAnimatorStateInfo.IsName("Run") || currentAnimatorStateInfo.IsName("Jump") || currentAnimatorStateInfo.IsName("Fall") || currentAnimatorStateInfo.IsName("Landing")))
			{
				playerDead = false;
				SetDeathStatus(dead: false);
			}
		}
	}

	private void SetDeathStatus(bool dead)
	{
		if (CharacterAnim != null)
		{
			CharacterAnim.enabled = !dead;
		}
		OtherPlayerAttack?.SetHitboxStatus(!dead);
		BoxCollider2D component = GetComponent<BoxCollider2D>();
		if (component != null)
		{
			component.enabled = !dead;
		}
	}

	public void LaunchEvent(string eventName)
	{
		if (eventName == "INTERACTION_END")
		{
			FinishSpecialAnimation();
		}
	}

	public bool BleedOnImpact()
	{
		return false;
	}

	public bool SparkOnImpact()
	{
		return false;
	}

	public Vector3 GetPosition()
	{
		return base.transform.position;
	}

	public void Damage(Hit hit)
	{
		Enemy enemy = ((hit.AttackingEntity != null) ? hit.AttackingEntity.GetComponentInParent<Enemy>() : null);
		// Warden pooled areas are Weapon roots rather than children of the boss.
		// Their native SetOwner supplies the same authoritative attacker identity.
		if (enemy == null && hit.AttackingEntity != null)
		{
			var weapon = hit.AttackingEntity.GetComponentInParent<Gameplay.GameControllers.Entities.Weapon.Weapon>();
			if (weapon != null && weapon.WeaponOwner is Gameplay.GameControllers.Bosses.ElderBrother.ElderBrother warden) enemy = warden;
		}
		if (enemy != null && EnemyManager.IsSyncableEnemy(enemy))
		{
			if (!playerDead)
			{
				Main.Multiplayer.EnemyManager?.NotifyRemotePlayerHitByEnemy(playerStatus.Name, hit.DamageAmount, enemy, hit);
			}
			return;
		}
		Config config = Main.Multiplayer.config;
		if (config.enablePvP && (config.enableFriendlyFire || Main.Multiplayer.PlayerTeam != playerStatus.Team))
		{
			AttackType attackType = CalculateAttack(hit);
			if (attackType != AttackType.NoDamage && !_blockedAttacks.Contains(attackType))
			{
				float num = Main.Multiplayer.DamageCalculator.CalculateOffense(attackType);
				ModLog.Warn($"Sending hit {attackType} to {playerStatus.Name} ({num} damage)");
				Core.Logic.Penitent.Audio.PlaySimpleHitToEnemy();
				Main.Multiplayer.AttackManager.ShowDamageEffects(playerStatus.Name);
				Main.Multiplayer.NetworkManager.SendAttack(playerStatus.Name, attackType, (byte)num);
			}
		}
	}

	private AttackType CalculateAttack(Hit hit)
	{
		string text = hit.AttackingEntity.name;
		string text2 = AnimationStates.animations.Select((PlayerAnimState x) => x.name).FirstOrDefault((string x) => Core.Logic.Penitent.Animator.GetCurrentAnimatorStateInfo(0).IsName(x)) ?? "Unknown";
		DamageArea.DamageType damageType = hit.DamageType;
		DamageArea.DamageElement damageElement = hit.DamageElement;
		float force = hit.Force;
		ModLog.Info($"{playerStatus.Name} received damage [{text},{text2},{damageType},{damageElement},{force}]");
		switch (text)
		{
		case "Penitent(Clone)":
			switch (text2)
			{
			case "Combo_4":
				return AttackType.ComboNormal;
			case "ComboFinisherUp":
				return AttackType.ComboUp;
			case "ComboFinisherDown":
				return AttackType.ComboDown;
			case "Charged Attack":
				if (hit.Force != 0f)
				{
					return AttackType.Charged;
				}
				return AttackType.ChargedProjectile;
			case "AuraTransform":
				if (force != 0f)
				{
					return AttackType.Lorquiana;
				}
				return AttackType.PrayerHit;
			default:
				if (Core.Logic.Penitent.LungeAttack.IsUsingAbility)
				{
					return AttackType.Lunge;
				}
				if (Core.Logic.Penitent.VerticalAttack.IsUsingAbility)
				{
					return AttackType.Vertical;
				}
				if (force != 0f)
				{
					return AttackType.Slash;
				}
				return AttackType.RangedExplosion;
			}
		case "RangeAttackProjectile(Clone)":
			return AttackType.Ranged;
		case "PenitentCloisteredGemBullet(Clone)":
			return AttackType.Gem;
		default:
			if (!text.StartsWith("ElmFireTrap"))
			{
				switch (text)
				{
				case "PenitentVerticalBeam(Clone)":
					return AttackType.Debla;
				case "PenitentTarantoDivineLight(Clone)":
					return AttackType.Taranto;
				case "CrawlerBullet_Base(Clone)":
					return AttackType.Verdiales;
				case "PrayerPoisonAreaEffect(Clone)":
					return AttackType.PoisonMist;
				default:
					if (text.StartsWith("PenitentShield"))
					{
						return AttackType.Shield;
					}
					if (text == "MiriamPortalPrayer(Clone)")
					{
						return AttackType.Miriam;
					}
					if (text.StartsWith("MiriamSpike"))
					{
						return AttackType.MiriamSpike;
					}
					if (text == "GuardianPrayer(Clone)")
					{
						return AttackType.Aubade;
					}
					return AttackType.NoDamage;
				}
			}
			goto case "PR203ElmFireTrapLightning(Clone)";
		case "PR203ElmFireTrapLightning(Clone)":
			return AttackType.Tirana;
		}
	}
}

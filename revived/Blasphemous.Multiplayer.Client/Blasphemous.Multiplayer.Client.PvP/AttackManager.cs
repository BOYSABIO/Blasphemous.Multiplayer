using System;
using System.Collections.Generic;
using Blasphemous.ModdingAPI;
using Blasphemous.Multiplayer.Client.Players;
using Blasphemous.Multiplayer.Client.PvP.Models;
using Framework.Managers;
using Gameplay.GameControllers.Effects.Player.Sparks;
using Gameplay.GameControllers.Entities;
using UnityEngine;

namespace Blasphemous.Multiplayer.Client.PvP;

public class AttackManager
{
	private readonly Dictionary<AttackType, AttackData> allAttacks;

	public AttackManager()
	{
		allAttacks = new Dictionary<AttackType, AttackData>();
		LoadAttacks();
	}

	public void ReceiveAttack(string attackerName, string receiverName, AttackType attack, byte damageAmount)
	{
		if (Core.Logic.Penitent == null || !allAttacks.ContainsKey(attack))
		{
			return;
		}
		if (receiverName == Main.Multiplayer.PlayerName)
		{
			OtherPlayerScript otherPlayerScript = Main.Multiplayer.OtherPlayerManager.FindActivePlayer(attackerName);
			if (!(otherPlayerScript == null) && !Core.Input.HasBlocker("ANY"))
			{
				Config config = Main.Multiplayer.config;
				byte team = Main.Multiplayer.OtherPlayerManager.FindConnectedPlayer(attackerName).Team;
				if (config.enablePvP && (config.enableFriendlyFire || Main.Multiplayer.PlayerTeam != team))
				{
					ModLog.Warn($"Receiving hit {attack} from {attackerName}");
					DamagePlayer(attack, damageAmount, otherPlayerScript.gameObject);
				}
			}
		}
		else
		{
			ShowDamageEffects(receiverName);
		}
	}

	public void ReceiveEffect(string playerName, EffectType effect)
	{
		OtherPlayerScript otherPlayerScript = Main.Multiplayer.OtherPlayerManager.FindActivePlayer(playerName);
		if (!(otherPlayerScript == null))
		{
			otherPlayerScript.OtherPlayerAttack.PlayEffectAnimation(effect, otherPlayerScript.IsFacingRight);
		}
	}

	private void DamagePlayer(AttackType attack, byte damageAmount, GameObject attacker)
	{
		float damageAmount2 = Main.Multiplayer.DamageCalculator.CalculateDefense(attack, damageAmount);
		AttackData attackData = GetAttackData(attack);
		Hit hit = new Hit
		{
			AttackingEntity = attacker,
			DamageAmount = damageAmount2,
			DamageElement = attackData.DamageElement,
			DamageType = attackData.DamageType,
			Force = attackData.Force,
			ThrowbackDirByOwnerPosition = true,
			Unparriable = true,
			Unblockable = true,
			Unnavoidable = true,
			HitSoundId = "PVP" + attackData.SoundId
		};
		Core.Logic.Penitent.Damage(hit);
	}

	public void DamagePlayer_Internal(AttackType type, byte amount)
	{
		DamagePlayer(type, amount, Core.Logic.Penitent.gameObject);
	}

	public void ShowDamageEffects(string receiverName)
	{
		OtherPlayerScript otherPlayerScript = Main.Multiplayer.OtherPlayerManager.FindActivePlayer(receiverName);
		if (!(otherPlayerScript == null))
		{
			Vector3 position = otherPlayerScript.transform.position + Vector3.up;
			Core.Logic.Penitent.GetComponentInChildren<SwordSparkSpawner>().GetSwordSpark(position);
			Core.Logic.Penitent.GetComponentInChildren<BloodSpawner>().GetBloodFX(BloodSpawner.BLOOD_FX_TYPES.SMALL).transform.position = position;
			Core.Logic.Penitent.Audio.PlaySimpleHitToEnemy();
		}
	}

	public AttackData GetAttackData(AttackType type)
	{
		if (allAttacks.TryGetValue(type, out var value))
		{
			return value;
		}
		throw new Exception($"Attack type \"{type}\" doesn't exist!");
	}

	private void LoadAttacks()
	{
		if (!Main.Multiplayer.FileHandler.LoadDataAsJson<AttackData[]>("attacks.json", out var output))
		{
			ModLog.Error("Failed to load attack data!");
			return;
		}
		AttackData[] array = output;
		foreach (AttackData attackData in array)
		{
			allAttacks.Add(attackData.Name, attackData);
		}
		ModLog.Info("Successfully loaded all attack data!");
	}
}

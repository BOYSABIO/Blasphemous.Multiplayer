using System;
using System.Linq;
using Blasphemous.ModdingAPI;
using Blasphemous.ModdingAPI.Helpers;
using Blasphemous.Multiplayer.Client.PvP.Models;
using Framework.Managers;
using Gameplay.GameControllers.Entities;

namespace Blasphemous.Multiplayer.Client.PvP;

public class DamageCalculator
{
	private readonly OffenseItem[] _offenses;

	private readonly DefenseItem[] _defenses;

	public DamageCalculator()
	{
		Main.Multiplayer.FileHandler.LoadDataAsJson<OffenseItem[]>("offenses.json", out _offenses);
		Main.Multiplayer.FileHandler.LoadDataAsJson<DefenseItem[]>("defenses.json", out _defenses);
	}

	public float CalculateOffense(AttackType attack)
	{
		AttackData data = Main.Multiplayer.AttackManager.GetAttackData(attack);
		float num = (from item in _offenses
			where IsItemEquipped(item.Id) && IsConditionMet(item.Condition)
			select GetIncreaseForType(item, data)).Sum();
		float num2 = ((data.ScalingType != ScalingType.Prayer) ? Core.Logic.Penitent.Stats.Strength.GetUpgrades() : 0);
		float num3 = data.BaseDamage + (num + num2) * data.ScalingAmount;
		ModLog.Info($"CalculateOffense: {data.BaseDamage} + ({num} + {num2}) * {data.ScalingAmount} = {num3}");
		return num3;
	}

	public float CalculateDefense(AttackType attack, byte damage)
	{
		AttackData data = Main.Multiplayer.AttackManager.GetAttackData(attack);
		float num = (from item in _defenses
			where IsItemEquipped(item.Id) && IsConditionMet(item.Condition)
			select 1f - GetReductionForElement(item, data)).Aggregate(1f, (float x, float y) => x * y);
		float num2 = (float)(int)damage * num;
		ModLog.Info($"CalculateDefense: {damage} * {num} = {num2}");
		return num2;
	}

	private bool IsItemEquipped(string item)
	{
		return ItemHelper.GetItemTypeFromId(item) switch
		{
			InventoryManager.ItemType.Bead => Core.InventoryManager.IsRosaryBeadEquipped(item), 
			InventoryManager.ItemType.Prayer => Core.InventoryManager.IsPrayerEquipped(item), 
			InventoryManager.ItemType.Relic => Core.InventoryManager.IsRelicEquipped(item), 
			InventoryManager.ItemType.Sword => Core.InventoryManager.IsSwordEquipped(item), 
			InventoryManager.ItemType.Quest => Core.InventoryManager.IsQuestItemOwned(item), 
			InventoryManager.ItemType.Collectible => Core.InventoryManager.IsCollectibleItemOwned(item), 
			_ => false, 
		};
	}

	private bool IsConditionMet(ConditionType condition)
	{
		return condition switch
		{
			ConditionType.None => true, 
			ConditionType.LowHealth => Core.Logic.Penitent.Stats.Life.MissingRatio <= 0.2f, 
			ConditionType.NoFlasks => Core.Logic.Penitent.Stats.Flask.Current == 0f, 
			_ => throw new Exception($"Invalid condition type: {condition}"), 
		};
	}

	private float GetIncreaseForType(OffenseItem item, AttackData attack)
	{
		return attack.ScalingType switch
		{
			ScalingType.Sword => item.SwordIncrease, 
			ScalingType.Prayer => item.PrayerIncrease, 
			ScalingType.Blood => item.BloodIncrease, 
			_ => throw new Exception($"Invalid scaling type: {attack.ScalingType}"), 
		};
	}

	private float GetReductionForElement(DefenseItem item, AttackData attack)
	{
		return attack.DamageElement switch
		{
			DamageArea.DamageElement.Normal => item.PhysicalReduction, 
			DamageArea.DamageElement.Fire => item.FireReduction, 
			DamageArea.DamageElement.Toxic => item.ToxicReduction, 
			DamageArea.DamageElement.Magic => item.MagicReduction, 
			DamageArea.DamageElement.Lightning => item.LightningReduction, 
			_ => throw new Exception($"Invalid element type: {attack.DamageElement}"), 
		};
	}
}

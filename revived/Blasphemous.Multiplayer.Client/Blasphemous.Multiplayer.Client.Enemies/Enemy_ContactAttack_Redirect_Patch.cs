using Blasphemous.Multiplayer.Client.Players;
using Framework.Managers;
using Gameplay.GameControllers.Enemies.Framework.Attack;
using Gameplay.GameControllers.Entities;
using Gameplay.GameControllers.Penitent;
using HarmonyLib;
using UnityEngine;

namespace Blasphemous.Multiplayer.Client.Enemies;

[HarmonyPatch(typeof(EnemyAttack), "ContactAttack")]
internal class Enemy_ContactAttack_Redirect_Patch
{
	public static void Prefix(EnemyAttack __instance, ref IDamageable damageable)
	{
		if (!Main.Multiplayer.NetworkManager.IsConnected || Main.Multiplayer.EnemyManager == null || !Main.Multiplayer.EnemyManager.IsSceneHost)
		{
			return;
		}
		Enemy enemy = ((__instance != null) ? __instance.GetComponentInParent<Enemy>() : null);
		if (!(enemy == null) && EnemyManager.IsSyncableEnemy(enemy) && !enemy.Dead)
		{
			OtherPlayerScript otherPlayerScript = ResolveRemoteTarget(enemy);
			if (!(otherPlayerScript == null) && !otherPlayerScript.IsDead && (damageable == null || IsLocalPenitent(damageable)) && !IsPenitentInContactRange(enemy))
			{
				damageable = otherPlayerScript;
			}
		}
	}

	private static OtherPlayerScript ResolveRemoteTarget(Enemy enemy)
	{
		if (enemy.Target == null)
		{
			return null;
		}
		return enemy.Target.GetComponent<OtherPlayerScript>() ?? enemy.Target.GetComponentInParent<OtherPlayerScript>();
	}

	private static bool IsLocalPenitent(IDamageable damageable)
	{
		if (damageable is Penitent)
		{
			return true;
		}
		if (damageable is Component component)
		{
			return component.GetComponentInParent<Penitent>() != null;
		}
		return false;
	}

	private static bool IsPenitentInContactRange(Enemy enemy)
	{
		if (Core.Logic.Penitent == null || Core.Logic.Penitent.Dead)
		{
			return false;
		}
		float num = Vector2.Distance(enemy.transform.position, Core.Logic.Penitent.transform.position);
		float num2 = (enemy.IsAttacking ? 2.4f : 1.75f);
		return num <= num2;
	}
}

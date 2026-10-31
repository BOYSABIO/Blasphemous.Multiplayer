using Gameplay.GameControllers.Entities;
using HarmonyLib;

namespace Blasphemous.Multiplayer.Client.Enemies;

[HarmonyPatch(typeof(DamageArea), "TakeDamage")]
internal class Enemy_TakeDamage_Patch
{
	public static bool Prefix(DamageArea __instance, Hit hit)
	{
		return !EnemyDamageHooks.ShouldBlockLocalDamage((__instance != null) ? __instance.GetComponentInParent<Enemy>() : null, hit);
	}
}

using Gameplay.GameControllers.Entities;
using HarmonyLib;

namespace Blasphemous.Multiplayer.Client.Enemies;

[HarmonyPatch(typeof(Entity), "Damage")]
internal class Enemy_EntityDamage_Patch
{
	public static bool Prefix(Entity __instance, float amount)
	{
		return true;
	}
}

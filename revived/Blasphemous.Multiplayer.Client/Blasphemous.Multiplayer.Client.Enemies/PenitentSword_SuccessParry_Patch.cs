using Gameplay.GameControllers.Entities;
using Gameplay.GameControllers.Penitent.Attack;
using HarmonyLib;

namespace Blasphemous.Multiplayer.Client.Enemies;

[HarmonyPatch(typeof(PenitentSword), "SuccessParryChance")]
internal class PenitentSword_SuccessParry_Patch
{
	public static void Postfix(Hit hit, bool __result)
	{
		if (__result)
		{
			Main.Multiplayer?.EnemyManager?.NotifyLocalParry(hit);
		}
	}
}

using Gameplay.GameControllers.Entities;
using HarmonyLib;

namespace Blasphemous.Multiplayer.Client.PvP;

[HarmonyPatch(typeof(Entity), "GetReducedDamage")]
internal class Entity_DamageReduction_Patch
{
	public static void Postfix(Hit hit, ref float __result)
	{
		if (hit.HitSoundId != null && hit.HitSoundId.StartsWith("PVP"))
		{
			__result = hit.DamageAmount;
		}
	}
}

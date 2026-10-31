using System.Collections.Generic;
using Framework.Managers;
using HarmonyLib;

namespace Blasphemous.Multiplayer.Client.PvP;

[HarmonyPatch(typeof(InputManager), "HasBlocker")]
internal class Input_Block_Patch
{
	public static void Postfix(string name, List<string> ___inputBlockers, ref bool __result)
	{
		if (!(name != "ANY"))
		{
			__result = ___inputBlockers.Count > 1 || (___inputBlockers.Count == 1 && ___inputBlockers[0] != "PLAYER_LOGIC");
		}
	}
}

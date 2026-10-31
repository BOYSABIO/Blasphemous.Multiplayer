using Framework.Managers;
using HarmonyLib;
using Tools.Level.Actionables;

namespace Blasphemous.Multiplayer.Client.ProgressSync.Helpers.PersistentObjects;

[HarmonyPatch(typeof(ActionableLadder), "SetCurrentPersistentState")]
internal class LadderLoad_Patch
{
	public static bool Prefix(PersistentManager.PersistentData data, ActionableLadder __instance, ref bool ___open)
	{
		if (data != null)
		{
			return !Main.Multiplayer.ProgressManager.IsObjectInteracted(__instance.GetPersistenID());
		}
		___open = false;
		__instance.Use();
		return false;
	}
}

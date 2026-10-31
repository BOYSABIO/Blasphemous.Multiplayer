using Framework.Managers;
using HarmonyLib;
using Tools.Level.Interactables;

namespace Blasphemous.Multiplayer.Client.ProgressSync.Helpers.PersistentObjects;

[HarmonyPatch(typeof(PrieDieu), "SetCurrentPersistentState")]
internal class PrieDieuLoad_Patch
{
	public static bool Prefix(PersistentManager.PersistentData data, PrieDieu __instance)
	{
		if (data != null)
		{
			return !Main.Multiplayer.ProgressManager.IsObjectInteracted(__instance.GetPersistenID());
		}
		__instance.Ligthed = true;
		return false;
	}
}

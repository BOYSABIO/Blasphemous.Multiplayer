using Framework.Managers;
using HarmonyLib;
using Tools.Level.Interactables;

namespace Blasphemous.Multiplayer.Client.ProgressSync.Helpers.PersistentObjects;

[HarmonyPatch(typeof(Lever), "SetCurrentPersistentState")]
internal class LeverLoad_Patch
{
	public static bool Prefix(PersistentManager.PersistentData data, Lever __instance)
	{
		if (data != null)
		{
			return !Main.Multiplayer.ProgressManager.IsObjectInteracted(__instance.GetPersistenID());
		}
		__instance.Consumed = true;
		__instance.SetLeverDownInstantly();
		return false;
	}
}

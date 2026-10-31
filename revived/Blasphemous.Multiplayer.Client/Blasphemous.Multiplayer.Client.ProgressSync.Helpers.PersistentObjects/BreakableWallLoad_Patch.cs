using Framework.Managers;
using HarmonyLib;
using Tools.Level.Actionables;

namespace Blasphemous.Multiplayer.Client.ProgressSync.Helpers.PersistentObjects;

[HarmonyPatch(typeof(BreakableWall), "SetCurrentPersistentState")]
internal class BreakableWallLoad_Patch
{
	public static bool Prefix(PersistentManager.PersistentData data, BreakableWall __instance)
	{
		if (data != null)
		{
			return !Main.Multiplayer.ProgressManager.IsObjectInteracted(__instance.GetPersistenID());
		}
		__instance.Use();
		return false;
	}
}

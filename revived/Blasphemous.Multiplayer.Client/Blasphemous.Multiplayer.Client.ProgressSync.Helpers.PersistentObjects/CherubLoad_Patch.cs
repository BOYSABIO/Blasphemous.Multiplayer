using Framework.Managers;
using HarmonyLib;

namespace Blasphemous.Multiplayer.Client.ProgressSync.Helpers.PersistentObjects;

[HarmonyPatch(typeof(CherubCaptorPersistentObject), "SetCurrentPersistentState")]
internal class CherubLoad_Patch
{
	public static bool Prefix(PersistentManager.PersistentData data, CherubCaptorPersistentObject __instance)
	{
		if (data != null)
		{
			return !Main.Multiplayer.ProgressManager.IsObjectInteracted(__instance.GetPersistenID());
		}
		__instance.destroyed = true;
		__instance.spawner.DisableCherubSpawn();
		__instance.spawner.DestroySpawnedCherub();
		return false;
	}
}

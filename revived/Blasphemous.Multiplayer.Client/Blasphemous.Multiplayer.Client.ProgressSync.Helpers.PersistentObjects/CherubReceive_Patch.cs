using HarmonyLib;

namespace Blasphemous.Multiplayer.Client.ProgressSync.Helpers.PersistentObjects;

[HarmonyPatch(typeof(CherubCaptorPersistentObject), "GetCurrentPersistentState")]
internal class CherubReceive_Patch
{
	public static bool Prefix(string dataPath, CherubCaptorPersistentObject __instance)
	{
		if (dataPath != "use")
		{
			return true;
		}
		__instance.destroyed = true;
		__instance.spawner.DisableCherubSpawn();
		__instance.spawner.DestroySpawnedCherub();
		return false;
	}
}

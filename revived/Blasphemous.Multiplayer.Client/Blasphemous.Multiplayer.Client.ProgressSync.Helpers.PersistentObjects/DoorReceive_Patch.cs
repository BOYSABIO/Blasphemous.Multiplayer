using HarmonyLib;
using Tools.Level.Interactables;

namespace Blasphemous.Multiplayer.Client.ProgressSync.Helpers.PersistentObjects;

[HarmonyPatch(typeof(Door), "GetCurrentPersistentState")]
internal class DoorReceive_Patch
{
	public static bool Prefix(string dataPath, Door __instance, ref bool ___objectUsed)
	{
		if (dataPath != "use")
		{
			return true;
		}
		___objectUsed = true;
		__instance.Closed = false;
		return false;
	}
}

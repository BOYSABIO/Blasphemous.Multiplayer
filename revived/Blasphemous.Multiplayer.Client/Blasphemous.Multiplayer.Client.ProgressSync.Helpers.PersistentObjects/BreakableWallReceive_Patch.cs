using HarmonyLib;
using Tools.Level.Actionables;

namespace Blasphemous.Multiplayer.Client.ProgressSync.Helpers.PersistentObjects;

[HarmonyPatch(typeof(BreakableWall), "GetCurrentPersistentState")]
internal class BreakableWallReceive_Patch
{
	public static bool Prefix(string dataPath, BreakableWall __instance)
	{
		if (dataPath != "use")
		{
			return true;
		}
		__instance.Use();
		return false;
	}
}

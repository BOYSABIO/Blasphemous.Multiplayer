using HarmonyLib;
using Tools.Level.Actionables;

namespace Blasphemous.Multiplayer.Client.ProgressSync.Helpers.PersistentObjects;

[HarmonyPatch(typeof(ActionableLadder), "GetCurrentPersistentState")]
internal class LadderReceive_Patch
{
	public static bool Prefix(string dataPath, ActionableLadder __instance, ref bool ___open)
	{
		if (dataPath != "use")
		{
			return true;
		}
		___open = false;
		__instance.Use();
		return false;
	}
}

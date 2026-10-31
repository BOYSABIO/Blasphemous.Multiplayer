using HarmonyLib;
using Tools.Level.Interactables;

namespace Blasphemous.Multiplayer.Client.ProgressSync.Helpers.PersistentObjects;

[HarmonyPatch(typeof(PrieDieu), "GetCurrentPersistentState")]
internal class PrieDieuReceive_Patch
{
	public static bool Prefix(string dataPath, PrieDieu __instance)
	{
		if (dataPath != "use")
		{
			return true;
		}
		__instance.Ligthed = true;
		return false;
	}
}

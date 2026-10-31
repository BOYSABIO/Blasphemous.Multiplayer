using Gameplay.GameControllers.Environment.MovingPlatforms;
using HarmonyLib;

namespace Blasphemous.Multiplayer.Client.ProgressSync.Helpers.PersistentObjects;

[HarmonyPatch(typeof(StraightMovingPlatform), "GetCurrentPersistentState")]
internal class MovingPlatformReceive_Patch
{
	public static bool Prefix(string dataPath, StraightMovingPlatform __instance)
	{
		if (dataPath != "use")
		{
			return true;
		}
		__instance.Reset();
		__instance.Use();
		return false;
	}
}

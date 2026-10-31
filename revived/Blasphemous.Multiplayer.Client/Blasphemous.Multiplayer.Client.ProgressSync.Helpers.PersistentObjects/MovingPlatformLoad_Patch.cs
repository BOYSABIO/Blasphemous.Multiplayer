using Framework.Managers;
using Gameplay.GameControllers.Environment.MovingPlatforms;
using HarmonyLib;

namespace Blasphemous.Multiplayer.Client.ProgressSync.Helpers.PersistentObjects;

[HarmonyPatch(typeof(StraightMovingPlatform), "SetCurrentPersistentState")]
internal class MovingPlatformLoad_Patch
{
	public static bool Prefix(PersistentManager.PersistentData data, StraightMovingPlatform __instance)
	{
		if (data != null)
		{
			return !Main.Multiplayer.ProgressManager.IsObjectInteracted(__instance.GetPersistenID());
		}
		__instance.Reset();
		__instance.Use();
		return false;
	}
}

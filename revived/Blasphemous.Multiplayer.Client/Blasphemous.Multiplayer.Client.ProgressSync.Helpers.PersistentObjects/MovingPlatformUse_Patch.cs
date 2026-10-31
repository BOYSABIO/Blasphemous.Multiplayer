using Blasphemous.ModdingAPI;
using Blasphemous.Multiplayer.Client.Data;
using Framework.Managers;
using Gameplay.GameControllers.Environment.MovingPlatforms;
using HarmonyLib;

namespace Blasphemous.Multiplayer.Client.ProgressSync.Helpers.PersistentObjects;

[HarmonyPatch(typeof(StraightMovingPlatform), "Use")]
internal class MovingPlatformUse_Patch
{
	public static void Postfix(StraightMovingPlatform __instance, ref bool ____running)
	{
		if (!Main.Multiplayer.ProgressManager.CurrentlyUpdatingProgress)
		{
			string persistenID = __instance.GetPersistenID();
			ModLog.Info("Activated platform: " + persistenID);
			Main.Multiplayer.ProgressManager.UsePersistentObject(persistenID);
			if (PersistentStates.getObjectSceneIndex(Core.LevelManager.currentLevel.LevelName, persistenID) >= 0)
			{
				____running = true;
			}
		}
	}
}

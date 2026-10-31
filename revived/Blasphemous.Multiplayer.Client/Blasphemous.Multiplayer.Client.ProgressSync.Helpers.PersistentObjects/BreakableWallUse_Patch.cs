using Blasphemous.ModdingAPI;
using HarmonyLib;
using Tools.Level.Actionables;

namespace Blasphemous.Multiplayer.Client.ProgressSync.Helpers.PersistentObjects;

[HarmonyPatch(typeof(BreakableWall), "Damage")]
internal class BreakableWallUse_Patch
{
	public static void Postfix(BreakableWall __instance)
	{
		if (!Main.Multiplayer.ProgressManager.CurrentlyUpdatingProgress)
		{
			string persistenID = __instance.GetPersistenID();
			ModLog.Info("Broke wall: " + persistenID);
			Main.Multiplayer.ProgressManager.UsePersistentObject(persistenID);
		}
	}
}

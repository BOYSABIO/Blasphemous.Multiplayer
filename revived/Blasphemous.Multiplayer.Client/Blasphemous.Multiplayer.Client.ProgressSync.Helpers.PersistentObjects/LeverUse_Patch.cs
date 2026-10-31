using Blasphemous.ModdingAPI;
using HarmonyLib;
using Tools.Level.Interactables;

namespace Blasphemous.Multiplayer.Client.ProgressSync.Helpers.PersistentObjects;

[HarmonyPatch(typeof(Lever), "OnUse")]
internal class LeverUse_Patch
{
	public static void Postfix(Lever __instance)
	{
		if (!Main.Multiplayer.ProgressManager.CurrentlyUpdatingProgress)
		{
			string persistenID = __instance.GetPersistenID();
			ModLog.Info("Used Lever: " + persistenID);
			Main.Multiplayer.ProgressManager.UsePersistentObject(persistenID);
		}
	}
}

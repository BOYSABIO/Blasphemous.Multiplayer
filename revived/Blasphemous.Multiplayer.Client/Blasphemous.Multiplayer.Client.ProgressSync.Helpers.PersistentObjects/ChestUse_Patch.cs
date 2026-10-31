using Blasphemous.ModdingAPI;
using HarmonyLib;
using Tools.Level.Interactables;

namespace Blasphemous.Multiplayer.Client.ProgressSync.Helpers.PersistentObjects;

[HarmonyPatch(typeof(Chest), "OnUse")]
internal class ChestUse_Patch
{
	public static void Postfix(Chest __instance)
	{
		if (!Main.Multiplayer.ProgressManager.CurrentlyUpdatingProgress)
		{
			string persistenID = __instance.GetPersistenID();
			ModLog.Info("Used Chest: " + persistenID);
			Main.Multiplayer.ProgressManager.UsePersistentObject(persistenID);
		}
	}
}

using Blasphemous.ModdingAPI;
using HarmonyLib;

namespace Blasphemous.Multiplayer.Client.ProgressSync.Helpers.PersistentObjects;

[HarmonyPatch(typeof(CherubCaptorPersistentObject), "OnCherubKilled")]
internal class CherubUse_Patch
{
	public static void Postfix(CherubCaptorPersistentObject __instance)
	{
		if (!Main.Multiplayer.ProgressManager.CurrentlyUpdatingProgress)
		{
			string persistenID = __instance.GetPersistenID();
			ModLog.Info("Cherub killed: " + persistenID);
			Main.Multiplayer.ProgressManager.UsePersistentObject(persistenID);
		}
	}
}

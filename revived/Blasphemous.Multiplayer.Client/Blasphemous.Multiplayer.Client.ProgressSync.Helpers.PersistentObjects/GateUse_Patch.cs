using Blasphemous.ModdingAPI;
using HarmonyLib;
using Tools.Level.Actionables;

namespace Blasphemous.Multiplayer.Client.ProgressSync.Helpers.PersistentObjects;

[HarmonyPatch(typeof(Gate), "Use")]
internal class GateUse_Patch
{
	public static void Postfix(Gate __instance)
	{
		if (!Main.Multiplayer.ProgressManager.CurrentlyUpdatingProgress)
		{
			string persistenID = __instance.GetPersistenID();
			ModLog.Info("Gate opened: " + persistenID);
			Main.Multiplayer.ProgressManager.UsePersistentObject(persistenID);
		}
	}
}

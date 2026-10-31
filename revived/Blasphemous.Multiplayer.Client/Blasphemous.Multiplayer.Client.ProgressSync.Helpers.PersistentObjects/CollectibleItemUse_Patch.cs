using Blasphemous.ModdingAPI;
using HarmonyLib;

namespace Blasphemous.Multiplayer.Client.ProgressSync.Helpers.PersistentObjects;

[HarmonyPatch(typeof(CollectibleItem), "OnUse")]
internal class CollectibleItemUse_Patch
{
	public static void Postfix(CollectibleItem __instance)
	{
		if (!Main.Multiplayer.ProgressManager.CurrentlyUpdatingProgress)
		{
			string persistenID = __instance.GetPersistenID();
			ModLog.Info("Used CollectibleItem: " + persistenID);
			Main.Multiplayer.ProgressManager.UsePersistentObject(persistenID);
		}
	}
}

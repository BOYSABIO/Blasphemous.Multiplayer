using System;
using Blasphemous.Multiplayer.Common.Enums;
using Framework.Inventory;
using Framework.Managers;
using HarmonyLib;

namespace Blasphemous.Multiplayer.Client.ProgressSync.Helpers;

[HarmonyPatch(typeof(InventoryManager), "AddPrayer", new Type[] { typeof(Prayer) })]
internal class InventoryPrayer_Patch
{
	public static void Postfix(Prayer prayer)
	{
		if (!Main.Multiplayer.ProgressManager.CurrentlyUpdatingProgress)
		{
			ProgressUpdate progress = new ProgressUpdate(prayer.id, ProgressType.Prayer, 0);
			Main.Multiplayer.NetworkManager.SendProgress(progress);
		}
	}
}

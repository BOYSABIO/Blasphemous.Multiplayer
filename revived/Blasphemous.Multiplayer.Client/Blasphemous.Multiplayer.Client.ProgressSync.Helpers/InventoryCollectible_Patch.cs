using System;
using Blasphemous.Multiplayer.Common.Enums;
using Framework.Inventory;
using Framework.Managers;
using HarmonyLib;

namespace Blasphemous.Multiplayer.Client.ProgressSync.Helpers;

[HarmonyPatch(typeof(InventoryManager), "AddCollectibleItem", new Type[] { typeof(Framework.Inventory.CollectibleItem) })]
internal class InventoryCollectible_Patch
{
	public static void Postfix(Framework.Inventory.CollectibleItem collectibleItem)
	{
		if (!Main.Multiplayer.ProgressManager.CurrentlyUpdatingProgress)
		{
			ProgressUpdate progress = new ProgressUpdate(collectibleItem.id, ProgressType.Collectible, 0);
			Main.Multiplayer.NetworkManager.SendProgress(progress);
		}
	}
}

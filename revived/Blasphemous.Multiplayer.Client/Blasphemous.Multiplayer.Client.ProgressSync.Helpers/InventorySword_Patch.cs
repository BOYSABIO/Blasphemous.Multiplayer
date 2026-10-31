using System;
using Blasphemous.Multiplayer.Common.Enums;
using Framework.Inventory;
using Framework.Managers;
using HarmonyLib;

namespace Blasphemous.Multiplayer.Client.ProgressSync.Helpers;

[HarmonyPatch(typeof(InventoryManager), "AddSword", new Type[] { typeof(Sword) })]
internal class InventorySword_Patch
{
	public static void Postfix(Sword sword)
	{
		if (!Main.Multiplayer.ProgressManager.CurrentlyUpdatingProgress)
		{
			ProgressUpdate progress = new ProgressUpdate(sword.id, ProgressType.Heart, 0);
			Main.Multiplayer.NetworkManager.SendProgress(progress);
		}
	}
}

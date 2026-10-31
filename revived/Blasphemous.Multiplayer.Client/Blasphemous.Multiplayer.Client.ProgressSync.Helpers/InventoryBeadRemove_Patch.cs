using System;
using Blasphemous.Multiplayer.Common.Enums;
using Framework.Inventory;
using Framework.Managers;
using HarmonyLib;

namespace Blasphemous.Multiplayer.Client.ProgressSync.Helpers;

[HarmonyPatch(typeof(InventoryManager), "RemoveRosaryBead", new Type[] { typeof(RosaryBead) })]
internal class InventoryBeadRemove_Patch
{
	public static void Postfix(RosaryBead bead)
	{
		if (!Main.Multiplayer.ProgressManager.CurrentlyUpdatingProgress)
		{
			ProgressUpdate progress = new ProgressUpdate(bead.id, ProgressType.Bead, 1);
			Main.Multiplayer.NetworkManager.SendProgress(progress);
		}
	}
}

using System;
using Blasphemous.Multiplayer.Common.Enums;
using Framework.Inventory;
using Framework.Managers;
using HarmonyLib;

namespace Blasphemous.Multiplayer.Client.ProgressSync.Helpers;

[HarmonyPatch(typeof(InventoryManager), "AddRosaryBead", new Type[] { typeof(RosaryBead) })]
internal class InventoryBead_Patch
{
	public static void Postfix(RosaryBead rosaryBead)
	{
		if (!Main.Multiplayer.ProgressManager.CurrentlyUpdatingProgress)
		{
			ProgressUpdate progress = new ProgressUpdate(rosaryBead.id, ProgressType.Bead, 0);
			Main.Multiplayer.NetworkManager.SendProgress(progress);
		}
	}
}

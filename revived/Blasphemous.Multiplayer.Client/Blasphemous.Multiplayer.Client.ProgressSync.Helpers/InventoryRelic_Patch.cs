using System;
using Blasphemous.Multiplayer.Common.Enums;
using Framework.Inventory;
using Framework.Managers;
using HarmonyLib;

namespace Blasphemous.Multiplayer.Client.ProgressSync.Helpers;

[HarmonyPatch(typeof(InventoryManager), "AddRelic", new Type[] { typeof(Relic) })]
internal class InventoryRelic_Patch
{
	public static void Postfix(Relic relic)
	{
		if (!Main.Multiplayer.ProgressManager.CurrentlyUpdatingProgress)
		{
			ProgressUpdate progress = new ProgressUpdate(relic.id, ProgressType.Relic, 0);
			Main.Multiplayer.NetworkManager.SendProgress(progress);
		}
	}
}

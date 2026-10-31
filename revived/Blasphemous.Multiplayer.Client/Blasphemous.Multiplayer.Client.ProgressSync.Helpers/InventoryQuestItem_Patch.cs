using System;
using Blasphemous.Multiplayer.Common.Enums;
using Framework.Inventory;
using Framework.Managers;
using HarmonyLib;

namespace Blasphemous.Multiplayer.Client.ProgressSync.Helpers;

[HarmonyPatch(typeof(InventoryManager), "AddQuestItem", new Type[] { typeof(QuestItem) })]
internal class InventoryQuestItem_Patch
{
	public static void Postfix(QuestItem questItem)
	{
		if (!Main.Multiplayer.ProgressManager.CurrentlyUpdatingProgress && !(questItem.id == "QI76") && !(questItem.id == "QI77"))
		{
			ProgressUpdate progress = new ProgressUpdate(questItem.id, ProgressType.QuestItem, 0);
			Main.Multiplayer.NetworkManager.SendProgress(progress);
		}
	}
}

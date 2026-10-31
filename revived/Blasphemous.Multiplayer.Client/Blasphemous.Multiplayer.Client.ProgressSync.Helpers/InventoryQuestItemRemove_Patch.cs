using System;
using Blasphemous.Multiplayer.Common.Enums;
using Framework.Inventory;
using Framework.Managers;
using HarmonyLib;

namespace Blasphemous.Multiplayer.Client.ProgressSync.Helpers;

[HarmonyPatch(typeof(InventoryManager), "RemoveQuestItem", new Type[] { typeof(QuestItem) })]
internal class InventoryQuestItemRemove_Patch
{
	public static void Postfix(QuestItem questItem)
	{
		if (!Main.Multiplayer.ProgressManager.CurrentlyUpdatingProgress && !(questItem.id == "QI75") && !(questItem.id == "QI76") && !(questItem.id == "QI77"))
		{
			ProgressUpdate progress = new ProgressUpdate(questItem.id, ProgressType.QuestItem, 1);
			Main.Multiplayer.NetworkManager.SendProgress(progress);
		}
	}
}

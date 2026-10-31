using Blasphemous.Multiplayer.Common.Enums;
using Framework.Inventory;
using Framework.Managers;

namespace Blasphemous.Multiplayer.Client.ProgressSync.Helpers;

public class QuestItemHelper : IProgressHelper
{
	public void ApplyProgress(ProgressUpdate progress)
	{
		if (progress.Value == 1)
		{
			Core.InventoryManager.RemoveQuestItem(progress.Id);
		}
		else
		{
			Core.InventoryManager.AddQuestItem(progress.Id);
		}
	}

	public string GetProgressNotification(ProgressUpdate progress)
	{
		QuestItem questItem = Core.InventoryManager.GetQuestItem(progress.Id);
		if (progress.Value != 0 || !(questItem != null))
		{
			return null;
		}
		return Main.Multiplayer.LocalizationHandler.Localize("itmnot") + " " + questItem.caption;
	}

	public void SendAllProgress()
	{
		foreach (QuestItem item in Core.InventoryManager.GetQuestItemOwned())
		{
			ProgressUpdate progress = new ProgressUpdate(item.id, ProgressType.QuestItem, 0);
			Main.Multiplayer.NetworkManager.SendProgress(progress);
		}
	}
}

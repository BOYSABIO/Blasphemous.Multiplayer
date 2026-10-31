using Blasphemous.Multiplayer.Common.Enums;
using Framework.Inventory;
using Framework.Managers;

namespace Blasphemous.Multiplayer.Client.ProgressSync.Helpers;

public class CollectibleHelper : IProgressHelper
{
	public void ApplyProgress(ProgressUpdate progress)
	{
		Core.InventoryManager.AddCollectibleItem(progress.Id);
	}

	public string GetProgressNotification(ProgressUpdate progress)
	{
		Framework.Inventory.CollectibleItem collectibleItem = Core.InventoryManager.GetCollectibleItem(progress.Id);
		if (progress.Value != 0 || !(collectibleItem != null))
		{
			return null;
		}
		return Main.Multiplayer.LocalizationHandler.Localize("itmnot") + " " + collectibleItem.caption;
	}

	public void SendAllProgress()
	{
		foreach (Framework.Inventory.CollectibleItem item in Core.InventoryManager.GetCollectibleItemOwned())
		{
			ProgressUpdate progress = new ProgressUpdate(item.id, ProgressType.Collectible, 0);
			Main.Multiplayer.NetworkManager.SendProgress(progress);
		}
	}
}

using Blasphemous.Multiplayer.Common.Enums;
using Framework.Inventory;
using Framework.Managers;

namespace Blasphemous.Multiplayer.Client.ProgressSync.Helpers;

public class HeartHelper : IProgressHelper
{
	public void ApplyProgress(ProgressUpdate progress)
	{
		Core.InventoryManager.AddSword(progress.Id);
	}

	public string GetProgressNotification(ProgressUpdate progress)
	{
		Sword sword = Core.InventoryManager.GetSword(progress.Id);
		if (progress.Value != 0 || !(sword != null))
		{
			return null;
		}
		return Main.Multiplayer.LocalizationHandler.Localize("itmnot") + " " + sword.caption;
	}

	public void SendAllProgress()
	{
		foreach (Sword item in Core.InventoryManager.GetSwordsOwned())
		{
			ProgressUpdate progress = new ProgressUpdate(item.id, ProgressType.Heart, 0);
			Main.Multiplayer.NetworkManager.SendProgress(progress);
		}
	}
}

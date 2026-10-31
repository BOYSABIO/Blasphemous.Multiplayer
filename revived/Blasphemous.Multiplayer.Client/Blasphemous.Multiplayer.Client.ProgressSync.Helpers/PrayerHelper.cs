using Blasphemous.Multiplayer.Common.Enums;
using Framework.Inventory;
using Framework.Managers;

namespace Blasphemous.Multiplayer.Client.ProgressSync.Helpers;

public class PrayerHelper : IProgressHelper
{
	public void ApplyProgress(ProgressUpdate progress)
	{
		Core.InventoryManager.AddPrayer(progress.Id);
	}

	public string GetProgressNotification(ProgressUpdate progress)
	{
		Prayer prayer = Core.InventoryManager.GetPrayer(progress.Id);
		if (progress.Value != 0 || !(prayer != null))
		{
			return null;
		}
		return Main.Multiplayer.LocalizationHandler.Localize("itmnot") + " " + prayer.caption;
	}

	public void SendAllProgress()
	{
		foreach (Prayer item in Core.InventoryManager.GetPrayersOwned())
		{
			ProgressUpdate progress = new ProgressUpdate(item.id, ProgressType.Prayer, 0);
			Main.Multiplayer.NetworkManager.SendProgress(progress);
		}
	}
}

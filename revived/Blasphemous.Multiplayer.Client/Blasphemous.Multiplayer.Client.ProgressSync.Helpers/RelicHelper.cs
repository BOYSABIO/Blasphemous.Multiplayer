using Blasphemous.Multiplayer.Common.Enums;
using Framework.Inventory;
using Framework.Managers;

namespace Blasphemous.Multiplayer.Client.ProgressSync.Helpers;

public class RelicHelper : IProgressHelper
{
	public void ApplyProgress(ProgressUpdate progress)
	{
		Core.InventoryManager.AddRelic(progress.Id);
	}

	public string GetProgressNotification(ProgressUpdate progress)
	{
		Relic relic = Core.InventoryManager.GetRelic(progress.Id);
		if (progress.Value != 0 || !(relic != null))
		{
			return null;
		}
		return Main.Multiplayer.LocalizationHandler.Localize("itmnot") + " " + relic.caption;
	}

	public void SendAllProgress()
	{
		foreach (Relic item in Core.InventoryManager.GetRelicsOwned())
		{
			ProgressUpdate progress = new ProgressUpdate(item.id, ProgressType.Relic, 0);
			Main.Multiplayer.NetworkManager.SendProgress(progress);
		}
	}
}

using Blasphemous.Multiplayer.Common.Enums;
using Framework.Inventory;
using Framework.Managers;

namespace Blasphemous.Multiplayer.Client.ProgressSync.Helpers;

public class BeadHelper : IProgressHelper
{
	public void ApplyProgress(ProgressUpdate progress)
	{
		if (progress.Value == 1)
		{
			Core.InventoryManager.RemoveRosaryBead(progress.Id);
		}
		else
		{
			Core.InventoryManager.AddRosaryBead(progress.Id);
		}
	}

	public string GetProgressNotification(ProgressUpdate progress)
	{
		RosaryBead rosaryBead = Core.InventoryManager.GetRosaryBead(progress.Id);
		if (progress.Value != 0 || !(rosaryBead != null))
		{
			return null;
		}
		return Main.Multiplayer.LocalizationHandler.Localize("itmnot") + " " + rosaryBead.caption;
	}

	public void SendAllProgress()
	{
		foreach (RosaryBead item in Core.InventoryManager.GetRosaryBeadOwned())
		{
			ProgressUpdate progress = new ProgressUpdate(item.id, ProgressType.Bead, 0);
			Main.Multiplayer.NetworkManager.SendProgress(progress);
		}
	}
}

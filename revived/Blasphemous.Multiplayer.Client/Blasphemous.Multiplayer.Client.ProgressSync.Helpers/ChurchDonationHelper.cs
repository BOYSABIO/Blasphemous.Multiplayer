using Blasphemous.Multiplayer.Common.Enums;
using Framework.Managers;

namespace Blasphemous.Multiplayer.Client.ProgressSync.Helpers;

public class ChurchDonationHelper : IProgressHelper
{
	public void ApplyProgress(ProgressUpdate progress)
	{
		int tears = progress.Value * 1000;
		Core.Alms.DEBUG_SetTearsGiven(tears);
	}

	public string GetProgressNotification(ProgressUpdate progress)
	{
		return null;
	}

	public void SendAllProgress()
	{
		ProgressUpdate progress = new ProgressUpdate("Tears", ProgressType.ChurchDonation, (byte)(Core.Alms.TearsGiven / 1000));
		Main.Multiplayer.NetworkManager.SendProgress(progress);
	}
}

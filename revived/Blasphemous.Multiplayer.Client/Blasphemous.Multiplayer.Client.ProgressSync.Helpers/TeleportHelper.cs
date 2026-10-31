using Framework.Managers;

namespace Blasphemous.Multiplayer.Client.ProgressSync.Helpers;

public class TeleportHelper : IProgressHelper
{
	public void ApplyProgress(ProgressUpdate progress)
	{
		Core.SpawnManager.SetTeleportActive(progress.Id, active: true);
	}

	public string GetProgressNotification(ProgressUpdate progress)
	{
		return null;
	}

	public void SendAllProgress()
	{
		Core.SpawnManager.GetCurrentPersistentState("intro", fullSave: false);
	}
}

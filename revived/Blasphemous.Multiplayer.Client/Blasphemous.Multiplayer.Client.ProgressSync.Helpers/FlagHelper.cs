using Blasphemous.Multiplayer.Client.Data;
using Framework.Managers;

namespace Blasphemous.Multiplayer.Client.ProgressSync.Helpers;

public class FlagHelper : IProgressHelper
{
	public void ApplyProgress(ProgressUpdate progress)
	{
		Core.Events.SetFlag(progress.Id, progress.Value == 0);
	}

	public string GetProgressNotification(ProgressUpdate progress)
	{
		return FlagStates.getFlagState(progress.Id)?.notification;
	}

	public void SendAllProgress()
	{
		Core.Events.GetCurrentPersistentState("intro", fullSave: false);
	}
}

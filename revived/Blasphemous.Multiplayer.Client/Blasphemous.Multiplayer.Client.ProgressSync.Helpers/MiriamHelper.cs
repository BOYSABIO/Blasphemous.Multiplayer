using Blasphemous.Multiplayer.Common.Enums;
using Framework.Managers;

namespace Blasphemous.Multiplayer.Client.ProgressSync.Helpers;

public class MiriamHelper : IProgressHelper
{
	public void ApplyProgress(ProgressUpdate progress)
	{
		string id = progress.Id;
		switch (id)
		{
		case "START":
			Core.Events.StartMiriamQuest();
			break;
		case "FINISH":
			Core.Events.FinishMiriamQuest();
			break;
		case "D02Z03S24":
		case "D03Z03S19":
		case "D04Z04S02":
		case "D05Z01S24":
		case "D06Z01S26":
			Core.Events.SetCurrentPersistentState(null, isloading: false, id);
			break;
		}
	}

	public string GetProgressNotification(ProgressUpdate progress)
	{
		return null;
	}

	public void SendAllProgress()
	{
		if (Core.Events.IsMiriamQuestStarted)
		{
			ProgressUpdate progress = new ProgressUpdate("START", ProgressType.MiriamStatus, 0);
			Main.Multiplayer.NetworkManager.SendProgress(progress);
		}
		if (Core.Events.IsMiriamQuestFinished)
		{
			ProgressUpdate progress2 = new ProgressUpdate("FINISH", ProgressType.MiriamStatus, 0);
			Main.Multiplayer.NetworkManager.SendProgress(progress2);
		}
	}
}

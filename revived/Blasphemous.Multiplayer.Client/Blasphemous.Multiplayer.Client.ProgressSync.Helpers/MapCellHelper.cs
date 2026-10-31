using Framework.Managers;
using UnityEngine;

namespace Blasphemous.Multiplayer.Client.ProgressSync.Helpers;

public class MapCellHelper : IProgressHelper
{
	public void ApplyProgress(ProgressUpdate progress)
	{
		Core.NewMapManager.RevealCellInPosition(new Vector2(int.Parse(progress.Id), 0f));
	}

	public string GetProgressNotification(ProgressUpdate progress)
	{
		return null;
	}

	public void SendAllProgress()
	{
		Core.NewMapManager.GetCurrentPersistentState("intro", fullSave: false);
	}
}

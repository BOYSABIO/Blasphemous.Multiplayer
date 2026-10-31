using System;
using System.Collections.Generic;
using Blasphemous.ModdingAPI;
using Blasphemous.Multiplayer.Client.Data;
using Blasphemous.Multiplayer.Common.Enums;
using Framework.FrameworkCore;
using Framework.Managers;
using UnityEngine;

namespace Blasphemous.Multiplayer.Client.ProgressSync.Helpers;

public class PersistentObjectHelper : IProgressHelper
{
	public void ApplyProgress(ProgressUpdate progress)
	{
		Main.Multiplayer.ProgressManager.AddInteractedObject(progress.Id);
		string[] array = progress.Id.Split('~');
		string text = array[0];
		int sceneIdx = int.Parse(array[1]);
		string objectPersistentId = PersistentStates.getObjectPersistentId(text, sceneIdx);
		if (Core.LevelManager.currentLevel.LevelName != text || objectPersistentId == null)
		{
			return;
		}
		PersistentObject[] array2 = UnityEngine.Object.FindObjectsOfType<PersistentObject>();
		foreach (PersistentObject persistentObject in array2)
		{
			try
			{
				if (persistentObject.GetPersistenID() == objectPersistentId)
				{
					persistentObject.GetCurrentPersistentState("use", fullSave: false);
					break;
				}
			}
			catch (NullReferenceException)
			{
				ModLog.Error("Error: Failed to get persistent id of object");
			}
		}
	}

	public string GetProgressNotification(ProgressUpdate progress)
	{
		return null;
	}

	public void SendAllProgress()
	{
		List<string> allInteractedObjects = Main.Multiplayer.ProgressManager.GetAllInteractedObjects();
		for (int i = 0; i < allInteractedObjects.Count; i++)
		{
			ProgressUpdate progress = new ProgressUpdate(allInteractedObjects[i], ProgressType.PersistentObject, 0);
			Main.Multiplayer.NetworkManager.SendProgress(progress);
		}
	}
}

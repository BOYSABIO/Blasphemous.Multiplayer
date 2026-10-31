using System.Collections.Generic;
using Blasphemous.ModdingAPI;
using Blasphemous.Multiplayer.Client.Data;
using Blasphemous.Multiplayer.Client.ProgressSync.Helpers;
using Blasphemous.Multiplayer.Common.Enums;
using Framework.FrameworkCore;
using Framework.Managers;
using UnityEngine;

namespace Blasphemous.Multiplayer.Client.ProgressSync;

public class ProgressManager
{
	private bool _sentAllProgress;

	private readonly List<ProgressUpdate> progressQueue = new List<ProgressUpdate>();

	private List<string> interactedPersistenceObjects = new List<string>();

	private readonly Dictionary<ProgressType, IProgressHelper> progressHelpers = new Dictionary<ProgressType, IProgressHelper>
	{
		{
			ProgressType.Bead,
			new BeadHelper()
		},
		{
			ProgressType.Prayer,
			new PrayerHelper()
		},
		{
			ProgressType.Relic,
			new RelicHelper()
		},
		{
			ProgressType.Heart,
			new HeartHelper()
		},
		{
			ProgressType.Collectible,
			new CollectibleHelper()
		},
		{
			ProgressType.QuestItem,
			new QuestItemHelper()
		},
		{
			ProgressType.PlayerStat,
			new StatHelper()
		},
		{
			ProgressType.SwordSkill,
			new SwordSkillHelper()
		},
		{
			ProgressType.MapCell,
			new MapCellHelper()
		},
		{
			ProgressType.Flag,
			new FlagHelper()
		},
		{
			ProgressType.PersistentObject,
			new PersistentObjectHelper()
		},
		{
			ProgressType.Teleport,
			new TeleportHelper()
		},
		{
			ProgressType.ChurchDonation,
			new ChurchDonationHelper()
		},
		{
			ProgressType.MiriamStatus,
			new MiriamHelper()
		}
	};

	public bool CurrentlyUpdatingProgress { get; private set; }

	public List<string> SaveInteractedObjects()
	{
		return interactedPersistenceObjects;
	}

	public void LoadInteractedObjects(List<string> objects)
	{
		interactedPersistenceObjects = objects;
	}

	public void ClearInteractedObjects()
	{
		interactedPersistenceObjects.Clear();
	}

	public void ReceiveProgress(ProgressUpdate progress)
	{
		ModLog.Info("Received new game progress: " + progress.Id);
		if (progress.ShouldSyncProgress(Main.Multiplayer.config))
		{
			progressQueue.Add(progress);
		}
	}

	public void Update()
	{
		CurrentlyUpdatingProgress = true;
		for (int i = 0; i < progressQueue.Count; i++)
		{
			ApplyProgress(progressQueue[i]);
		}
		progressQueue.Clear();
		CurrentlyUpdatingProgress = false;
	}

	private void ApplyProgress(ProgressUpdate progress)
	{
		if (!progressHelpers.TryGetValue(progress.Type, out var value))
		{
			ModLog.Error("Error: Progress type doesn't exist: " + progress.Type);
		}
		else
		{
			value.ApplyProgress(progress);
		}
	}

	public string GetProgressNotification(ProgressUpdate progress)
	{
		if (!progressHelpers.TryGetValue(progress.Type, out var value))
		{
			ModLog.Error("Error: Progress type doesn't exist: " + progress.Type);
			return null;
		}
		return value.GetProgressNotification(progress);
	}

	public void UsePersistentObject(string persistentId)
	{
		string levelName = Core.LevelManager.currentLevel.LevelName;
		int objectSceneIndex = PersistentStates.getObjectSceneIndex(levelName, persistentId);
		string text = levelName + "~" + objectSceneIndex;
		if (objectSceneIndex >= 0 && !IsObjectInteracted(text))
		{
			AddInteractedObject(text);
			ProgressUpdate progress = new ProgressUpdate(text, ProgressType.PersistentObject, 0);
			Main.Multiplayer.NetworkManager.SendProgress(progress);
		}
	}

	public void SendAllProgress()
	{
		if (_sentAllProgress)
		{
			return;
		}
		_sentAllProgress = true;
		foreach (IProgressHelper value in progressHelpers.Values)
		{
			value.SendAllProgress();
		}
	}

	public void ResetProgressSentStatus()
	{
		_sentAllProgress = false;
		progressQueue.Clear();
	}

	public void LevelLoaded(string scene)
	{
		PersistentObject[] array = Object.FindObjectsOfType<PersistentObject>();
		foreach (PersistentObject persistentObject in array)
		{
			int objectSceneIndex = PersistentStates.getObjectSceneIndex(scene, persistentObject.GetPersistenID());
			string objectSceneId = scene + "~" + objectSceneIndex;
			if (objectSceneIndex >= 0 && IsObjectInteracted(objectSceneId))
			{
				persistentObject.SetCurrentPersistentState(null, isloading: false, null);
			}
		}
	}

	public bool IsObjectInteracted(string objectSceneId)
	{
		return interactedPersistenceObjects.Contains(objectSceneId);
	}

	public void AddInteractedObject(string objectSceneId)
	{
		interactedPersistenceObjects.Add(objectSceneId);
	}

	public List<string> GetAllInteractedObjects()
	{
		return interactedPersistenceObjects;
	}
}

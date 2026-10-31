using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Blasphemous.ModdingAPI;
using Framework.Managers;
using Tools.Level;
using Tools.Level.Interactables;
using UnityEngine;
using UnityEngine.UI;

namespace Blasphemous.Multiplayer.Client.Players;

public class OtherPlayerManager
{
	private readonly List<PlayerStatus> connectedPlayers = new List<PlayerStatus>();

	private readonly List<OtherPlayerScript> activePlayers = new List<OtherPlayerScript>();

	private readonly List<Text> nametags = new List<Text>();

	public ReadOnlyCollection<PlayerStatus> AllConnectedPlayers => connectedPlayers.AsReadOnly();

	public ReadOnlyCollection<OtherPlayerScript> AllActivePlayers => activePlayers.AsReadOnly();

	public ReadOnlyCollection<Text> AllNametags => nametags.AsReadOnly();

	public void LevelLoaded(string scene)
	{
		RemoveAllActivePlayers();
		foreach (PlayerStatus connectedPlayer in connectedPlayers)
		{
			if (connectedPlayer.CurrentScene == scene)
			{
				AddActivePlayer(connectedPlayer.Name);
			}
		}
		int num = 0;
		Interactable[] array = UnityEngine.Object.FindObjectsOfType<Interactable>();
		foreach (Interactable interactable in array)
		{
			Type type = interactable.GetType();
			if (type != typeof(PrieDieu) && type != typeof(CollectibleItem) && type != typeof(Chest) && type != typeof(Lever) && type != typeof(Door))
			{
				continue;
			}
			foreach (Transform item in interactable.transform)
			{
				if (item.name.ToLower().Contains("interactor"))
				{
					item.gameObject.AddComponent<SpecialAnimationChecker>();
					num++;
					break;
				}
			}
		}
		if (scene == "D17Z01S01")
		{
			GameObject gameObject = GameObject.Find("FakePenitent");
			if (gameObject != null)
			{
				gameObject.AddComponent<SpecialAnimationChecker>();
				num++;
			}
		}
		ModLog.Info("Adding special animation checkers to " + num + " objects!");
		if (Main.Multiplayer.NetworkManager.IsConnected)
		{
			AddNametag(Main.Multiplayer.PlayerName, friendlyTeam: true);
		}
	}

	public void Update()
	{
		foreach (PlayerStatus connectedPlayer in connectedPlayers)
		{
			switch (connectedPlayer.SkinUpdateStatus)
			{
			case PlayerStatus.SkinStatus.NoUpdate:
				connectedPlayer.SkinUpdateStatus = PlayerStatus.SkinStatus.YesUpdate;
				break;
			case PlayerStatus.SkinStatus.YesUpdate:
				ApplySkinTexture(connectedPlayer.Name);
				connectedPlayer.SkinUpdateStatus = PlayerStatus.SkinStatus.Updated;
				break;
			}
		}
		for (int i = 0; i < nametags.Count; i++)
		{
			RectTransform rectTransform = nametags[i].rectTransform;
			string name = nametags[i].name;
			Vector3 position;
			if (name == Main.Multiplayer.PlayerName)
			{
				position = Core.Logic.Penitent.transform.position;
			}
			else
			{
				OtherPlayerScript otherPlayerScript = FindActivePlayer(name);
				if (!(otherPlayerScript != null))
				{
					continue;
				}
				position = otherPlayerScript.transform.position;
			}
			Vector3 vector = Camera.main.WorldToViewportPoint(position + Vector3.up * 3.1f);
			rectTransform.anchorMin = vector;
			rectTransform.anchorMax = vector;
			rectTransform.anchoredPosition = Vector2.zero;
		}
	}

	private void ApplySkinTexture(string playerName)
	{
		OtherPlayerScript otherPlayerScript = FindActivePlayer(playerName);
		if (!(otherPlayerScript == null))
		{
			ModLog.Info("Setting skin texture for " + playerName);
			otherPlayerScript.ApplySkinTexture();
		}
	}

	public PlayerStatus FindConnectedPlayer(string name)
	{
		for (int i = 0; i < connectedPlayers.Count; i++)
		{
			if (connectedPlayers[i].Name == name)
			{
				return connectedPlayers[i];
			}
		}
		ModLog.Warn("Couldn't find connected player: " + name);
		return null;
	}

	public void AddConnectedPlayer(string name)
	{
		connectedPlayers.Add(new PlayerStatus(name));
	}

	public void RemoveConnectedPlayer(string name)
	{
		PlayerStatus playerStatus = FindConnectedPlayer(name);
		if (playerStatus != null)
		{
			connectedPlayers.Remove(playerStatus);
		}
	}

	public void RemoveAllConnectedPlayers()
	{
		connectedPlayers.Clear();
	}

	public OtherPlayerScript FindActivePlayer(string name)
	{
		for (int i = 0; i < activePlayers.Count; i++)
		{
			if (activePlayers[i].name == "_" + name)
			{
				return activePlayers[i];
			}
		}
		ModLog.Warn("Couldn't find active player: " + name);
		return null;
	}

	public void AddActivePlayer(string name)
	{
		PlayerStatus playerStatus = FindConnectedPlayer(name);
		if (playerStatus != null)
		{
			GameObject gameObject = new GameObject("_" + name);
			OtherPlayerScript otherPlayerScript = gameObject.AddComponent<OtherPlayerScript>();
			otherPlayerScript.SetupPlayer(playerStatus);
			activePlayers.Add(otherPlayerScript);
			if (Core.LevelManager.currentLevel.LevelName == "D17Z01S01")
			{
				gameObject.AddComponent<FakePenitentIntro>();
			}
			AddNametag(name, Main.Multiplayer.PlayerTeam == playerStatus.Team);
			ModLog.Info("Created new player object for " + name);
		}
	}

	public void RemoveActivePlayer(string name)
	{
		OtherPlayerScript otherPlayerScript = FindActivePlayer(name);
		if (otherPlayerScript != null)
		{
			activePlayers.Remove(otherPlayerScript);
			UnityEngine.Object.Destroy(otherPlayerScript.gameObject);
			ModLog.Info("Removed player object for " + name);
		}
		RemoveNametag(name);
	}

	public void RemoveAllActivePlayers()
	{
		for (int i = 0; i < activePlayers.Count; i++)
		{
			if (activePlayers[i] != null)
			{
				UnityEngine.Object.Destroy(activePlayers[i].gameObject);
			}
		}
		activePlayers.Clear();
		RemoveAllNametags();
	}

	public Text FindNametag(string name)
	{
		for (int i = 0; i < nametags.Count; i++)
		{
			if (nametags[i].name == name)
			{
				return nametags[i];
			}
		}
		ModLog.Warn("Couldn't find nametag: " + name);
		return null;
	}

	public void AddNametag(string name, bool friendlyTeam)
	{
		if (Main.Multiplayer.config.displayNametags && (Main.Multiplayer.config.displayOwnNametag || !(name == Main.Multiplayer.PlayerName)))
		{
			Transform canvasObject = UnityReferences.CanvasObject;
			GameObject textObject = UnityReferences.TextObject;
			if (canvasObject == null || textObject == null)
			{
				ModLog.Error("Error: Failed to create nametag for " + name);
				return;
			}
			Text component = UnityEngine.Object.Instantiate(textObject, canvasObject).GetComponent<Text>();
			component.rectTransform.sizeDelta = new Vector2(100f, 50f);
			component.rectTransform.SetAsFirstSibling();
			component.name = name;
			component.text = name;
			component.alignment = TextAnchor.LowerCenter;
			component.color = (friendlyTeam ? new Color(0.671f, 0.604f, 0.247f) : Color.red);
			nametags.Add(component);
		}
	}

	public void RemoveNametag(string name)
	{
		Text text = FindNametag(name);
		if (text != null)
		{
			nametags.Remove(text);
			UnityEngine.Object.Destroy(text.gameObject);
			ModLog.Info("Removed nametag for " + name);
		}
	}

	public void RemoveAllNametags()
	{
		for (int i = 0; i < nametags.Count; i++)
		{
			if (nametags[i] != null)
			{
				UnityEngine.Object.Destroy(nametags[i].gameObject);
			}
		}
		nametags.Clear();
	}

	public void RefreshNametagColors()
	{
		for (int i = 0; i < nametags.Count; i++)
		{
			string name = nametags[i].name;
			bool flag;
			if (name == Main.Multiplayer.PlayerName)
			{
				flag = true;
			}
			else
			{
				PlayerStatus playerStatus = FindConnectedPlayer(name);
				if (playerStatus == null)
				{
					continue;
				}
				flag = Main.Multiplayer.PlayerTeam == playerStatus.Team;
			}
			nametags[i].GetComponent<Text>().color = (flag ? new Color(0.671f, 0.604f, 0.247f) : Color.red);
		}
	}

	public void ReceivePosition(string playerName, Vector2 position)
	{
		if (Main.Multiplayer.CurrentlyInLevel)
		{
			OtherPlayerScript otherPlayerScript = FindActivePlayer(playerName);
			if (otherPlayerScript != null)
			{
				otherPlayerScript.CurrentPosition = position;
			}
		}
	}

	public void ReceiveAnimation(string playerName, byte animation)
	{
		if (Main.Multiplayer.CurrentlyInLevel)
		{
			OtherPlayerScript otherPlayerScript = FindActivePlayer(playerName);
			if (otherPlayerScript != null)
			{
				otherPlayerScript.CurrentAnimation = animation;
			}
		}
	}

	public void ReceiveDirection(string playerName, bool direction)
	{
		if (Main.Multiplayer.CurrentlyInLevel)
		{
			OtherPlayerScript otherPlayerScript = FindActivePlayer(playerName);
			if (otherPlayerScript != null)
			{
				otherPlayerScript.CurrentDirection = direction;
			}
		}
	}

	public void ReceiveEnterScene(string playerName, string scene)
	{
		PlayerStatus playerStatus = FindConnectedPlayer(playerName);
		if (playerStatus != null)
		{
			playerStatus.CurrentScene = scene;
			if (Core.LevelManager.currentLevel.LevelName == scene)
			{
				AddActivePlayer(playerName);
				Main.Multiplayer.EnemyManager.OnPlayersInSceneChanged();
			}
			Main.Multiplayer.MapManager.QueueMapUpdate();
		}
	}

	public void ReceiveLeaveScene(string playerName)
	{
		PlayerStatus playerStatus = FindConnectedPlayer(playerName);
		if (playerStatus != null)
		{
			if (Core.LevelManager.currentLevel.LevelName == playerStatus.CurrentScene)
			{
				RemoveActivePlayer(playerName);
				Main.Multiplayer.EnemyManager.OnPlayersInSceneChanged();
			}
			playerStatus.CurrentScene = string.Empty;
			Main.Multiplayer.MapManager.QueueMapUpdate();
		}
	}

	public void ReceiveTeam(string playerName, byte team)
	{
		PlayerStatus playerStatus = FindConnectedPlayer(playerName);
		if (playerStatus != null)
		{
			playerStatus.Team = team;
			if (Main.Multiplayer.CurrentlyInLevel)
			{
				Main.Multiplayer.RefreshPlayerColors();
			}
		}
	}

	public void ReceivePing(string playerName, ushort ping)
	{
		PlayerStatus playerStatus = FindConnectedPlayer(playerName);
		if (playerStatus != null)
		{
			playerStatus.Ping = ping;
		}
	}
}

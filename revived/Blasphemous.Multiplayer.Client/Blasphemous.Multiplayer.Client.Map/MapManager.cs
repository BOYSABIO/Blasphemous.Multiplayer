using Blasphemous.ModdingAPI;
using Blasphemous.Multiplayer.Client.Players;
using Framework.Managers;
using Framework.Map;
using Gameplay.UI.Others.MenuLogic;
using UnityEngine;
using UnityEngine.UI;

namespace Blasphemous.Multiplayer.Client.Map;

public class MapManager
{
	private bool mapUpdateQueued;

	private Transform playerMarks;

	private Sprite[] playerSprites;

	public Vector2 ActivePlayerPosition { get; set; }

	public void QueueMapUpdate()
	{
		mapUpdateQueued = true;
	}

	public void Update()
	{
		if (mapUpdateQueued && Core.Logic.IsPaused)
		{
			RefreshMap(forceRecalculate: false);
		}
		mapUpdateQueued = false;
	}

	public void RefreshMap(bool forceRecalculate)
	{
		if (!Main.Multiplayer.config.showPlayersOnMap)
		{
			return;
		}
		ModLog.Info("Updating map with new player marks!");
		if (forceRecalculate && playerMarks != null)
		{
			Object.Destroy(playerMarks.gameObject);
			playerMarks = null;
		}
		if (playerMarks == null || playerSprites == null)
		{
			CreateMapUI();
		}
		for (int num = playerMarks.childCount - 1; num >= 0; num--)
		{
			Object.Destroy(playerMarks.GetChild(num).gameObject);
		}
		foreach (PlayerStatus allConnectedPlayer in Main.Multiplayer.OtherPlayerManager.AllConnectedPlayers)
		{
			if (allConnectedPlayer.Team != Main.Multiplayer.PlayerTeam && !Main.Multiplayer.config.showOtherTeamOnMap)
			{
				continue;
			}
			Core.NewMapManager.GetCellKeyFromPosition(allConnectedPlayer.LastMapScene, new Vector2(-1000f, 0f));
			Vector2 activePlayerPosition = ActivePlayerPosition;
			if (!(activePlayerPosition.x < 0f) && !(activePlayerPosition.y < 0f))
			{
				Sprite sprite = playerSprites[0];
				if (allConnectedPlayer.CurrentScene != allConnectedPlayer.LastMapScene)
				{
					sprite = playerSprites[1];
				}
				else if (allConnectedPlayer.Team != Main.Multiplayer.PlayerTeam)
				{
					sprite = playerSprites[2];
				}
				RectTransform component = new GameObject(allConnectedPlayer.Name, typeof(RectTransform)).GetComponent<RectTransform>();
				component.SetParent(playerMarks, worldPositionStays: false);
				component.localRotation = Quaternion.identity;
				component.localScale = Vector3.one;
				component.localPosition = new Vector2(16f * activePlayerPosition.x, 16f * activePlayerPosition.y);
				component.sizeDelta = new Vector2(playerSprites[0].rect.width, playerSprites[0].rect.height);
				component.gameObject.AddComponent<Image>().sprite = sprite;
				ModLog.Info("Creating mark at " + component.localPosition.ToString());
			}
		}
	}

	private void CreateMapUI()
	{
		NewMapMenuWidget newMapMenuWidget = Object.FindObjectOfType<NewMapMenuWidget>();
		if (!(newMapMenuWidget == null))
		{
			Transform transform = newMapMenuWidget.transform.Find("Background/Map/MapMask/MapRoot/RootRenderer_0");
			if (!(transform == null))
			{
				playerMarks = new GameObject("PlayerMarks", typeof(RectTransform)).transform;
				playerMarks.SetParent(transform, worldPositionStays: false);
				MapRendererConfig mapRendererConfig = newMapMenuWidget.RendererConfigs[0];
				playerSprites = new Sprite[3]
				{
					mapRendererConfig.Marks[MapData.MarkType.Blue],
					mapRendererConfig.Marks[MapData.MarkType.Green],
					mapRendererConfig.Marks[MapData.MarkType.Red]
				};
			}
		}
	}
}

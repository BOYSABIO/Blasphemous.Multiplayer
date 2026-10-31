using System.Linq;
using Blasphemous.ModdingAPI.Helpers;
using Blasphemous.Multiplayer.Client.Players;
using UnityEngine;

namespace Blasphemous.Multiplayer.Client.Ping;

public class PlayerDisplay : MonoBehaviour
{
	private Rect _playerWindow = new Rect(Screen.width - 250 - 10, 200f, 250f, 10f);

	private static readonly PingRegion[] _regions = new PingRegion[4]
	{
		new PingRegion(100, "109748"),
		new PingRegion(500, "FFE733"),
		new PingRegion(1000, "FF8C01"),
		new PingRegion(int.MaxValue, "ED2938")
	};

	private const int WINDOW_WIDTH = 250;

	private const int LINE_HEIGHT = 25;

	private const int LINE_GAP = 5;

	private void OnGUI()
	{
		if (SceneHelper.GameSceneLoaded && Main.Multiplayer.NetworkManager.IsConnected)
		{
			_playerWindow = GUI.Window(3, _playerWindow, PlayerWindow, "Connected Players");
		}
	}

	private void PlayerWindow(int windowID)
	{
		GUIStyle style = new GUIStyle(GUI.skin.label)
		{
			alignment = TextAnchor.MiddleCenter
		};
		GUIStyle style2 = new GUIStyle(GUI.skin.label)
		{
			alignment = TextAnchor.MiddleRight,
			richText = true
		};
		int num = 20;
		foreach (IGrouping<byte, PlayerDisplayInfo> item in from x in Main.Multiplayer.OtherPlayerManager.AllConnectedPlayers.Select((PlayerStatus x) => new PlayerDisplayInfo(x.Name, x.Team, x.Ping)).Concat(new[] { new PlayerDisplayInfo(Main.Multiplayer.PlayerName, Main.Multiplayer.PlayerTeam, Main.Multiplayer.PingManager.AveragePing) })
			orderby x.Name
			group x by x.Team)
		{
			GUI.Label(new Rect(0f, num, 250f, 25f), $"Team {item.Key}", style);
			num += 30;
			GUI.Box(new Rect(0f, num, 250f, item.Count() * 30 + 5), string.Empty);
			num += 5;
			foreach (PlayerDisplayInfo info in item)
			{
				string color = _regions.OrderBy((PingRegion x) => x.MaxPing).First((PingRegion x) => info.Ping <= x.MaxPing).Color;
				string text = info.Name;
				string text2 = $"<color=#{color}>{info.Ping}ms</color>";
				GUI.Label(new Rect(10f, num, 230f, 25f), text2, style2);
				GUI.Label(new Rect(10f, num, 230f, 25f), text);
				num += 30;
			}
		}
		_playerWindow.height = num;
	}
}


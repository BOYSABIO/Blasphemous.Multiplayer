using System.Linq;
using Blasphemous.ModdingAPI.Helpers;
using UnityEngine;

namespace Blasphemous.Multiplayer.Client.Ping;

public class PingDisplay : MonoBehaviour
{
	private static readonly PingRegion[] _regions = new PingRegion[4]
	{
		new PingRegion(50, "109748"),
		new PingRegion(100, "FFE733"),
		new PingRegion(500, "FF8C01"),
		new PingRegion(int.MaxValue, "ED2938")
	};

	private void OnGUI()
	{
		if (SceneHelper.GameSceneLoaded && Main.Multiplayer.NetworkManager.IsConnected)
		{
			GUI.Window(2, new Rect(1810f, 970f, 100f, 100f), PingWindow, "Test Window");
		}
	}

	private void PingWindow(int windowID)
	{
		ushort ping = Main.Multiplayer.PingManager.AveragePing;
		string color = _regions.OrderBy((PingRegion x) => x.MaxPing).First((PingRegion x) => ping <= x.MaxPing).Color;
		string text = $"Ping: <color=#{color}>{Main.Multiplayer.PingManager.AveragePing}ms</color>";
		GUIStyle style = new GUIStyle(GUI.skin.label)
		{
			richText = true
		};
		GUI.Label(new Rect(10f, 20f, 80f, 30f), text, style);
	}
}

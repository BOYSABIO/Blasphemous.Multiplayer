using System.Collections;
using System.Linq;
using Blasphemous.ModdingAPI;
using Blasphemous.ModdingAPI.Helpers;
using Blasphemous.Multiplayer.Client.InputValidation;
using Blasphemous.Multiplayer.Common.Enums;
using Framework.Managers;
using UnityEngine;

namespace Blasphemous.Multiplayer.Client.Network;

public class ConnectionDisplay : MonoBehaviour
{
	private readonly IValidator _validator;

	private readonly ISanitizer _sanitizer;

	private bool _attemptingConnection;

	private bool _firstShowing = true;

	private ConnectionInfo _connection = new ConnectionInfo();

	private Rect _window;

	private bool _open;

	private const int START_X = 10;

	private const int START_Y = 20;

	private const int GAP = 10;

	private const int LINE_HEIGHT = 25;

	private const int LABEL_WIDTH = 100;

	private const int TEXT_WIDTH = 200;

	private const int BUTTON_WIDTH = 90;

	private const int WIDTH = 330;

	private const int HEIGHT = 400;

	public ConnectionDisplay()
	{
		_sanitizer = (ISanitizer)(_validator = new StandardValidator());
	}

	private void SetOpenStatus(bool open)
	{
		if (_open != open)
		{
			_open = open;
			Core.Input.SetBlocker("MULTIPLAYER", _open);
		}
	}

	private void OnGUI()
	{
		if (!SceneHelper.GameSceneLoaded)
		{
			Cursor.visible = false;
			SetOpenStatus(open: false);
			return;
		}
		Cursor.visible = true;
		Event current = Event.current;
		if (current.type == EventType.MouseDown && current.button == 0)
		{
			SetOpenStatus(_window.Contains(current.mousePosition));
		}
		int num = Screen.height - (_open ? 400 : 17);
		_window = GUI.Window(99, new Rect(240f, num, 330f, 420f), MultiplayerWindow, "Multiplayer connection info");
	}

	private void MultiplayerWindow(int windowID)
	{
		if (Main.Multiplayer.NetworkManager.IsConnected)
		{
			DisplayEnd();
		}
		else if (_attemptingConnection)
		{
			DisplayMiddle();
		}
		else
		{
			DisplayStart();
		}
	}

	private void DisplayStart()
	{
		if (_firstShowing)
		{
			_connection = Main.Multiplayer.LastConnectionInfo;
		}
		string text = ReadTextField("Server IP:", _connection.ServerIp, 0);
		if (_firstShowing || text != _connection.ServerIp)
		{
			text = _sanitizer.CleanServer(text);
		}
		string roomName = "a";
		string text2 = ReadTextField("Player name:", _connection.PlayerName, 2);
		if (_firstShowing || text2 != _connection.PlayerName)
		{
			text2 = _sanitizer.CleanPlayer(text2);
		}
		string text3 = ReadTextField("Password:", _connection.Password, 3);
		if (_firstShowing || text3 != _connection.Password)
		{
			text3 = _sanitizer.CleanPassword(text3);
		}
		int num = ReadChoiceBox("Team number:", _connection.TeamNumber - 1, (from x in Enumerable.Range(1, 8)
			select x.ToString()).ToArray(), 4) + 1;
		if (_firstShowing || num != _connection.TeamNumber)
		{
			num = _sanitizer.CleanTeam(num);
		}
		_connection = new ConnectionInfo(text, roomName, text2, text3, (byte)num);
		_firstShowing = false;
		if (ReadButton("Connect", 5))
		{
			ValidateAndConnect();
		}
	}

	private void DisplayMiddle()
	{
		GUIStyle style = new GUIStyle(GUI.skin.label)
		{
			alignment = TextAnchor.MiddleCenter,
			fontStyle = FontStyle.Bold,
			fontSize = 20
		};
		GUI.Label(new Rect(0f, 20f, 330f, 380f), "Connnecting...", style);
	}

	private void DisplayEnd()
	{
		string label = $"You are connected to {_connection.ServerIp} ({_connection.RoomName}) as {_connection.PlayerName} on team {_connection.TeamNumber}";
		ShowLabel(label, 0);
		if (ReadButton("Disconnect", 2))
		{
			TryDisconnect();
		}
	}

	private void ValidateAndConnect()
	{
		bool flag = true;
		if (!_validator.IsServerValid(_connection.ServerIp))
		{
			Main.Multiplayer.NotificationManager.DisplayNotification("Input error: Server IP is invalid");
			flag = false;
		}
		if (!_validator.IsRoomValid(_connection.RoomName))
		{
			Main.Multiplayer.NotificationManager.DisplayNotification("Input error: Room name is invalid");
			flag = false;
		}
		if (!_validator.IsPlayerValid(_connection.PlayerName))
		{
			Main.Multiplayer.NotificationManager.DisplayNotification("Input error: Player name is invalid");
			flag = false;
		}
		if (!_validator.IsPasswordValid(_connection.Password))
		{
			Main.Multiplayer.NotificationManager.DisplayNotification("Input error: Password is invalid");
			flag = false;
		}
		if (!_validator.IsTeamValid(_connection.TeamNumber))
		{
			Main.Multiplayer.NotificationManager.DisplayNotification("Input error: Team number is invalid");
			flag = false;
		}
		if (flag)
		{
			StartCoroutine(TryConnect());
		}
	}

	private IEnumerator TryConnect()
	{
		ModLog.Info("Attempting to connect to " + _connection.ServerIp);
		string server;
		int port;
		if (_connection.ServerIp.Contains(':'))
		{
			string[] array = _connection.ServerIp.Split(':');
			server = array[0];
			port = int.Parse(array[1]);
		}
		else
		{
			server = _connection.ServerIp;
			port = 27051;
		}
		_attemptingConnection = true;
		Main.Multiplayer.NetworkManager.OnConnect += OnConnect;
		yield return null;
		Main.Multiplayer.NetworkManager.Connect(server, port, _connection.RoomName, _connection.PlayerName, _connection.Password, _connection.TeamNumber);
	}

	private void OnConnect(bool success, RefusalType refusal)
	{
		_attemptingConnection = false;
		Main.Multiplayer.LastConnectionInfo = _connection;
		Main.Multiplayer.NetworkManager.OnConnect -= OnConnect;
	}

	private void TryDisconnect()
	{
		ModLog.Info("Disconnecting from " + _connection.ServerIp);
		Main.Multiplayer.NetworkManager.Disconnect();
	}

	private void ShowLabel(string label, int line)
	{
		GUI.Label(new Rect(10f, 20 + 35 * line, 310f, 60f), label);
	}

	private string ReadTextField(string label, string value, int line)
	{
		GUI.Label(new Rect(10f, 20 + 35 * line, 100f, 25f), label);
		return GUI.TextField(new Rect(120f, 20 + 35 * line, 200f, 25f), value);
	}

	private int ReadChoiceBox(string label, int value, string[] choices, int line)
	{
		GUI.Label(new Rect(10f, 20 + 35 * line, 100f, 25f), label);
		return GUI.Toolbar(new Rect(120f, 20 + 35 * line, 200f, 25f), value, choices);
	}

	private bool ReadButton(string label, int line)
	{
		return GUI.Button(new Rect(10f, 20 + 35 * line, 90f, 25f), label);
	}
}

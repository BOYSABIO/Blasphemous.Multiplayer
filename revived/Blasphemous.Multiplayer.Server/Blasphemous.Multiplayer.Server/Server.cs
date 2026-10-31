using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using Blasphemous.Multiplayer.Common.Enums;
using Blasphemous.Multiplayer.Common.Networking;
using Blasphemous.Multiplayer.Server.Models;
using SimpleTCP;

namespace Blasphemous.Multiplayer.Server;

public class Server
{
	private struct SceneHostState
	{
		public string HostName;

		public uint Term;
	}

	private struct SceneDeathRecord
	{
		public int NetId;

		public byte[] Body;
	}

	private readonly int _maxPlayers;

	private readonly string _password;

	private SimpleTcpServer server;

	private Dictionary<string, PlayerInfo> connectedPlayers;

	private string _sessionEnemyHostName = string.Empty;

	private readonly Dictionary<string, List<byte>> _receiveBuffers = new Dictionary<string, List<byte>>();

	private readonly Dictionary<string, SceneHostState> _sceneHosts = new Dictionary<string, SceneHostState>();

	private readonly Dictionary<string, List<SceneDeathRecord>> _sceneDeaths = new Dictionary<string, List<SceneDeathRecord>>();

	public Server(int P_0, string P_1)
	{
		_maxPlayers = P_0;
		_password = P_1;
	}

	public Dictionary<string, PlayerInfo> getPlayers()
	{
		return connectedPlayers;
	}

	public bool Start(int P_0)
	{
		try
		{
			server = new SimpleTcpServer();
			server.ClientConnected += clientConnected;
			server.ClientDisconnected += clientDisconnected;
			server.DataReceived += Receive;
			server.Start(P_0);
			server.DelayDisabled = true;
		}
		catch (SocketException)
		{
			return false;
		}
		connectedPlayers = new Dictionary<string, PlayerInfo>();
		_sessionEnemyHostName = string.Empty;
		_receiveBuffers.Clear();
		_sceneHosts.Clear();
		_sceneDeaths.Clear();
		return true;
	}

	private void Send(string P_0, byte[] P_1, NetworkType P_2)
	{
		if (P_1 != null && P_1.Length != 0)
		{
			try
			{
				server.Send(P_0, FrameCodec.Wrap((byte)P_2, P_1));
			}
			catch (Exception)
			{
				Logger.Error("Couldn't send data to " + P_0 + "!");
			}
		}
	}

	private void Receive(object sender, DataReceivedEventArgs e)
	{
		if (!_receiveBuffers.TryGetValue(e.ip, out var list))
		{
			list = new List<byte>();
			_receiveBuffers[e.ip] = list;
		}
		foreach (FrameCodec.Frame item in FrameCodec.Drain(e.data, list, delegate(string msg)
		{
			Logger.Error(msg);
		}))
		{
			processDataReceived((NetworkType)item.Type, item.Payload);
		}
		void processDataReceived(NetworkType P_0, byte[] P_1)
		{
			switch (P_0)
			{
			case NetworkType.Position:
				ReceivePosition(e.ip, P_1);
				break;
			case NetworkType.Animation:
				ReceiveAnimation(e.ip, P_1);
				break;
			case NetworkType.Direction:
				ReceiveDirection(e.ip, P_1);
				break;
			case NetworkType.EnterScene:
				receivePlayerEnterScene(e.ip, P_1);
				break;
			case NetworkType.LeaveScene:
				receivePlayerLeaveScene(e.ip, P_1);
				break;
			case NetworkType.Skin:
				receivePlayerSkin(e.ip, P_1);
				break;
			case NetworkType.Team:
				receivePlayerTeam(e.ip, P_1);
				break;
			case NetworkType.Intro:
				receivePlayerIntro(e.ip, P_1);
				break;
			case NetworkType.Progress:
				receivePlayerProgress(e.ip, P_1);
				break;
			case NetworkType.Attack:
				receivePlayerAttack(e.ip, P_1);
				break;
			case NetworkType.Effect:
				receivePlayerEffect(e.ip, P_1);
				break;
			case NetworkType.Ping:
				ReceivePing(e.ip, P_1);
				break;
			case NetworkType.EnemySnapshot:
				ReceiveEnemyAuthorityRelay(e.ip, P_1, NetworkType.EnemySnapshot);
				break;
			case NetworkType.EnemyState:
				ReceiveEnemyAuthorityRelay(e.ip, P_1, NetworkType.EnemyState);
				break;
			case NetworkType.EnemyDamage:
				ReceiveEnemyDamageRelay(e.ip, P_1);
				break;
			case NetworkType.EnemyDeath:
				ReceiveEnemyDeathRelay(e.ip, P_1);
				break;
			case NetworkType.EnemySyncRequest:
				ReceiveEnemySyncRequestRelay(e.ip, P_1);
				break;
			case NetworkType.EnemyPlayerHit:
				ReceiveEnemyAuthorityRelay(e.ip, P_1, NetworkType.EnemyPlayerHit);
				break;
			case NetworkType.EnemyParried:
				ReceiveEnemyParriedRelay(e.ip, P_1);
				break;
			case NetworkType.EnemySyncDone:
				ReceiveEnemyAuthorityRelay(e.ip, P_1, NetworkType.EnemySyncDone);
				break;
			case NetworkType.EnemyExecuted:
				ReceiveEnemyExecutedRelay(e.ip, P_1);
				break;
			case NetworkType.PlayerExecutionAnim:
				ReceivePlayerExecutionAnimRelay(e.ip, P_1);
				break;
			default:
				Logger.Error($"Data type '{P_0}' is not valid");
				break;
			case NetworkType.SessionEnemyHost:
			case NetworkType.SceneEnemyHost:
				break;
			}
		}
	}

	private void clientConnected(object sender, ClientConnectionEventArgs e)
	{
		Logger.Info("Client connected at " + e.ip);
	}

	private void clientDisconnected(object sender, ClientConnectionEventArgs e)
	{
		Logger.Info("Client disconnected at " + e.ip);
		if (!connectedPlayers.ContainsKey(e.ip))
		{
			_receiveBuffers.Remove(e.ip);
			return;
		}
		string name = connectedPlayers[e.ip].name;
		string room = connectedPlayers[e.ip].room;
		string sceneName = connectedPlayers[e.ip].sceneName;
		sendPlayerConnection(e.ip, false);
		connectedPlayers.Remove(e.ip);
		_receiveBuffers.Remove(e.ip);
		Core.removeUnusedGameData(connectedPlayers);
		if (name == _sessionEnemyHostName)
		{
			_sessionEnemyHostName = ((connectedPlayers.Count > 0) ? connectedPlayers.Values.First().name : string.Empty);
			BroadcastSessionEnemyHost();
			Logger.Info("Session enemy host left → now '" + _sessionEnemyHostName + "'");
		}
		RecalculateSceneHost(room, sceneName);
	}

	private PlayerInfo getCurrentPlayer(string P_0)
	{
		if (connectedPlayers.ContainsKey(P_0))
		{
			return connectedPlayers[P_0];
		}
		Logger.Warn("Data for " + P_0 + " has not been created yet!");
		return new PlayerInfo(string.Empty, 1);
	}

	private List<byte> addPlayerNameToData(string P_0)
	{
		byte[] bytes = Encoding.UTF8.GetBytes(P_0);
		List<byte> list = new List<byte>();
		list.Add((byte)bytes.Length);
		list.AddRange(bytes);
		return list;
	}

	private void SendPosition(string P_0)
	{
		PlayerInfo currentPlayer = getCurrentPlayer(P_0);
		foreach (string key in connectedPlayers.Keys)
		{
			if (P_0 != key && currentPlayer.isInSameScene(connectedPlayers[key]))
			{
				Send(key, GetPositionPacket(currentPlayer), NetworkType.Position);
			}
		}
	}

	private void ReceivePosition(string P_0, byte[] P_1)
	{
		PlayerInfo currentPlayer = getCurrentPlayer(P_0);
		currentPlayer.xPos = BitConverter.ToSingle(P_1, 0);
		currentPlayer.yPos = BitConverter.ToSingle(P_1, 4);
		SendPosition(P_0);
	}

	private byte[] GetPositionPacket(PlayerInfo P_0)
	{
		List<byte> list = addPlayerNameToData(P_0.name);
		list.AddRange(BitConverter.GetBytes(P_0.xPos));
		list.AddRange(BitConverter.GetBytes(P_0.yPos));
		return list.ToArray();
	}

	private void SendAnimation(string P_0)
	{
		PlayerInfo currentPlayer = getCurrentPlayer(P_0);
		foreach (string key in connectedPlayers.Keys)
		{
			if (P_0 != key && currentPlayer.isInSameScene(connectedPlayers[key]))
			{
				Send(key, GetAnimationPacket(currentPlayer), NetworkType.Animation);
			}
		}
	}

	private void ReceiveAnimation(string P_0, byte[] P_1)
	{
		getCurrentPlayer(P_0).animation = P_1[0];
		SendAnimation(P_0);
	}

	private byte[] GetAnimationPacket(PlayerInfo P_0)
	{
		List<byte> list = addPlayerNameToData(P_0.name);
		list.Add(P_0.animation);
		return list.ToArray();
	}

	private void SendDirection(string P_0)
	{
		PlayerInfo currentPlayer = getCurrentPlayer(P_0);
		foreach (string key in connectedPlayers.Keys)
		{
			if (P_0 != key && currentPlayer.isInSameScene(connectedPlayers[key]))
			{
				Send(key, GetDirectionPacket(currentPlayer), NetworkType.Direction);
			}
		}
	}

	private void ReceiveDirection(string P_0, byte[] P_1)
	{
		getCurrentPlayer(P_0).facingDirection = BitConverter.ToBoolean(P_1);
		SendDirection(P_0);
	}

	private byte[] GetDirectionPacket(PlayerInfo P_0)
	{
		List<byte> list = addPlayerNameToData(P_0.name);
		list.AddRange(BitConverter.GetBytes(P_0.facingDirection));
		return list.ToArray();
	}

	private void sendPlayerEnterScene(string P_0)
	{
		PlayerInfo currentPlayer = getCurrentPlayer(P_0);
		foreach (string key in connectedPlayers.Keys)
		{
			if (P_0 != key)
			{
				Send(key, getScenePacket(currentPlayer), NetworkType.EnterScene);
				if (currentPlayer.isInSameScene(connectedPlayers[key]))
				{
					Send(key, GetPositionPacket(currentPlayer), NetworkType.Position);
					Send(key, GetAnimationPacket(currentPlayer), NetworkType.Animation);
					Send(key, GetDirectionPacket(currentPlayer), NetworkType.Direction);
					Send(P_0, GetPositionPacket(connectedPlayers[key]), NetworkType.Position);
					Send(P_0, GetAnimationPacket(connectedPlayers[key]), NetworkType.Animation);
					Send(P_0, GetDirectionPacket(connectedPlayers[key]), NetworkType.Direction);
				}
			}
		}
	}

	private void receivePlayerEnterScene(string P_0, byte[] P_1)
	{
		PlayerInfo currentPlayer = getCurrentPlayer(P_0);
		string sceneName = currentPlayer.sceneName;
		currentPlayer.sceneName = Encoding.UTF8.GetString(P_1);
		sendPlayerEnterScene(P_0);
		if (!string.IsNullOrEmpty(sceneName) && sceneName != currentPlayer.sceneName)
		{
			RecalculateSceneHost(currentPlayer.room, sceneName);
		}
		RecalculateSceneHost(currentPlayer.room, currentPlayer.sceneName);
	}

	private byte[] getScenePacket(PlayerInfo P_0)
	{
		List<byte> list = addPlayerNameToData(P_0.name);
		list.AddRange(Encoding.UTF8.GetBytes(P_0.sceneName));
		return list.ToArray();
	}

	private void sendPlayerLeaveScene(string P_0)
	{
		PlayerInfo currentPlayer = getCurrentPlayer(P_0);
		currentPlayer.xPos = 0f;
		currentPlayer.yPos = 0f;
		currentPlayer.animation = 0;
		currentPlayer.facingDirection = false;
		foreach (string key in connectedPlayers.Keys)
		{
			if (P_0 != key)
			{
				Send(key, Encoding.UTF8.GetBytes(currentPlayer.name), NetworkType.LeaveScene);
			}
		}
	}

	private void receivePlayerLeaveScene(string P_0, byte[] P_1)
	{
		PlayerInfo currentPlayer = getCurrentPlayer(P_0);
		string sceneName = currentPlayer.sceneName;
		string room = currentPlayer.room;
		sendPlayerLeaveScene(P_0);
		currentPlayer.sceneName = "";
		RecalculateSceneHost(room, sceneName);
	}

	private void sendPlayerSkin(string P_0)
	{
		PlayerInfo currentPlayer = getCurrentPlayer(P_0);
		foreach (string key in connectedPlayers.Keys)
		{
			if (P_0 != key)
			{
				Send(key, getSkinPacket(currentPlayer), NetworkType.Skin);
			}
		}
	}

	private void receivePlayerSkin(string P_0, byte[] P_1)
	{
		getCurrentPlayer(P_0).skin = P_1;
		sendPlayerSkin(P_0);
	}

	private byte[] getSkinPacket(PlayerInfo P_0)
	{
		List<byte> list = addPlayerNameToData(P_0.name);
		list.AddRange(P_0.skin);
		return list.ToArray();
	}

	private void sendPlayerTeam(string P_0)
	{
		PlayerInfo currentPlayer = getCurrentPlayer(P_0);
		foreach (string key in connectedPlayers.Keys)
		{
			if (P_0 != key)
			{
				Send(key, getTeamPacket(currentPlayer), NetworkType.Team);
			}
		}
		Logger.Info("Sending all server data to " + P_0);
		for (byte b = 0; b < 14; b++)
		{
			Dictionary<string, byte> teamProgressSet = Core.getTeamData(currentPlayer.team).GetTeamProgressSet(b);
			foreach (string key2 in teamProgressSet.Keys)
			{
				byte b2 = teamProgressSet[key2];
				if (b == 6 && key2 == "FLASK")
				{
					Logger.Info("Send all server flask: " + b2);
					b2 -= Core.getTeamData(currentPlayer.team).GetTeamProgressValue(6, "FLASKHEALTH");
					Logger.Info("Send all send flask: " + b2);
				}
				Send(P_0, getProgressPacket("*", b, b2, key2), NetworkType.Progress);
			}
		}
	}

	private void receivePlayerTeam(string P_0, byte[] P_1)
	{
		getCurrentPlayer(P_0).team = P_1[0];
		sendPlayerTeam(P_0);
		Core.removeUnusedGameData(connectedPlayers);
	}

	private byte[] getTeamPacket(PlayerInfo P_0)
	{
		List<byte> list = addPlayerNameToData(P_0.name);
		list.Add(P_0.team);
		return list.ToArray();
	}

	private void sendPlayerConnection(string P_0, bool P_1)
	{
		PlayerInfo currentPlayer = getCurrentPlayer(P_0);
		foreach (string key in connectedPlayers.Keys)
		{
			if (P_0 != key)
			{
				Send(key, getConnectionPacket(currentPlayer, P_1), NetworkType.Connection);
			}
		}
	}

	private byte[] getConnectionPacket(PlayerInfo P_0, bool P_1)
	{
		List<byte> list = addPlayerNameToData(P_0.name);
		list.AddRange(BitConverter.GetBytes(P_1));
		return list.ToArray();
	}

	private void sendPlayerIntro(string P_0, RefusalType P_1)
	{
		Send(P_0, getIntroPacket((byte)P_1), NetworkType.Intro);
		if (P_1 > RefusalType.Accepted)
		{
			return;
		}
		PlayerInfo currentPlayer = getCurrentPlayer(P_0);
		foreach (string key in connectedPlayers.Keys)
		{
			if (!(P_0 != key))
			{
				continue;
			}
			Send(P_0, getConnectionPacket(connectedPlayers[key], true), NetworkType.Connection);
			Send(P_0, getSkinPacket(connectedPlayers[key]), NetworkType.Skin);
			Send(P_0, getTeamPacket(connectedPlayers[key]), NetworkType.Team);
			if (connectedPlayers[key].sceneName != "")
			{
				Send(P_0, getScenePacket(connectedPlayers[key]), NetworkType.EnterScene);
				if (currentPlayer.isInSameScene(connectedPlayers[key]))
				{
					Send(P_0, GetPositionPacket(connectedPlayers[key]), NetworkType.Position);
					Send(P_0, GetAnimationPacket(connectedPlayers[key]), NetworkType.Animation);
					Send(P_0, GetDirectionPacket(connectedPlayers[key]), NetworkType.Direction);
				}
			}
		}
	}

	private void receivePlayerIntro(string P_0, byte[] P_1)
	{
		int num = 0;
		if (connectedPlayers.Count >= _maxPlayers)
		{
			Logger.Warn("Player connection rejected: Player limit reached");
			sendPlayerIntro(P_0, RefusalType.PlayerLimit);
			return;
		}
		if (connectedPlayers.ContainsKey(P_0))
		{
			Logger.Warn("Player connection rejected: Duplicate ip address");
			sendPlayerIntro(P_0, RefusalType.DuplicateIp);
			return;
		}
		if (P_1[num++] != 16)
		{
			Logger.Warn("Player connection rejected: Protocol version doesn't match");
			sendPlayerIntro(P_0, RefusalType.Protocol);
			return;
		}
		byte b = P_1[num];
		if (num + 1 + b > P_1.Length)
		{
			sendPlayerIntro(P_0, RefusalType.Protocol);
			return;
		}
		string text = Encoding.UTF8.GetString(P_1, num + 1, b);
		num += b + 1;
		byte b2 = P_1[num];
		if (num + 1 + b2 > P_1.Length)
		{
			sendPlayerIntro(P_0, RefusalType.Protocol);
			return;
		}
		string text2 = Encoding.UTF8.GetString(P_1, num + 1, b2);
		num += b2 + 1;
		foreach (PlayerInfo value in connectedPlayers.Values)
		{
			if (value.name == text2)
			{
				Logger.Warn("Player connection rejected: Duplicate name");
				sendPlayerIntro(P_0, RefusalType.DuplicateName);
				return;
			}
		}
		byte b3 = P_1[num];
		if (num + 1 + b3 > P_1.Length)
		{
			sendPlayerIntro(P_0, RefusalType.Protocol);
			return;
		}
		string text3 = ((b3 == 0) ? null : Encoding.UTF8.GetString(P_1, num + 1, b3));
		num += b3 + 1;
		if (!string.IsNullOrEmpty(_password) && (text3 == null || text3 != _password))
		{
			Logger.Warn("Player connection rejected: Incorrect password");
			sendPlayerIntro(P_0, RefusalType.Password);
			return;
		}
		if (num >= P_1.Length)
		{
			sendPlayerIntro(P_0, RefusalType.Protocol);
			return;
		}
		byte b4 = P_1[num];
		Logger.Info("Player connection accepted");
		PlayerInfo playerInfo = new PlayerInfo(text2, b4, text);
		connectedPlayers.Add(P_0, playerInfo);
		bool flag = false;
		if (string.IsNullOrEmpty(_sessionEnemyHostName))
		{
			_sessionEnemyHostName = text2;
			flag = true;
			Logger.Info("Session enemy host set to '" + _sessionEnemyHostName + "'");
		}
		sendPlayerConnection(P_0, true);
		sendPlayerIntro(P_0, RefusalType.Accepted);
		if (flag)
		{
			BroadcastSessionEnemyHost();
		}
	}

	private byte[] getIntroPacket(byte P_0)
	{
		List<byte> list = new List<byte> { P_0 };
		if (P_0 == 0)
		{
			byte[] bytes = Encoding.UTF8.GetBytes(_sessionEnemyHostName ?? string.Empty);
			list.Add((byte)bytes.Length);
			list.AddRange(bytes);
		}
		return list.ToArray();
	}

	private void BroadcastSessionEnemyHost()
	{
		byte[] bytes = Encoding.UTF8.GetBytes(_sessionEnemyHostName ?? string.Empty);
		List<byte> list = new List<byte>();
		list.Add((byte)bytes.Length);
		list.AddRange(bytes);
		byte[] array = list.ToArray();
		foreach (string key in connectedPlayers.Keys)
		{
			Send(key, array, NetworkType.SessionEnemyHost);
		}
	}

	private void sendPlayerProgress(string P_0, byte P_1, byte P_2, string P_3)
	{
		PlayerInfo currentPlayer = getCurrentPlayer(P_0);
		foreach (string key in connectedPlayers.Keys)
		{
			if (P_0 != key && currentPlayer.team == getCurrentPlayer(key).team)
			{
				Send(key, getProgressPacket(currentPlayer.name, P_1, P_2, P_3), NetworkType.Progress);
			}
		}
	}

	private void receivePlayerProgress(string P_0, byte[] P_1)
	{
		PlayerInfo currentPlayer = getCurrentPlayer(P_0);
		byte b = P_1[0];
		byte b2 = P_1[1];
		string text = Encoding.UTF8.GetString(P_1, 2, P_1.Length - 2);
		if (!Core.getTeamData(currentPlayer.team).AddTeamProgress(text, b, b2))
		{
			Logger.ProgressBad($"Received duplicated or inferior progress from {currentPlayer.name}: {text}, Type {b}, Value {b2}");
			return;
		}
		if (b >= 0 && b <= 5)
		{
			Logger.ProgressGood($"{((b2 == 0) ? "Received new" : "Lost an")} item from {currentPlayer.name}: {text}");
		}
		else
		{
			switch (b)
			{
			case 6:
				Logger.ProgressGood($"Received new stat upgrade from {currentPlayer.name}: {text} level {b2 + 1}");
				break;
			case 7:
				Logger.ProgressGood("Received new skill from " + currentPlayer.name + ": " + text);
				break;
			case 8:
				Logger.ProgressGood("Received new map cell from " + currentPlayer.name + ": " + text);
				break;
			case 9:
				Logger.ProgressGood("Received new flag from " + currentPlayer.name + ": " + text);
				break;
			case 10:
				Logger.ProgressGood("Received new pers. object from " + currentPlayer.name + ": " + text);
				break;
			case 11:
				Logger.ProgressGood("Received new teleport location from " + currentPlayer.name + ": " + text);
				break;
			case 12:
				Logger.ProgressGood($"Received new tear donation from {currentPlayer.name}: {b2}");
				break;
			case 13:
				Logger.ProgressGood("Received new miriam status from " + currentPlayer.name + ": " + text);
				break;
			}
		}
		if (b == 6)
		{
			if (text == "FLASK")
			{
				Logger.Info("Received flask level: " + b2);
				byte teamProgressValue = Core.getTeamData(currentPlayer.team).GetTeamProgressValue(6, "FLASKHEALTH");
				b2 -= teamProgressValue;
				Logger.Info("Flask level sent out: " + b2);
			}
			else if (text == "FLASKHEALTH")
			{
				byte teamProgressValue2 = Core.getTeamData(currentPlayer.team).GetTeamProgressValue(6, "FLASK");
				Logger.Info("Flask level stored: " + teamProgressValue2);
				Logger.Info("Flask level sent: " + (byte)(teamProgressValue2 - b2));
				sendPlayerProgress(P_0, 6, (byte)(teamProgressValue2 - b2), "FLASK");
			}
		}
		sendPlayerProgress(P_0, b, b2, text);
	}

	private byte[] getProgressPacket(string P_0, byte P_1, byte P_2, string P_3)
	{
		List<byte> list = addPlayerNameToData(P_0);
		list.Add(P_1);
		list.Add(P_2);
		list.AddRange(Encoding.UTF8.GetBytes(P_3));
		return list.ToArray();
	}

	private void sendPlayerAttack(string P_0, byte[] P_1)
	{
		PlayerInfo currentPlayer = getCurrentPlayer(P_0);
		foreach (string key in connectedPlayers.Keys)
		{
			if (P_0 != key && currentPlayer.isInSameScene(connectedPlayers[key]))
			{
				Send(key, getAttackPacket(currentPlayer, P_1), NetworkType.Attack);
			}
		}
	}

	private void receivePlayerAttack(string P_0, byte[] P_1)
	{
		sendPlayerAttack(P_0, P_1);
	}

	private byte[] getAttackPacket(PlayerInfo P_0, byte[] P_1)
	{
		List<byte> list = addPlayerNameToData(P_0.name);
		list.AddRange(P_1);
		return list.ToArray();
	}

	private void sendPlayerEffect(string P_0, byte P_1)
	{
		PlayerInfo currentPlayer = getCurrentPlayer(P_0);
		foreach (string key in connectedPlayers.Keys)
		{
			if (P_0 != key && currentPlayer.isInSameScene(connectedPlayers[key]))
			{
				Send(key, getEffectPacket(currentPlayer, P_1), NetworkType.Effect);
			}
		}
	}

	private void receivePlayerEffect(string P_0, byte[] P_1)
	{
		sendPlayerEffect(P_0, P_1[0]);
	}

	private byte[] getEffectPacket(PlayerInfo P_0, byte P_1)
	{
		List<byte> list = addPlayerNameToData(P_0.name);
		list.Add(P_1);
		return list.ToArray();
	}

	private void SendPing(string P_0, float P_1)
	{
		List<byte> list = new List<byte>();
		list.AddRange(BitConverter.GetBytes(P_1));
		foreach (KeyValuePair<string, PlayerInfo> player in getPlayers())
		{
			if (!(player.Key == P_0))
			{
				string name = player.Value.name;
				ushort ping = player.Value.ping;
				list.Add((byte)name.Length);
				list.AddRange(Encoding.UTF8.GetBytes(name));
				list.AddRange(BitConverter.GetBytes(ping));
			}
		}
		Send(P_0, list.ToArray(), NetworkType.Ping);
	}

	private void ReceivePing(string P_0, byte[] P_1)
	{
		float num = BitConverter.ToSingle(P_1, 0);
		ushort ping = BitConverter.ToUInt16(P_1, 4);
		getCurrentPlayer(P_0).ping = ping;
		SendPing(P_0, num);
	}

	private static string SceneKey(string P_0, string P_1)
	{
		return (P_0 ?? string.Empty) + "|" + (P_1 ?? string.Empty);
	}

	private void RecalculateSceneHost(string P_0, string P_1)
	{
		if (string.IsNullOrEmpty(P_1))
		{
			return;
		}
		string text = SceneKey(P_0, P_1);
		List<string> list = (from p in connectedPlayers.Values
			where p.sceneName == P_1 && (string.IsNullOrEmpty(P_0) || p.room == P_0)
			select p.name).ToList();
		if (list.Count == 0)
		{
			_sceneHosts.Remove(text);
			_sceneDeaths.Remove(text);
			return;
		}
		SceneHostState sceneHostState;
		string obj = (_sceneHosts.TryGetValue(text, out sceneHostState) ? sceneHostState.HostName : string.Empty);
		SceneHostState sceneHostState2;
		string text2 = ((!string.IsNullOrEmpty(_sessionEnemyHostName) && list.Contains(_sessionEnemyHostName)) ? _sessionEnemyHostName : ((!_sceneHosts.TryGetValue(text, out sceneHostState2) || !list.Contains(sceneHostState2.HostName)) ? list[0] : sceneHostState2.HostName));
		uint num = 1u;
		if (_sceneHosts.TryGetValue(text, out var sceneHostState3))
		{
			num = ((sceneHostState3.HostName == text2) ? sceneHostState3.Term : (sceneHostState3.Term + 1));
		}
		bool num2 = obj != text2;
		_sceneHosts[text] = new SceneHostState
		{
			HostName = text2,
			Term = num
		};
		BroadcastSceneEnemyHost(P_0, P_1, text2, num);
		Logger.Info($"Scene enemy host {text} → '{text2}' term={num}");
		if (num2)
		{
			ReplaySceneDeaths(P_0, P_1, text2);
		}
	}

	private void BroadcastSceneEnemyHost(string P_0, string P_1, string P_2, uint P_3)
	{
		byte[] array = EnemySyncCodec.BuildSceneEnemyHost(P_1, P_3, P_2);
		foreach (KeyValuePair<string, PlayerInfo> connectedPlayer in connectedPlayers)
		{
			PlayerInfo value = connectedPlayer.Value;
			if (!(value.sceneName != P_1) && (string.IsNullOrEmpty(P_0) || !(value.room != P_0)))
			{
				Send(connectedPlayer.Key, array, NetworkType.SceneEnemyHost);
			}
		}
	}

	private bool TryGetSceneHost(PlayerInfo P_0, out SceneHostState P_1)
	{
		P_1 = default(SceneHostState);
		if (P_0 == null || string.IsNullOrEmpty(P_0.sceneName))
		{
			return false;
		}
		return _sceneHosts.TryGetValue(SceneKey(P_0.room, P_0.sceneName), out P_1);
	}

	private void ReceiveEnemyAuthorityRelay(string P_0, byte[] P_1, NetworkType P_2)
	{
		PlayerInfo currentPlayer = getCurrentPlayer(P_0);
		if (!TryGetSceneHost(currentPlayer, out var sceneHostState) || currentPlayer.name != sceneHostState.HostName)
		{
			Logger.Warn($"Drop {P_2} from non-host '{currentPlayer.name}' in {currentPlayer.room}/{currentPlayer.sceneName}");
			return;
		}
		byte[] namedPacket = GetNamedPacket(currentPlayer, P_1);
		foreach (string key in connectedPlayers.Keys)
		{
			if (P_0 != key && currentPlayer.isInSameScene(connectedPlayers[key]))
			{
				Send(key, namedPacket, P_2);
			}
		}
	}

	private void ReceiveEnemyDeathRelay(string P_0, byte[] P_1)
	{
		PlayerInfo currentPlayer = getCurrentPlayer(P_0);
		if (!TryGetSceneHost(currentPlayer, out var sceneHostState) || currentPlayer.name != sceneHostState.HostName)
		{
			Logger.Warn($"Drop EnemyDeath from non-host '{currentPlayer.name}' in {currentPlayer.room}/{currentPlayer.sceneName}");
			return;
		}
		StoreSceneDeath(currentPlayer.room, currentPlayer.sceneName, P_1);
		byte[] namedPacket = GetNamedPacket(currentPlayer, P_1);
		foreach (string key in connectedPlayers.Keys)
		{
			if (P_0 != key && currentPlayer.isInSameScene(connectedPlayers[key]))
			{
				Send(key, namedPacket, NetworkType.EnemyDeath);
			}
		}
	}

	private void StoreSceneDeath(string P_0, string P_1, byte[] P_2)
	{
		if (P_2 == null || P_2.Length < 18 || string.IsNullOrEmpty(P_1))
		{
			return;
		}
		string text = SceneKey(P_0, P_1);
		int num = BitConverter.ToInt32(P_2, 14);
		if (!_sceneDeaths.TryGetValue(text, out var list))
		{
			list = new List<SceneDeathRecord>();
			_sceneDeaths[text] = list;
		}
		for (int i = 0; i < list.Count; i++)
		{
			if (list[i].NetId == num)
			{
				list[i] = new SceneDeathRecord
				{
					NetId = num,
					Body = (byte[])P_2.Clone()
				};
				return;
			}
		}
		list.Add(new SceneDeathRecord
		{
			NetId = num,
			Body = (byte[])P_2.Clone()
		});
	}

	private void ReplaySceneDeaths(string P_0, string P_1, string P_2)
	{
		string text = SceneKey(P_0, P_1);
		if (!_sceneDeaths.TryGetValue(text, out var list) || list.Count == 0)
		{
			return;
		}
		string text2 = (string.IsNullOrEmpty(P_2) ? "*" : P_2);
		Logger.Info($"Replaying {list.Count} scene death(s) for {text} as '{text2}'");
		foreach (SceneDeathRecord item in list)
		{
			List<byte> list2 = addPlayerNameToData(text2);
			list2.AddRange(item.Body);
			byte[] array = list2.ToArray();
			foreach (KeyValuePair<string, PlayerInfo> connectedPlayer in connectedPlayers)
			{
				PlayerInfo value = connectedPlayer.Value;
				if (!(value.sceneName != P_1) && (string.IsNullOrEmpty(P_0) || !(value.room != P_0)))
				{
					Send(connectedPlayer.Key, array, NetworkType.EnemyDeath);
				}
			}
		}
	}

	private void ReceiveEnemyDamageRelay(string P_0, byte[] P_1)
	{
		PlayerInfo currentPlayer = getCurrentPlayer(P_0);
		if (!TryGetSceneHost(currentPlayer, out var sceneHostState))
		{
			return;
		}
		byte[] namedPacket = GetNamedPacket(currentPlayer, P_1);
		foreach (KeyValuePair<string, PlayerInfo> connectedPlayer in connectedPlayers)
		{
			if (!(connectedPlayer.Key == P_0) && currentPlayer.isInSameScene(connectedPlayer.Value) && !(connectedPlayer.Value.name != sceneHostState.HostName))
			{
				Send(connectedPlayer.Key, namedPacket, NetworkType.EnemyDamage);
				break;
			}
		}
	}

	private void ReceiveEnemySyncRequestRelay(string P_0, byte[] P_1)
	{
		PlayerInfo currentPlayer = getCurrentPlayer(P_0);
		if (!TryGetSceneHost(currentPlayer, out var sceneHostState) || currentPlayer.name == sceneHostState.HostName)
		{
			return;
		}
		byte[] namedPacket = GetNamedPacket(currentPlayer, P_1);
		foreach (KeyValuePair<string, PlayerInfo> connectedPlayer in connectedPlayers)
		{
			if (!(connectedPlayer.Value.name != sceneHostState.HostName) && currentPlayer.isInSameScene(connectedPlayer.Value))
			{
				Send(connectedPlayer.Key, namedPacket, NetworkType.EnemySyncRequest);
				break;
			}
		}
	}

	private void ReceiveEnemyParriedRelay(string P_0, byte[] P_1)
	{
		PlayerInfo currentPlayer = getCurrentPlayer(P_0);
		if (!TryGetSceneHost(currentPlayer, out var sceneHostState) || currentPlayer.name == sceneHostState.HostName)
		{
			return;
		}
		byte[] namedPacket = GetNamedPacket(currentPlayer, P_1);
		foreach (KeyValuePair<string, PlayerInfo> connectedPlayer in connectedPlayers)
		{
			if (!(connectedPlayer.Key == P_0) && currentPlayer.isInSameScene(connectedPlayer.Value) && !(connectedPlayer.Value.name != sceneHostState.HostName))
			{
				Send(connectedPlayer.Key, namedPacket, NetworkType.EnemyParried);
				break;
			}
		}
	}

	private void ReceiveEnemyExecutedRelay(string P_0, byte[] P_1)
	{
		PlayerInfo currentPlayer = getCurrentPlayer(P_0);
		if (!TryGetSceneHost(currentPlayer, out var sceneHostState) || currentPlayer.name == sceneHostState.HostName)
		{
			return;
		}
		byte[] namedPacket = GetNamedPacket(currentPlayer, P_1);
		foreach (KeyValuePair<string, PlayerInfo> connectedPlayer in connectedPlayers)
		{
			if (!(connectedPlayer.Key == P_0) && currentPlayer.isInSameScene(connectedPlayer.Value) && !(connectedPlayer.Value.name != sceneHostState.HostName))
			{
				Send(connectedPlayer.Key, namedPacket, NetworkType.EnemyExecuted);
				break;
			}
		}
	}

	private void ReceivePlayerExecutionAnimRelay(string P_0, byte[] P_1)
	{
		PlayerInfo currentPlayer = getCurrentPlayer(P_0);
		if (currentPlayer == null || string.IsNullOrEmpty(currentPlayer.sceneName))
		{
			return;
		}
		byte[] namedPacket = GetNamedPacket(currentPlayer, P_1);
		foreach (KeyValuePair<string, PlayerInfo> connectedPlayer in connectedPlayers)
		{
			if (!(connectedPlayer.Key == P_0) && currentPlayer.isInSameScene(connectedPlayer.Value))
			{
				Send(connectedPlayer.Key, namedPacket, NetworkType.PlayerExecutionAnim);
			}
		}
	}

	private byte[] GetNamedPacket(PlayerInfo P_0, byte[] P_1)
	{
		List<byte> list = addPlayerNameToData(P_0.name);
		list.AddRange(P_1);
		return list.ToArray();
	}
}

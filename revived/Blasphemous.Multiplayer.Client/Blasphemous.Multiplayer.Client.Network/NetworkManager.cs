using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Text;
using Blasphemous.ModdingAPI;
using Blasphemous.Multiplayer.Client.Players;
using Blasphemous.Multiplayer.Client.ProgressSync;
using Blasphemous.Multiplayer.Client.PvP.Models;
using Blasphemous.Multiplayer.Common.Enums;
using Blasphemous.Multiplayer.Common.Networking;
using Framework.Managers;
using SimpleTCP;
using UnityEngine;

namespace Blasphemous.Multiplayer.Client.Network;

public class NetworkManager
{
	public delegate void ConnectDelegate(bool success, RefusalType refusal);

	private ConnectionStatus _connectionStatus;

	private SimpleTcpClient _client;

	private string _serverIp = string.Empty;

	private string _sessionEnemyHostName = string.Empty;

	private readonly List<byte> sendingQueue = new List<byte>();

	private readonly List<byte> receivingQueue = new List<byte>();

	private static readonly object datalock = new object();

	public bool IsConnected
	{
		get
		{
			if (_connectionStatus == ConnectionStatus.Connected)
			{
				return _client != null;
			}
			return false;
		}
	}

	public string ServerIP => _serverIp;

	public string SessionEnemyHostName => _sessionEnemyHostName;

	public event ConnectDelegate OnConnect;

	public bool Connect(string server, int port, string room, string player, string password, byte team)
	{
		if (_connectionStatus != ConnectionStatus.Disconnected)
		{
			return false;
		}
		try
		{
			_client = new SimpleTcpClient();
			_client.Connect(server, port);
			_client.DataReceived += ReceiveMessage;
			_client.TcpClient.NoDelay = true;
		}
		catch (SocketException)
		{
			ModLog.Info("Failed to establish connection");
			this.OnConnect?.Invoke(success: false, RefusalType.Connection);
			return false;
		}
		OnConnectOld(server, room, player, password, team);
		return true;
	}

	public void Disconnect()
	{
		_client.Disconnect();
		OnDisconnect();
	}

	private void OnConnectOld(string ipAddress, string room, string player, string password, byte team)
	{
		ModLog.Info("Established basic connection");
		_connectionStatus = ConnectionStatus.Attempting;
		_serverIp = ipAddress;
		SendIntro(room, player, password, team);
		Main.Multiplayer.SetPlayerData(player, team);
	}

	private void OnDisconnect()
	{
		_connectionStatus = ConnectionStatus.Disconnected;
		_serverIp = string.Empty;
		_sessionEnemyHostName = string.Empty;
		_client = null;
		sendingQueue.Clear();
		receivingQueue.Clear();
		ModLog.Info("Disconnected from server");
		Main.Multiplayer.OnDisconnect();
	}

	private void QueueMesssage(byte[] data, NetworkType type)
	{
		if (data != null && data.Length != 0 && _connectionStatus != ConnectionStatus.Disconnected)
		{
			sendingQueue.AddRange(FrameCodec.Wrap((byte)type, data));
		}
	}

	public void SendQueue()
	{
		if (sendingQueue.Count == 0 || _connectionStatus == ConnectionStatus.Disconnected)
		{
			return;
		}
		try
		{
			_client.Write(sendingQueue.ToArray());
		}
		catch (IOException)
		{
			OnDisconnect();
		}
		finally
		{
			sendingQueue.Clear();
		}
	}

	private void ReceiveMessage(object sender, DataReceivedEventArgs message)
	{
		lock (datalock)
		{
			receivingQueue.AddRange(message.data);
		}
	}

	public void ProcessQueue()
	{
		lock (datalock)
		{
			if (receivingQueue.Count == 0)
			{
				return;
			}
			foreach (FrameCodec.Frame item in FrameCodec.Drain(receivingQueue, delegate(string msg)
			{
				ModLog.Error(msg);
			}))
			{
				switch ((NetworkType)item.Type)
				{
				case NetworkType.Position:
					ReceivePosition(item.Payload);
					break;
				case NetworkType.Animation:
					ReceiveAnimation(item.Payload);
					break;
				case NetworkType.Direction:
					ReceiveDirection(item.Payload);
					break;
				case NetworkType.EnterScene:
					ReceiveEnterScene(item.Payload);
					break;
				case NetworkType.LeaveScene:
					ReceiveLeaveScene(item.Payload);
					break;
				case NetworkType.Skin:
					ReceiveSkin(item.Payload);
					break;
				case NetworkType.Team:
					ReceiveTeam(item.Payload);
					break;
				case NetworkType.Connection:
					ReceiveConnection(item.Payload);
					break;
				case NetworkType.Intro:
					ReceiveIntro(item.Payload);
					break;
				case NetworkType.Progress:
					ReceiveProgress(item.Payload);
					break;
				case NetworkType.Attack:
					ReceiveAttack(item.Payload);
					break;
				case NetworkType.Effect:
					ReceiveEffect(item.Payload);
					break;
				case NetworkType.Ping:
					ReceivePing(item.Payload);
					break;
				case NetworkType.EnemySnapshot:
					ReceiveEnemySnapshot(item.Payload);
					break;
				case NetworkType.EnemyState:
					ReceiveEnemyState(item.Payload);
					break;
				case NetworkType.EnemyDamage:
					ReceiveEnemyDamage(item.Payload);
					break;
				case NetworkType.EnemyDeath:
					ReceiveEnemyDeath(item.Payload);
					break;
				case NetworkType.EnemySyncRequest:
					ReceiveEnemySyncRequest(item.Payload);
					break;
				case NetworkType.EnemyPlayerHit:
					ReceiveEnemyPlayerHit(item.Payload);
					break;
				case NetworkType.SessionEnemyHost:
					ReceiveSessionEnemyHost(item.Payload);
					break;
				case NetworkType.SceneEnemyHost:
					ReceiveSceneEnemyHost(item.Payload);
					break;
				case NetworkType.EnemyParried:
					ReceiveEnemyParried(item.Payload);
					break;
				case NetworkType.EnemySyncDone:
					ReceiveEnemySyncDone(item.Payload);
					break;
				case NetworkType.EnemyExecuted:
					ReceiveEnemyExecuted(item.Payload);
					break;
				case NetworkType.PlayerExecutionAnim:
					ReceivePlayerExecutionAnim(item.Payload);
					break;
				}
			}
		}
	}

	private int ExtractNameFromData(byte[] data, out string name)
	{
		if (!EnemySyncCodec.TryExtractName(data, out name, out int bodyIndex))
		{
			name = string.Empty;
			return 0;
		}
		return bodyIndex;
	}

	public void SendPosition(Vector2 position)
	{
		List<byte> list = new List<byte>();
		list.AddRange(BitConverter.GetBytes(position.x));
		list.AddRange(BitConverter.GetBytes(position.y));
		QueueMesssage(list.ToArray(), NetworkType.Position);
	}

	private void ReceivePosition(byte[] data)
	{
		string name;
		int num = ExtractNameFromData(data, out name);
		float x = BitConverter.ToSingle(data, num);
		float y = BitConverter.ToSingle(data, num + 4);
		Main.Multiplayer.OtherPlayerManager.ReceivePosition(name, new Vector2(x, y));
	}

	public void SendAnimation(byte animation)
	{
		QueueMesssage(new byte[1] { animation }, NetworkType.Animation);
	}

	private void ReceiveAnimation(byte[] data)
	{
		string name;
		int num = ExtractNameFromData(data, out name);
		byte animation = data[num];
		Main.Multiplayer.OtherPlayerManager.ReceiveAnimation(name, animation);
	}

	public void SendDirection(bool direction)
	{
		QueueMesssage(BitConverter.GetBytes(direction), NetworkType.Direction);
	}

	private void ReceiveDirection(byte[] data)
	{
		string name;
		int startIndex = ExtractNameFromData(data, out name);
		bool direction = BitConverter.ToBoolean(data, startIndex);
		Main.Multiplayer.OtherPlayerManager.ReceiveDirection(name, direction);
	}

	public void SendEnterScene(string sceneName)
	{
		QueueMesssage(Encoding.UTF8.GetBytes(sceneName), NetworkType.EnterScene);
	}

	private void ReceiveEnterScene(byte[] data)
	{
		string name;
		int num = ExtractNameFromData(data, out name);
		string scene = Encoding.UTF8.GetString(data, num, data.Length - num);
		Main.Multiplayer.OtherPlayerManager.ReceiveEnterScene(name, scene);
	}

	public void SendLeaveScene()
	{
		QueueMesssage(new byte[1], NetworkType.LeaveScene);
	}

	private void ReceiveLeaveScene(byte[] data)
	{
		string playerName = Encoding.UTF8.GetString(data);
		Main.Multiplayer.OtherPlayerManager.ReceiveLeaveScene(playerName);
	}

	public void SendSkin(string skinName)
	{
		if (string.IsNullOrEmpty(skinName))
		{
			ModLog.Error("Skin name is invalid");
			skinName = "PENITENT_DEFAULT";
		}
		bool num = PlayerStatus.IsOriginalSkin(skinName);
		List<byte> list = new List<byte>();
		if (num)
		{
			list.Add(0);
			list.AddRange(Encoding.UTF8.GetBytes(skinName));
		}
		else
		{
			list.Add(1);
			list.AddRange(Core.ColorPaletteManager.GetColorPaletteById(skinName).texture.EncodeToPNG());
		}
		QueueMesssage(list.ToArray(), NetworkType.Skin);
	}

	private void ReceiveSkin(byte[] data)
	{
		string name;
		int num = ExtractNameFromData(data, out name);
		PlayerStatus playerStatus = Main.Multiplayer.OtherPlayerManager.FindConnectedPlayer(name);
		if (playerStatus == null)
		{
			return;
		}
		if (data[num] == 0)
		{
			string skinTexture = Encoding.UTF8.GetString(data, num + 1, data.Length - num - 1);
			playerStatus.SetSkinTexture(skinTexture);
			return;
		}
		byte[] array = new byte[data.Length - num - 1];
		for (int i = 0; i < array.Length; i++)
		{
			array[i] = data[num + 1 + i];
		}
		playerStatus.SetSkinTexture(array);
	}

	public void SendTeam(byte team)
	{
		QueueMesssage(new byte[1] { team }, NetworkType.Team);
	}

	private void ReceiveTeam(byte[] data)
	{
		string name;
		int num = ExtractNameFromData(data, out name);
		byte team = data[num];
		Main.Multiplayer.OtherPlayerManager.ReceiveTeam(name, team);
	}

	public void SendConnection()
	{
	}

	private void ReceiveConnection(byte[] data)
	{
		string name;
		int startIndex = ExtractNameFromData(data, out name);
		bool flag = BitConverter.ToBoolean(data, startIndex);
		if (flag)
		{
			Main.Multiplayer.OtherPlayerManager.AddConnectedPlayer(name);
		}
		else
		{
			Main.Multiplayer.OtherPlayerManager.ReceiveLeaveScene(name);
			Main.Multiplayer.OtherPlayerManager.RemoveConnectedPlayer(name);
		}
		Main.Multiplayer.NotificationManager.DisplayNotification(name + " " + Main.Multiplayer.LocalizationHandler.Localize(flag ? "join" : "leave"));
	}

	public void SendIntro(string room, string player, string password, byte team)
	{
		List<byte> list = new List<byte>();
		list.Add(16);
		AppendLengthPrefixedUtf8(list, room);
		AppendLengthPrefixedUtf8(list, player);
		AppendLengthPrefixedUtf8(list, password);
		list.Add(team);
		QueueMesssage(list.ToArray(), NetworkType.Intro);
	}

	private static void AppendLengthPrefixedUtf8(List<byte> bytes, string value)
	{
		byte[] bytes2 = Encoding.UTF8.GetBytes(value ?? string.Empty);
		int num = ((bytes2.Length > 255) ? 255 : bytes2.Length);
		bytes.Add((byte)num);
		for (int i = 0; i < num; i++)
		{
			bytes.Add(bytes2[i]);
		}
	}

	private void ReceiveIntro(byte[] data)
	{
		RefusalType refusalType = (RefusalType)data[0];
		bool flag = refusalType == RefusalType.Accepted;
		ModLog.Info($"Established full connection (Success: {flag})");
		if (flag)
		{
			_connectionStatus = ConnectionStatus.Connected;
			if (data.Length > 1)
			{
				byte b = data[1];
				if (b > 0 && 2 + b <= data.Length)
				{
					_sessionEnemyHostName = Encoding.UTF8.GetString(data, 2, b);
					ModLog.Info("[EnemySync] Session preferred host = " + _sessionEnemyHostName);
				}
			}
		}
		else
		{
			Disconnect();
		}
		this.OnConnect?.Invoke(flag, refusalType);
	}

	private void ReceiveSessionEnemyHost(byte[] data)
	{
		if (data != null && data.Length >= 1)
		{
			byte b = data[0];
			_sessionEnemyHostName = ((b > 0 && 1 + b <= data.Length) ? Encoding.UTF8.GetString(data, 1, b) : string.Empty);
			ModLog.Info("[EnemySync] Session preferred host updated = " + _sessionEnemyHostName);
			Main.Multiplayer.EnemyManager?.OnPlayersInSceneChanged();
		}
	}

	public void SendProgress(ProgressUpdate progress)
	{
		if (progress.ShouldSyncProgress(Main.Multiplayer.config))
		{
			List<byte> list = new List<byte>();
			list.Add((byte)progress.Type);
			list.Add(progress.Value);
			list.AddRange(Encoding.UTF8.GetBytes(progress.Id));
			QueueMesssage(list.ToArray(), NetworkType.Progress);
		}
	}

	private void ReceiveProgress(byte[] data)
	{
		string name;
		int num = ExtractNameFromData(data, out name);
		ProgressType type = (ProgressType)data[num];
		byte value = data[num + 1];
		ProgressUpdate progress = new ProgressUpdate(Encoding.UTF8.GetString(data, num + 2, data.Length - num - 2), type, value);
		Main.Multiplayer.ProgressManager.ReceiveProgress(progress);
		Main.Multiplayer.ProcessRecievedStat(name, progress);
		if (name != "*")
		{
			Main.Multiplayer.NotificationManager.DisplayProgressNotification(name, progress);
		}
	}

	public void SendAttack(string hitPlayerName, AttackType attackType, byte damageAmount)
	{
		List<byte> list = new List<byte>();
		list.Add((byte)attackType);
		list.Add(damageAmount);
		list.AddRange(Encoding.UTF8.GetBytes(hitPlayerName));
		QueueMesssage(list.ToArray(), NetworkType.Attack);
	}

	private void ReceiveAttack(byte[] data)
	{
		string name;
		int num = ExtractNameFromData(data, out name);
		AttackType attack = (AttackType)data[num];
		byte damageAmount = data[num + 1];
		string receiverName = Encoding.UTF8.GetString(data, num + 2, data.Length - num - 2);
		Main.Multiplayer.AttackManager.ReceiveAttack(name, receiverName, attack, damageAmount);
	}

	public void SendEffect(EffectType effectType)
	{
		QueueMesssage(new byte[1] { (byte)effectType }, NetworkType.Effect);
	}

	private void ReceiveEffect(byte[] data)
	{
		string name;
		int num = ExtractNameFromData(data, out name);
		EffectType effect = (EffectType)data[num];
		Main.Multiplayer.AttackManager.ReceiveEffect(name, effect);
	}

	public void SendPing(float time, ushort ping)
	{
		List<byte> list = new List<byte>();
		list.AddRange(BitConverter.GetBytes(time));
		list.AddRange(BitConverter.GetBytes(ping));
		QueueMesssage(list.ToArray(), NetworkType.Ping);
	}

	private void ReceivePing(byte[] data)
	{
		float time = BitConverter.ToSingle(data, 0);
		int num = 4;
		while (num < data.Length)
		{
			byte b = data[num];
			string playerName = Encoding.UTF8.GetString(data, num + 1, b);
			ushort ping = BitConverter.ToUInt16(data, num + 1 + b);
			num += 1 + b + 2;
			Main.Multiplayer.OtherPlayerManager.ReceivePing(playerName, ping);
		}
		Main.Multiplayer.PingManager.ReceivePing(time);
	}

	public void SendEnemySnapshot(int netId, string enemyTypeId, Vector2 position, float life, bool alive, bool facingRight, bool attacking, bool parryable, bool despawned, bool executable, int animHash, float animTime, float hostTime, int spawnKey, uint hostTerm, ushort generation, uint sequence, int sceneHash)
	{
		QueueMesssage(EnemySyncCodec.BuildEnemyBody(netId, enemyTypeId, position.x, position.y, life, alive, facingRight, attacking, parryable, despawned, executable, animHash, animTime, hostTime, spawnKey, includeTypeId: true, sceneHash, hostTerm, generation, sequence), NetworkType.EnemySnapshot);
	}

	private void ReceiveEnemySnapshot(byte[] data)
	{
		string name;
		int idx = ExtractNameFromData(data, out name);
		if (EnemySyncCodec.TryReadEnemyBody(data, idx, includeTypeId: true, out int sceneHash, out uint hostTerm, out ushort generation, out uint sequence, out int netId, out string enemyTypeId, out float x, out float y, out float life, out bool alive, out bool facingRight, out bool attacking, out bool parryable, out bool despawned, out bool executable, out int animHash, out float animTime, out float hostTime, out int spawnKey))
		{
			Main.Multiplayer.EnemyManager.ReceiveSnapshot(name, netId, enemyTypeId, new Vector2(x, y), life, alive, facingRight, attacking, parryable, despawned, executable, animHash, animTime, hostTime, spawnKey, sceneHash, hostTerm, generation, sequence);
		}
	}

	public void SendEnemyState(int netId, Vector2 position, float life, bool alive, bool facingRight, bool attacking, bool parryable, bool despawned, bool executable, int animHash, float animTime, float hostTime, int spawnKey, uint hostTerm, ushort generation, uint sequence, int sceneHash)
	{
		QueueMesssage(EnemySyncCodec.BuildEnemyBody(netId, null, position.x, position.y, life, alive, facingRight, attacking, parryable, despawned, executable, animHash, animTime, hostTime, spawnKey, includeTypeId: false, sceneHash, hostTerm, generation, sequence), NetworkType.EnemyState);
	}

	private void ReceiveEnemyState(byte[] data)
	{
		string name;
		int idx = ExtractNameFromData(data, out name);
		if (EnemySyncCodec.TryReadEnemyBody(data, idx, includeTypeId: false, out int sceneHash, out uint hostTerm, out ushort generation, out uint sequence, out int netId, out string _, out float x, out float y, out float life, out bool alive, out bool facingRight, out bool attacking, out bool parryable, out bool despawned, out bool executable, out int animHash, out float animTime, out float hostTime, out int spawnKey))
		{
			Main.Multiplayer.EnemyManager.ReceiveState(name, netId, new Vector2(x, y), life, alive, facingRight, attacking, parryable, despawned, executable, animHash, animTime, hostTime, spawnKey, sceneHash, hostTerm, generation, sequence);
		}
	}

	public void SendEnemyDamage(int netId, float amount, Vector2 hitPos, string enemyTypeId, byte damageType, byte damageElement, float force, uint attackInstanceId, EnemyHitFlags hitFlags, uint hostTerm, ushort generation, int sceneHash)
	{
		QueueMesssage(EnemySyncCodec.BuildDamage(netId, amount, hitPos.x, hitPos.y, damageType, damageElement, force, attackInstanceId, hitFlags, enemyTypeId, sceneHash, hostTerm, generation), NetworkType.EnemyDamage);
	}

	private void ReceiveEnemyDamage(byte[] data)
	{
		string name;
		int idx = ExtractNameFromData(data, out name);
		if (EnemySyncCodec.TryReadDamage(data, idx, out int sceneHash, out uint hostTerm, out ushort generation, out int netId, out float amount, out float hitX, out float hitY, out byte damageType, out byte damageElement, out float force, out uint attackInstanceId, out EnemyHitFlags hitFlags, out string enemyTypeId))
		{
			Main.Multiplayer.EnemyManager.ReceiveDamage(netId, amount, name, new Vector2(hitX, hitY), enemyTypeId, damageType, damageElement, force, attackInstanceId, hitFlags, sceneHash, hostTerm, generation);
		}
	}

	public void SendEnemyDeath(int netId, Vector2 deathPos, string enemyTypeId, int spawnKey, uint hostTerm, ushort generation, uint sequence, int sceneHash)
	{
		QueueMesssage(EnemySyncCodec.BuildDeath(netId, deathPos.x, deathPos.y, enemyTypeId, spawnKey, sceneHash, hostTerm, generation, sequence), NetworkType.EnemyDeath);
	}

	private void ReceiveEnemyDeath(byte[] data)
	{
		string name;
		int idx = ExtractNameFromData(data, out name);
		if (EnemySyncCodec.TryReadDeath(data, idx, out int sceneHash, out uint hostTerm, out ushort generation, out uint sequence, out int netId, out float x, out float y, out int spawnKey, out string enemyTypeId))
		{
			Main.Multiplayer.EnemyManager.ReceiveDeath(name, netId, new Vector2(x, y), enemyTypeId, spawnKey, sceneHash, hostTerm, generation, sequence);
		}
	}

	public void SendEnemySyncRequest()
	{
		QueueMesssage(new byte[1], NetworkType.EnemySyncRequest);
	}

	private void ReceiveEnemySyncRequest(byte[] data)
	{
		ExtractNameFromData(data, out var name);
		Main.Multiplayer.EnemyManager.ReceiveSyncRequest(name);
	}

	public void SendEnemyPlayerHit(string receiverName, float damageAmount, int enemyNetId, byte damageType, byte damageElement, float force, uint attackInstanceId, Vector2 direction, EnemyHitFlags hitFlags, uint hostTerm, int sceneHash)
	{
		QueueMesssage(EnemySyncCodec.BuildPlayerHit(receiverName, damageAmount, enemyNetId, damageType, damageElement, force, attackInstanceId, direction.x, direction.y, hitFlags, sceneHash, hostTerm), NetworkType.EnemyPlayerHit);
	}

	private void ReceiveEnemyPlayerHit(byte[] data)
	{
		string name;
		int idx = ExtractNameFromData(data, out name);
		if (EnemySyncCodec.TryReadPlayerHit(data, idx, out int sceneHash, out uint hostTerm, out string receiverName, out float amount, out int enemyNetId, out byte damageType, out byte damageElement, out float force, out uint attackInstanceId, out float dirX, out float dirY, out EnemyHitFlags hitFlags))
		{
			Main.Multiplayer.EnemyManager.ReceiveEnemyPlayerHit(name, receiverName, amount, enemyNetId, damageType, damageElement, force, attackInstanceId, new Vector2(dirX, dirY), hitFlags, sceneHash, hostTerm);
		}
	}

	public void SendEnemyParried(int netId, uint hostTerm, int sceneHash)
	{
		QueueMesssage(EnemySyncCodec.BuildEnemyParried(netId, sceneHash, hostTerm), NetworkType.EnemyParried);
	}

	private void ReceiveEnemyParried(byte[] data)
	{
		string name;
		int idx = ExtractNameFromData(data, out name);
		if (EnemySyncCodec.TryReadEnemyParried(data, idx, out var sceneHash, out var hostTerm, out var netId))
		{
			Main.Multiplayer.EnemyManager.ReceiveEnemyParried(name, netId, sceneHash, hostTerm);
		}
	}

	public void SendEnemySyncDone(int liveCount, uint hostTerm, int sceneHash)
	{
		QueueMesssage(EnemySyncCodec.BuildEnemySyncDone(liveCount, sceneHash, hostTerm), NetworkType.EnemySyncDone);
	}

	private void ReceiveEnemySyncDone(byte[] data)
	{
		string name;
		int idx = ExtractNameFromData(data, out name);
		if (EnemySyncCodec.TryReadEnemySyncDone(data, idx, out var sceneHash, out var hostTerm, out var liveCount))
		{
			Main.Multiplayer.EnemyManager.ReceiveEnemySyncDone(name, liveCount, sceneHash, hostTerm);
		}
	}

	public void SendEnemyExecuted(int netId, uint hostTerm, int sceneHash)
	{
		QueueMesssage(EnemySyncCodec.BuildEnemyExecuted(netId, sceneHash, hostTerm), NetworkType.EnemyExecuted);
	}

	private void ReceiveEnemyExecuted(byte[] data)
	{
		string name;
		int idx = ExtractNameFromData(data, out name);
		if (EnemySyncCodec.TryReadEnemyExecuted(data, idx, out var sceneHash, out var hostTerm, out var netId))
		{
			Main.Multiplayer.EnemyManager.ReceiveEnemyExecuted(name, netId, sceneHash, hostTerm);
		}
	}

	public void SendPlayerExecutionAnim(int netId, int stateHash, float normalizedTime, uint hostTerm, int sceneHash)
	{
		QueueMesssage(EnemySyncCodec.BuildPlayerExecutionAnim(netId, stateHash, normalizedTime, sceneHash, hostTerm), NetworkType.PlayerExecutionAnim);
	}

	private void ReceivePlayerExecutionAnim(byte[] data)
	{
		string name;
		int idx = ExtractNameFromData(data, out name);
		if (EnemySyncCodec.TryReadPlayerExecutionAnim(data, idx, out var sceneHash, out var hostTerm, out var netId, out var stateHash, out var normalizedTime))
		{
			Main.Multiplayer.EnemyManager.ReceivePlayerExecutionAnim(name, netId, stateHash, normalizedTime, sceneHash, hostTerm);
		}
	}

	private void ReceiveSceneEnemyHost(byte[] data)
	{
		if (EnemySyncCodec.TryReadSceneEnemyHost(data, 0, out string scene, out uint hostTerm, out string hostName))
		{
			ModLog.Info($"[EnemySync] Scene host for '{scene}' → '{hostName}' term={hostTerm}");
			Main.Multiplayer.EnemyManager?.ReceiveSceneHost(scene, hostName, hostTerm);
		}
	}
}

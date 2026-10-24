using Blasphemous.ModdingAPI;
using Blasphemous.Multiplayer.Client.Enemies;
using Blasphemous.Multiplayer.Client.Players;
using Blasphemous.Multiplayer.Client.ProgressSync;
using Blasphemous.Multiplayer.Client.PvP.Models;
using Blasphemous.Multiplayer.Common.Enums;
using Framework.Managers;
using SimpleTCP;
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Blasphemous.Multiplayer.Client.Network
{
    public class NetworkManager
    {
        private ConnectionStatus _connectionStatus = ConnectionStatus.Disconnected;
        private SimpleTcpClient _client;
        private string _serverIp = string.Empty;

        public bool IsConnected => _connectionStatus == ConnectionStatus.Connected && _client != null;
        public string ServerIP => _serverIp;

        private readonly List<byte> sendingQueue = new ();
        private readonly List<byte> receivingQueue = new ();
        private static readonly object datalock = new object();

        // Connection

        public bool Connect(string server, int port, string room, string player, string password, byte team)
        {
            if (_connectionStatus != ConnectionStatus.Disconnected)
                return false;

            try
            {
                _client = new SimpleTcpClient();
                _client.Connect(server, port);
                _client.DataReceived += ReceiveMessage;
                _client.TcpClient.NoDelay = true;
            }
            catch (System.Net.Sockets.SocketException)
            {
                ModLog.Info("Failed to establish connection");
                OnConnect?.Invoke(false, RefusalType.Connection);
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
            _client = null;
            sendingQueue.Clear();
            receivingQueue.Clear();

            ModLog.Info("Disconnected from server");
            Main.Multiplayer.OnDisconnect();
        }

        // Server

        private void QueueMesssage(byte[] data, NetworkType type)
        {
            if (data == null || data.Length == 0 || _connectionStatus == ConnectionStatus.Disconnected) return;

            List<byte> bytes = new();
            bytes.AddRange(BitConverter.GetBytes((ushort)data.Length));
            bytes.Add((byte)type);
            bytes.AddRange(data);

            sendingQueue.AddRange(bytes);
        }

        public void SendQueue()
        {
            if (sendingQueue.Count == 0 || _connectionStatus == ConnectionStatus.Disconnected) return;

            try
            {
                _client.Write(sendingQueue.ToArray());
            }
            catch (System.IO.IOException)
            {
                OnDisconnect();
            }
            finally
            {
                sendingQueue.Clear();
            }
        }

        // Data should be formatted as length length type data
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
                if (receivingQueue.Count == 0) return;

                int startIdx = 0;
                while (startIdx < receivingQueue.Count - 3)
                {
                    ushort length = BitConverter.ToUInt16(receivingQueue.GetRange(startIdx, 2).ToArray(), 0);
                    byte type = receivingQueue[startIdx + 2];
                    byte[] messageData = new byte[length];
                    for (int i = 0; i < length; i++)
                        messageData[i] = receivingQueue[startIdx + 3 + i];

                    switch ((NetworkType)type)
                    {
                        case NetworkType.Position: ReceivePosition(messageData); break;
                        case NetworkType.Animation: ReceiveAnimation(messageData); break;
                        case NetworkType.Direction: ReceiveDirection(messageData); break;
                        case NetworkType.EnterScene: ReceiveEnterScene(messageData); break;
                        case NetworkType.LeaveScene: ReceiveLeaveScene(messageData); break;
                        case NetworkType.Skin: ReceiveSkin(messageData); break;
                        case NetworkType.Team: ReceiveTeam(messageData); break;
                        case NetworkType.Connection: ReceiveConnection(messageData); break;
                        case NetworkType.Intro: ReceiveIntro(messageData); break;
                        case NetworkType.Progress: ReceiveProgress(messageData); break;
                        case NetworkType.Attack: ReceiveAttack(messageData); break;
                        case NetworkType.Effect: ReceiveEffect(messageData); break;
                        case NetworkType.Ping: ReceivePing(messageData); break;
                        case NetworkType.EnemySnapshot: ReceiveEnemySnapshot(messageData); break;
                        case NetworkType.EnemyState: ReceiveEnemyState(messageData); break;
                        case NetworkType.EnemyDamage: ReceiveEnemyDamage(messageData); break;
                        case NetworkType.EnemyDeath: ReceiveEnemyDeath(messageData); break;
                        case NetworkType.EnemySyncRequest: ReceiveEnemySyncRequest(messageData); break;
                        case NetworkType.EnemyPlayerHit: ReceiveEnemyPlayerHit(messageData); break;
                    }
                    startIdx += 3 + length;
                }
                if (startIdx != receivingQueue.Count)
                    ModLog.Error("Received data was formatted incorrectly");

                receivingQueue.Clear();
            }
        }

        private int ExtractNameFromData(byte[] data, out string name)
        {
            byte nameLength = data[0];
            name = Encoding.UTF8.GetString(data, 1, nameLength);
            return nameLength + 1;
        }

        // Position

        public void SendPosition(Vector2 position)
        {
            List<byte> bytes = new();
            bytes.AddRange(BitConverter.GetBytes(position.x));
            bytes.AddRange(BitConverter.GetBytes(position.y));
            QueueMesssage(bytes.ToArray(), NetworkType.Position);
        }

        private void ReceivePosition(byte[] data)
        {
            int startIdx = ExtractNameFromData(data, out string playerName);
            float xpos = BitConverter.ToSingle(data, startIdx);
            float ypos = BitConverter.ToSingle(data, startIdx + 4);

            Main.Multiplayer.OtherPlayerManager.ReceivePosition(playerName, new Vector2(xpos, ypos));
        }
        
        // Animation

        public void SendAnimation(byte animation)
        {
            QueueMesssage(new byte[] { animation }, NetworkType.Animation);
        }

        private void ReceiveAnimation(byte[] data)
        {
            int startIdx = ExtractNameFromData(data, out string playerName);
            byte animation = data[startIdx];

            Main.Multiplayer.OtherPlayerManager.ReceiveAnimation(playerName, animation);
        }

        // Direction

        public void SendDirection(bool direction)
        {
            QueueMesssage(BitConverter.GetBytes(direction), NetworkType.Direction);
        }

        private void ReceiveDirection(byte[] data)
        {
            int startIdx = ExtractNameFromData(data, out string playerName);
            bool directon = BitConverter.ToBoolean(data, startIdx);

            Main.Multiplayer.OtherPlayerManager.ReceiveDirection(playerName, directon);
        }

        // Enter scene

        public void SendEnterScene(string sceneName)
        {
            QueueMesssage(Encoding.UTF8.GetBytes(sceneName), NetworkType.EnterScene);
        }

        private void ReceiveEnterScene(byte[] data)
        {
            int startIdx = ExtractNameFromData(data, out string playerName);
            string scene = Encoding.UTF8.GetString(data, startIdx, data.Length - startIdx);

            Main.Multiplayer.OtherPlayerManager.ReceiveEnterScene(playerName, scene);
        }

        // Leave scene

        public void SendLeaveScene()
        {
            QueueMesssage(new byte[] { 0 }, NetworkType.LeaveScene);
        }

        private void ReceiveLeaveScene(byte[] data)
        {
            string playerName = Encoding.UTF8.GetString(data);

            Main.Multiplayer.OtherPlayerManager.ReceiveLeaveScene(playerName);
        }

        // Skin

        public void SendSkin(string skinName)
        {
            if (string.IsNullOrEmpty(skinName))
            {
                ModLog.Error("Skin name is invalid");
                skinName = "PENITENT_DEFAULT";
            }

            bool originalSkin = PlayerStatus.IsOriginalSkin(skinName);
            List<byte> bytes = new ();

            if (originalSkin)
            {
                bytes.Add(0);
                bytes.AddRange(Encoding.UTF8.GetBytes(skinName));
            }
            else
            {
                bytes.Add(1);
                bytes.AddRange(Core.ColorPaletteManager.GetColorPaletteById(skinName).texture.EncodeToPNG());
            }

            QueueMesssage(bytes.ToArray(), NetworkType.Skin);
        }

        private void ReceiveSkin(byte[] data)
        {
            int startIdx = ExtractNameFromData(data, out string playerName);

            PlayerStatus player = Main.Multiplayer.OtherPlayerManager.FindConnectedPlayer(playerName);
            if (player == null) return;

            bool originalSkin = data[startIdx] == 0;
            if (originalSkin)
            {
                string skinName = Encoding.UTF8.GetString(data, startIdx + 1, data.Length - startIdx - 1);
                player.SetSkinTexture(skinName);
            }
            else
            {
                byte[] skinData = new byte[data.Length - startIdx - 1];
                for (int i = 0; i < skinData.Length; i++)
                {
                    skinData[i] = data[startIdx + 1 + i];
                }
                player.SetSkinTexture(skinData);
            }
        }

        // Team

        public void SendTeam(byte team)
        {
            QueueMesssage(new byte[] { team }, NetworkType.Team);
        }

        private void ReceiveTeam(byte[] data)
        {
            int startIdx = ExtractNameFromData(data, out string playerName);
            byte team = data[startIdx];

            Main.Multiplayer.OtherPlayerManager.ReceiveTeam(playerName, team);
        }

        // Connection

        public void SendConnection()
        {
            // A client will never send a player connection, this is only sent by the server
        }

        private void ReceiveConnection(byte[] data)
        {
            int startIdx = ExtractNameFromData(data, out string playerName);
            bool connected = BitConverter.ToBoolean(data, startIdx);

            if (connected)
            {
                // Add this player to the list of connected players
                Main.Multiplayer.OtherPlayerManager.AddConnectedPlayer(playerName);
            }
            else
            {
                // Remove this player from the list of connected players
                Main.Multiplayer.OtherPlayerManager.ReceiveLeaveScene(playerName);
                Main.Multiplayer.OtherPlayerManager.RemoveConnectedPlayer(playerName);
            }
            Main.Multiplayer.NotificationManager.DisplayNotification(
                $"{playerName} {Main.Multiplayer.LocalizationHandler.Localize(connected ? "join" : "leave")}");
        }

        // Intro

        public void SendIntro(string room, string player, string password, byte team)
        {
            var bytes = new List<byte>();
            bytes.Add(PROTOCOL_VERSION);

            bytes.Add((byte)room.Length);
            bytes.AddRange(Encoding.UTF8.GetBytes(room));

            bytes.Add((byte)player.Length);
            bytes.AddRange(Encoding.UTF8.GetBytes(player));

            bytes.Add((byte)password.Length);
            if (password.Length > 0)
                bytes.AddRange(Encoding.UTF8.GetBytes(password));

            bytes.Add(team);

            QueueMesssage(bytes.ToArray(), NetworkType.Intro);
        }

        private void ReceiveIntro(byte[] data)
        {
            RefusalType response = (RefusalType)data[0];
            bool success = response == RefusalType.Accepted;

            ModLog.Info($"Established full connection (Success: {success})");

            if (success)
            {
                // Successfully connected and can sync data now
                _connectionStatus = ConnectionStatus.Connected;
            }
            else
            {
                // Rejected from server
                Disconnect();
            }

            OnConnect?.Invoke(success, response);
        }

        // Progress

        public void SendProgress(ProgressUpdate progress)
        {
            if (!progress.ShouldSyncProgress(Main.Multiplayer.config))
                return;

            List<byte> bytes = new ();
            bytes.Add((byte)progress.Type);
            bytes.Add(progress.Value);
            bytes.AddRange(Encoding.UTF8.GetBytes(progress.Id));

            QueueMesssage(bytes.ToArray(), NetworkType.Progress);
        }

        private void ReceiveProgress(byte[] data)
        {
            int startIdx = ExtractNameFromData(data, out string playerName);
            ProgressType progressType = (ProgressType)data[startIdx];
            byte progressValue = data[startIdx + 1];
            string progressId = Encoding.UTF8.GetString(data, startIdx + 2, data.Length - startIdx - 2);

            ProgressUpdate progress = new ProgressUpdate(progressId, progressType, progressValue);
            Main.Multiplayer.ProgressManager.ReceiveProgress(progress);
            Main.Multiplayer.ProcessRecievedStat(playerName, progress);

            if (playerName != "*")
                Main.Multiplayer.NotificationManager.DisplayProgressNotification(playerName, progress);
        }

        // Attack

        public void SendAttack(string hitPlayerName, AttackType attackType, byte damageAmount)
        {
            List<byte> bytes = new ();
            bytes.Add((byte)attackType);
            bytes.Add(damageAmount);
            bytes.AddRange(Encoding.UTF8.GetBytes(hitPlayerName));

            QueueMesssage(bytes.ToArray(), NetworkType.Attack);
        }

        private void ReceiveAttack(byte[] data)
        {
            int startIdx = ExtractNameFromData(data, out string attackerName);
            AttackType attackType = (AttackType)data[startIdx];
            byte damageAmount = data[startIdx + 1];
            string receiverName = Encoding.UTF8.GetString(data, startIdx + 2, data.Length - startIdx - 2);

            Main.Multiplayer.AttackManager.ReceiveAttack(attackerName, receiverName, attackType, damageAmount);
        }

        // Effect

        public void SendEffect(EffectType effectType)
        {
            QueueMesssage(new byte[] { (byte)effectType }, NetworkType.Effect);
        }

        private void ReceiveEffect(byte[] data)
        {
            int startIdx = ExtractNameFromData(data, out string playerName);
            EffectType effectType = (EffectType)data[startIdx];

            Main.Multiplayer.AttackManager.ReceiveEffect(playerName, effectType);
        }

        // Ping

        public void SendPing(float time, ushort ping)
        {
            var bytes = new List<byte>();
            bytes.AddRange(BitConverter.GetBytes(time));
            bytes.AddRange(BitConverter.GetBytes(ping));

            QueueMesssage(bytes.ToArray(), NetworkType.Ping);
        }

        private void ReceivePing(byte[] data)
        {
            float time = BitConverter.ToSingle(data, 0);

            int idx = 4;
            while (idx < data.Length)
            {
                byte length = data[idx];
                string name = Encoding.UTF8.GetString(data, idx + 1, length);
                ushort ping = BitConverter.ToUInt16(data, idx + 1 + length);
                idx += 1 + length + 2;

                Main.Multiplayer.OtherPlayerManager.ReceivePing(name, ping);
            }

            Main.Multiplayer.PingManager.ReceivePing(time);
        }

        // Enemy sync — payloads are name-prefixed by the server (same as Position/Attack)

        public void SendEnemySnapshot(int netId, string enemyTypeId, Vector2 position, float life, bool alive, bool facingRight, int animHash, float animTime)
        {
            QueueMesssage(BuildEnemyBody(netId, enemyTypeId, position, life, alive, facingRight, animHash, animTime, includeTypeId: true), NetworkType.EnemySnapshot);
        }

        private void ReceiveEnemySnapshot(byte[] data)
        {
            int idx = ExtractNameFromData(data, out string sender);
            ReadEnemyBody(data, idx, includeTypeId: true, out int netId, out string enemyTypeId, out Vector2 position, out float life, out bool alive, out bool facingRight, out int animHash, out float animTime);
            Main.Multiplayer.EnemyManager.ReceiveSnapshot(sender, netId, enemyTypeId, position, life, alive, facingRight, animHash, animTime);
        }

        public void SendEnemyState(int netId, Vector2 position, float life, bool alive, bool facingRight, int animHash, float animTime)
        {
            QueueMesssage(BuildEnemyBody(netId, null, position, life, alive, facingRight, animHash, animTime, includeTypeId: false), NetworkType.EnemyState);
        }

        private void ReceiveEnemyState(byte[] data)
        {
            int idx = ExtractNameFromData(data, out string sender);
            ReadEnemyBody(data, idx, includeTypeId: false, out int netId, out _, out Vector2 position, out float life, out bool alive, out bool facingRight, out int animHash, out float animTime);
            Main.Multiplayer.EnemyManager.ReceiveState(sender, netId, position, life, alive, facingRight, animHash, animTime);
        }

        public void SendEnemyDamage(int netId, float amount, Vector2 hitPos, string enemyTypeId)
        {
            List<byte> bytes = new();
            bytes.AddRange(BitConverter.GetBytes(netId));
            bytes.AddRange(BitConverter.GetBytes(amount));
            bytes.AddRange(BitConverter.GetBytes(hitPos.x));
            bytes.AddRange(BitConverter.GetBytes(hitPos.y));
            byte[] typeBytes = Encoding.UTF8.GetBytes(enemyTypeId ?? string.Empty);
            bytes.Add((byte)typeBytes.Length);
            bytes.AddRange(typeBytes);
            QueueMesssage(bytes.ToArray(), NetworkType.EnemyDamage);
        }

        private void ReceiveEnemyDamage(byte[] data)
        {
            int startIdx = ExtractNameFromData(data, out string attackerName);
            int netId = BitConverter.ToInt32(data, startIdx);
            float amount = BitConverter.ToSingle(data, startIdx + 4);
            Vector2 hitPos = Vector2.zero;
            string enemyTypeId = string.Empty;
            if (data.Length >= startIdx + 16)
            {
                hitPos = new Vector2(
                    BitConverter.ToSingle(data, startIdx + 8),
                    BitConverter.ToSingle(data, startIdx + 12));
            }
            // Protocol 7+: enemy type id for remap
            if (data.Length > startIdx + 16)
            {
                byte typeLen = data[startIdx + 16];
                if (typeLen > 0 && startIdx + 17 + typeLen <= data.Length)
                    enemyTypeId = Encoding.UTF8.GetString(data, startIdx + 17, typeLen);
            }
            Main.Multiplayer.EnemyManager.ReceiveDamage(netId, amount, attackerName, hitPos, enemyTypeId);
        }

        public void SendEnemyDeath(int netId, Vector2 deathPos, string enemyTypeId)
        {
            List<byte> bytes = new();
            bytes.AddRange(BitConverter.GetBytes(netId));
            bytes.AddRange(BitConverter.GetBytes(deathPos.x));
            bytes.AddRange(BitConverter.GetBytes(deathPos.y));
            byte[] typeBytes = Encoding.UTF8.GetBytes(enemyTypeId ?? string.Empty);
            bytes.Add((byte)typeBytes.Length);
            bytes.AddRange(typeBytes);
            QueueMesssage(bytes.ToArray(), NetworkType.EnemyDeath);
        }

        private void ReceiveEnemyDeath(byte[] data)
        {
            int startIdx = ExtractNameFromData(data, out string sender);
            int netId = BitConverter.ToInt32(data, startIdx);
            Vector2 deathPos = Vector2.zero;
            string enemyTypeId = string.Empty;
            if (data.Length >= startIdx + 12)
            {
                deathPos = new Vector2(
                    BitConverter.ToSingle(data, startIdx + 4),
                    BitConverter.ToSingle(data, startIdx + 8));
            }
            if (data.Length > startIdx + 12)
            {
                byte typeLen = data[startIdx + 12];
                if (typeLen > 0 && startIdx + 13 + typeLen <= data.Length)
                    enemyTypeId = Encoding.UTF8.GetString(data, startIdx + 13, typeLen);
            }
            Main.Multiplayer.EnemyManager.ReceiveDeath(sender, netId, deathPos, enemyTypeId);
        }

        public void SendEnemySyncRequest()
        {
            QueueMesssage(new byte[] { 0 }, NetworkType.EnemySyncRequest);
        }

        private void ReceiveEnemySyncRequest(byte[] data)
        {
            int startIdx = ExtractNameFromData(data, out string requester);
            Main.Multiplayer.EnemyManager.ReceiveSyncRequest(requester);
        }

        public void SendEnemyPlayerHit(string receiverName, float damageAmount)
        {
            byte[] nameBytes = Encoding.UTF8.GetBytes(receiverName ?? string.Empty);
            List<byte> bytes = new();
            bytes.Add((byte)nameBytes.Length);
            bytes.AddRange(nameBytes);
            bytes.AddRange(BitConverter.GetBytes(damageAmount));
            QueueMesssage(bytes.ToArray(), NetworkType.EnemyPlayerHit);
        }

        private void ReceiveEnemyPlayerHit(byte[] data)
        {
            int idx = ExtractNameFromData(data, out string sender);
            byte nameLen = data[idx];
            string receiver = Encoding.UTF8.GetString(data, idx + 1, nameLen);
            float amount = BitConverter.ToSingle(data, idx + 1 + nameLen);
            Main.Multiplayer.EnemyManager.ReceiveEnemyPlayerHit(sender, receiver, amount);
        }

        private static byte[] BuildEnemyBody(int netId, string enemyTypeId, Vector2 position, float life, bool alive, bool facingRight, int animHash, float animTime, bool includeTypeId)
        {
            EnemySyncFlags flags = EnemySyncFlags.None;
            if (alive) flags |= EnemySyncFlags.Alive;
            if (facingRight) flags |= EnemySyncFlags.FacingRight;

            List<byte> bytes = new();
            bytes.AddRange(BitConverter.GetBytes(netId));
            bytes.Add((byte)flags);
            bytes.AddRange(BitConverter.GetBytes(position.x));
            bytes.AddRange(BitConverter.GetBytes(position.y));
            bytes.AddRange(BitConverter.GetBytes(life));
            bytes.AddRange(BitConverter.GetBytes(animHash));
            bytes.AddRange(BitConverter.GetBytes(animTime));

            if (includeTypeId)
            {
                byte[] idBytes = Encoding.UTF8.GetBytes(enemyTypeId ?? string.Empty);
                bytes.Add((byte)idBytes.Length);
                bytes.AddRange(idBytes);
            }

            return bytes.ToArray();
        }

        private static void ReadEnemyBody(byte[] data, int idx, bool includeTypeId, out int netId, out string enemyTypeId, out Vector2 position, out float life, out bool alive, out bool facingRight, out int animHash, out float animTime)
        {
            netId = BitConverter.ToInt32(data, idx);
            EnemySyncFlags flags = (EnemySyncFlags)data[idx + 4];
            alive = (flags & EnemySyncFlags.Alive) != 0;
            facingRight = (flags & EnemySyncFlags.FacingRight) != 0;
            position = new Vector2(BitConverter.ToSingle(data, idx + 5), BitConverter.ToSingle(data, idx + 9));
            life = BitConverter.ToSingle(data, idx + 13);
            animHash = 0;
            animTime = 0f;
            // Protocol 9+: anim hash after life
            if (data.Length >= idx + 21)
                animHash = BitConverter.ToInt32(data, idx + 17);
            // Protocol 11+: normalized anim time after hash
            if (data.Length >= idx + 25)
                animTime = BitConverter.ToSingle(data, idx + 21);
            enemyTypeId = string.Empty;
            if (includeTypeId)
            {
                int typeAt = data.Length >= idx + 25 ? idx + 25 : idx + 21;
                if (data.Length > typeAt)
                {
                    byte typeLen = data[typeAt];
                    if (typeLen > 0 && typeAt + 1 + typeLen <= data.Length)
                        enemyTypeId = Encoding.UTF8.GetString(data, typeAt + 1, typeLen);
                }
            }
        }

        // Maybe replace name with entire ConnectInfo
        public delegate void ConnectDelegate(bool success, RefusalType refusal);
        public event ConnectDelegate OnConnect;

        private const byte PROTOCOL_VERSION = 11;
    }
}

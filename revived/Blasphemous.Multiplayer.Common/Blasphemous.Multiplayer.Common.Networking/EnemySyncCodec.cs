using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;

namespace Blasphemous.Multiplayer.Common.Networking
{
	public static class EnemySyncCodec
	{
		public static int SceneHash(string scene)
		{
			if (string.IsNullOrEmpty(scene))
			{
				return 0;
			}
			int num = 17;
			for (int i = 0; i < scene.Length; i++)
			{
				num = num * 31 + scene[i];
			}
			return num;
		}

		public static byte[] BuildAuthorityHeader(int sceneHash, uint hostTerm, ushort generation, uint sequence)
		{
			List<byte> list = new List<byte>(14);
			list.AddRange(BitConverter.GetBytes(sceneHash));
			list.AddRange(BitConverter.GetBytes(hostTerm));
			list.AddRange(BitConverter.GetBytes(generation));
			list.AddRange(BitConverter.GetBytes(sequence));
			return list.ToArray();
		}

		public static bool TryReadAuthorityHeader(byte[] data, ref int idx, out int sceneHash, out uint hostTerm, out ushort generation, out uint sequence)
		{
			sceneHash = 0;
			hostTerm = 0u;
			generation = 0;
			sequence = 0u;
			if (data == null || idx + 14 > data.Length)
			{
				return false;
			}
			sceneHash = BitConverter.ToInt32(data, idx);
			hostTerm = BitConverter.ToUInt32(data, idx + 4);
			generation = BitConverter.ToUInt16(data, idx + 8);
			sequence = BitConverter.ToUInt32(data, idx + 10);
			idx += 14;
			return true;
		}

		public static byte[] BuildEnemyBody(int netId, string enemyTypeId, float x, float y, float life, bool alive, bool facingRight, bool attacking, bool parryable, bool despawned, bool executable, int animHash, float animTime, float hostTime, int spawnKey, bool includeTypeId, int sceneHash, uint hostTerm, ushort generation, uint sequence)
		{
			EnemySyncFlags enemySyncFlags = EnemySyncFlags.None;
			if (alive)
			{
				enemySyncFlags |= EnemySyncFlags.Alive;
			}
			if (facingRight)
			{
				enemySyncFlags |= EnemySyncFlags.FacingRight;
			}
			if (attacking)
			{
				enemySyncFlags |= EnemySyncFlags.Attacking;
			}
			if (parryable)
			{
				enemySyncFlags |= EnemySyncFlags.Parryable;
			}
			if (despawned)
			{
				enemySyncFlags |= EnemySyncFlags.Despawned;
			}
			if (executable)
			{
				enemySyncFlags |= EnemySyncFlags.Executable;
			}
			List<byte> list = new List<byte>();
			list.AddRange(BuildAuthorityHeader(sceneHash, hostTerm, generation, sequence));
			list.AddRange(BitConverter.GetBytes(netId));
			list.Add((byte)enemySyncFlags);
			list.AddRange(BitConverter.GetBytes(x));
			list.AddRange(BitConverter.GetBytes(y));
			list.AddRange(BitConverter.GetBytes(life));
			list.AddRange(BitConverter.GetBytes(animHash));
			list.AddRange(BitConverter.GetBytes(animTime));
			list.AddRange(BitConverter.GetBytes(hostTime));
			list.AddRange(BitConverter.GetBytes(spawnKey));
			if (includeTypeId)
			{
				byte[] array = Encoding.UTF8.GetBytes((enemyTypeId == null) ? string.Empty : enemyTypeId);
				if (array.Length > 255)
				{
					Array.Resize(ref array, 255);
				}
				list.Add((byte)array.Length);
				list.AddRange(array);
			}
			return list.ToArray();
		}

		public static bool TryReadEnemyBody(byte[] data, int idx, bool includeTypeId, out int sceneHash, out uint hostTerm, out ushort generation, out uint sequence, out int netId, out string enemyTypeId, out float x, out float y, out float life, out bool alive, out bool facingRight, out bool attacking, out bool parryable, out bool despawned, out bool executable, out int animHash, out float animTime, out float hostTime, out int spawnKey)
		{
			enemyTypeId = string.Empty;
			netId = 0;
			x = (y = (life = (animTime = (hostTime = 0f))));
			alive = (facingRight = (attacking = (parryable = (despawned = (executable = false)))));
			animHash = 0;
			spawnKey = 0;
			sceneHash = 0;
			hostTerm = 0u;
			generation = 0;
			sequence = 0u;
			if (!TryReadAuthorityHeader(data, ref idx, out sceneHash, out hostTerm, out generation, out sequence))
			{
				return false;
			}
			if (idx + 33 > data.Length)
			{
				return false;
			}
			netId = BitConverter.ToInt32(data, idx);
			EnemySyncFlags enemySyncFlags = (EnemySyncFlags)data[idx + 4];
			alive = (enemySyncFlags & EnemySyncFlags.Alive) != 0;
			facingRight = (enemySyncFlags & EnemySyncFlags.FacingRight) != 0;
			attacking = (enemySyncFlags & EnemySyncFlags.Attacking) != 0;
			parryable = (enemySyncFlags & EnemySyncFlags.Parryable) != 0;
			despawned = (enemySyncFlags & EnemySyncFlags.Despawned) != 0;
			executable = (enemySyncFlags & EnemySyncFlags.Executable) != 0;
			x = BitConverter.ToSingle(data, idx + 5);
			y = BitConverter.ToSingle(data, idx + 9);
			life = BitConverter.ToSingle(data, idx + 13);
			animHash = BitConverter.ToInt32(data, idx + 17);
			animTime = BitConverter.ToSingle(data, idx + 21);
			hostTime = BitConverter.ToSingle(data, idx + 25);
			spawnKey = BitConverter.ToInt32(data, idx + 29);
			idx += 33;
			if (includeTypeId)
			{
				if (idx >= data.Length)
				{
					return true;
				}
				byte b = data[idx];
				if (b > 0 && idx + 1 + b <= data.Length)
				{
					enemyTypeId = Encoding.UTF8.GetString(data, idx + 1, b);
				}
			}
			return true;
		}

		public static byte[] BuildDamage(int netId, float amount, float hitX, float hitY, byte damageType, byte damageElement, float force, uint attackInstanceId, EnemyHitFlags hitFlags, string enemyTypeId, int sceneHash, uint hostTerm, ushort generation)
		{
			List<byte> list = new List<byte>();
			list.AddRange(BuildAuthorityHeader(sceneHash, hostTerm, generation, 0u));
			list.AddRange(BitConverter.GetBytes(netId));
			list.AddRange(BitConverter.GetBytes(amount));
			list.AddRange(BitConverter.GetBytes(hitX));
			list.AddRange(BitConverter.GetBytes(hitY));
			list.Add(damageType);
			list.Add(damageElement);
			list.AddRange(BitConverter.GetBytes(force));
			list.AddRange(BitConverter.GetBytes(attackInstanceId));
			list.Add((byte)hitFlags);
			byte[] array = Encoding.UTF8.GetBytes(enemyTypeId ?? string.Empty);
			if (array.Length > 255)
			{
				Array.Resize(ref array, 255);
			}
			list.Add((byte)array.Length);
			list.AddRange(array);
			return list.ToArray();
		}

		public static bool TryReadDamage(byte[] data, int idx, out int sceneHash, out uint hostTerm, out ushort generation, out int netId, out float amount, out float hitX, out float hitY, out byte damageType, out byte damageElement, out float force, out uint attackInstanceId, out EnemyHitFlags hitFlags, out string enemyTypeId)
		{
			sceneHash = 0;
			hostTerm = 0u;
			generation = 0;
			netId = 0;
			amount = (hitX = (hitY = (force = 0f)));
			damageType = (damageElement = 0);
			attackInstanceId = 0u;
			hitFlags = EnemyHitFlags.None;
			enemyTypeId = string.Empty;
			uint sequence;
			if (!TryReadAuthorityHeader(data, ref idx, out sceneHash, out hostTerm, out generation, out sequence))
			{
				return false;
			}
			if (idx + 28 > data.Length)
			{
				return false;
			}
			netId = BitConverter.ToInt32(data, idx);
			amount = BitConverter.ToSingle(data, idx + 4);
			hitX = BitConverter.ToSingle(data, idx + 8);
			hitY = BitConverter.ToSingle(data, idx + 12);
			damageType = data[idx + 16];
			damageElement = data[idx + 17];
			force = BitConverter.ToSingle(data, idx + 18);
			attackInstanceId = BitConverter.ToUInt32(data, idx + 22);
			hitFlags = (EnemyHitFlags)data[idx + 26];
			byte b = data[idx + 27];
			if (b > 0 && idx + 28 + b <= data.Length)
			{
				enemyTypeId = Encoding.UTF8.GetString(data, idx + 28, b);
			}
			return true;
		}

		public static byte[] BuildDeath(int netId, float x, float y, string enemyTypeId, int spawnKey, int sceneHash, uint hostTerm, ushort generation, uint sequence)
		{
			List<byte> list = new List<byte>();
			list.AddRange(BuildAuthorityHeader(sceneHash, hostTerm, generation, sequence));
			list.AddRange(BitConverter.GetBytes(netId));
			list.AddRange(BitConverter.GetBytes(x));
			list.AddRange(BitConverter.GetBytes(y));
			list.AddRange(BitConverter.GetBytes(spawnKey));
			byte[] array = Encoding.UTF8.GetBytes(enemyTypeId ?? string.Empty);
			if (array.Length > 255)
			{
				Array.Resize(ref array, 255);
			}
			list.Add((byte)array.Length);
			list.AddRange(array);
			return list.ToArray();
		}

		public static bool TryReadDeath(byte[] data, int idx, out int sceneHash, out uint hostTerm, out ushort generation, out uint sequence, out int netId, out float x, out float y, out int spawnKey, out string enemyTypeId)
		{
			sceneHash = 0;
			hostTerm = 0u;
			generation = 0;
			sequence = 0u;
			netId = 0;
			x = (y = 0f);
			spawnKey = 0;
			enemyTypeId = string.Empty;
			if (!TryReadAuthorityHeader(data, ref idx, out sceneHash, out hostTerm, out generation, out sequence))
			{
				return false;
			}
			if (idx + 17 > data.Length)
			{
				return false;
			}
			netId = BitConverter.ToInt32(data, idx);
			x = BitConverter.ToSingle(data, idx + 4);
			y = BitConverter.ToSingle(data, idx + 8);
			spawnKey = BitConverter.ToInt32(data, idx + 12);
			byte b = data[idx + 16];
			if (b > 0 && idx + 17 + b <= data.Length)
			{
				enemyTypeId = Encoding.UTF8.GetString(data, idx + 17, b);
			}
			return true;
		}

		public static byte[] BuildPlayerHit(string receiverName, float amount, int enemyNetId, byte damageType, byte damageElement, float force, uint attackInstanceId, float dirX, float dirY, EnemyHitFlags hitFlags, int sceneHash, uint hostTerm)
		{
			List<byte> list = new List<byte>();
			list.AddRange(BuildAuthorityHeader(sceneHash, hostTerm, 0, 0u));
			byte[] array = Encoding.UTF8.GetBytes(receiverName ?? string.Empty);
			if (array.Length > 255)
			{
				Array.Resize(ref array, 255);
			}
			list.Add((byte)array.Length);
			list.AddRange(array);
			list.AddRange(BitConverter.GetBytes(amount));
			list.AddRange(BitConverter.GetBytes(enemyNetId));
			list.Add(damageType);
			list.Add(damageElement);
			list.AddRange(BitConverter.GetBytes(force));
			list.AddRange(BitConverter.GetBytes(attackInstanceId));
			list.AddRange(BitConverter.GetBytes(dirX));
			list.AddRange(BitConverter.GetBytes(dirY));
			list.Add((byte)hitFlags);
			return list.ToArray();
		}

		public static bool TryReadPlayerHit(byte[] data, int idx, out int sceneHash, out uint hostTerm, out string receiverName, out float amount, out int enemyNetId, out byte damageType, out byte damageElement, out float force, out uint attackInstanceId, out float dirX, out float dirY, out EnemyHitFlags hitFlags)
		{
			sceneHash = 0;
			hostTerm = 0u;
			receiverName = string.Empty;
			amount = (force = (dirX = (dirY = 0f)));
			enemyNetId = 0;
			damageType = (damageElement = 0);
			attackInstanceId = 0u;
			hitFlags = EnemyHitFlags.None;
			ushort generation;
			uint sequence;
			if (!TryReadAuthorityHeader(data, ref idx, out sceneHash, out hostTerm, out generation, out sequence))
			{
				return false;
			}
			if (idx >= data.Length)
			{
				return false;
			}
			byte b = data[idx];
			if (idx + 1 + b + 27 > data.Length)
			{
				return false;
			}
			receiverName = Encoding.UTF8.GetString(data, idx + 1, b);
			int num = idx + 1 + b;
			amount = BitConverter.ToSingle(data, num);
			enemyNetId = BitConverter.ToInt32(data, num + 4);
			damageType = data[num + 8];
			damageElement = data[num + 9];
			force = BitConverter.ToSingle(data, num + 10);
			attackInstanceId = BitConverter.ToUInt32(data, num + 14);
			dirX = BitConverter.ToSingle(data, num + 18);
			dirY = BitConverter.ToSingle(data, num + 22);
			hitFlags = (EnemyHitFlags)data[num + 26];
			return true;
		}

		public static byte[] BuildEnemyParried(int netId, int sceneHash, uint hostTerm)
		{
			List<byte> list = new List<byte>();
			list.AddRange(BuildAuthorityHeader(sceneHash, hostTerm, 0, 0u));
			list.AddRange(BitConverter.GetBytes(netId));
			return list.ToArray();
		}

		public static bool TryReadEnemyParried(byte[] data, int idx, out int sceneHash, out uint hostTerm, out int netId)
		{
			sceneHash = 0;
			hostTerm = 0u;
			netId = 0;
			ushort generation;
			uint sequence;
			if (!TryReadAuthorityHeader(data, ref idx, out sceneHash, out hostTerm, out generation, out sequence))
			{
				return false;
			}
			if (idx + 4 > data.Length)
			{
				return false;
			}
			netId = BitConverter.ToInt32(data, idx);
			return true;
		}

		public static byte[] BuildEnemySyncDone(int liveCount, int sceneHash, uint hostTerm)
		{
			List<byte> list = new List<byte>();
			list.AddRange(BuildAuthorityHeader(sceneHash, hostTerm, 0, 0u));
			list.AddRange(BitConverter.GetBytes(liveCount));
			return list.ToArray();
		}

		public static bool TryReadEnemySyncDone(byte[] data, int idx, out int sceneHash, out uint hostTerm, out int liveCount)
		{
			sceneHash = 0;
			hostTerm = 0u;
			liveCount = 0;
			ushort generation;
			uint sequence;
			if (!TryReadAuthorityHeader(data, ref idx, out sceneHash, out hostTerm, out generation, out sequence))
			{
				return false;
			}
			if (idx + 4 > data.Length)
			{
				return false;
			}
			liveCount = BitConverter.ToInt32(data, idx);
			return true;
		}

		public static byte[] BuildEnemyExecuted(int netId, int sceneHash, uint hostTerm)
		{
			return BuildEnemyParried(netId, sceneHash, hostTerm);
		}

		public static bool TryReadEnemyExecuted(byte[] data, int idx, out int sceneHash, out uint hostTerm, out int netId)
		{
			return TryReadEnemyParried(data, idx, out sceneHash, out hostTerm, out netId);
		}

		public static byte[] BuildPlayerExecutionAnim(int netId, int stateHash, float normalizedTime, int sceneHash, uint hostTerm)
		{
			List<byte> list = new List<byte>();
			list.AddRange(BuildAuthorityHeader(sceneHash, hostTerm, 0, 0u));
			list.AddRange(BitConverter.GetBytes(netId));
			list.AddRange(BitConverter.GetBytes(stateHash));
			list.AddRange(BitConverter.GetBytes(normalizedTime));
			return list.ToArray();
		}

		public static bool TryReadPlayerExecutionAnim(byte[] data, int idx, out int sceneHash, out uint hostTerm, out int netId, out int stateHash, out float normalizedTime)
		{
			sceneHash = 0;
			hostTerm = 0u;
			netId = 0;
			stateHash = 0;
			normalizedTime = 0f;
			ushort generation;
			uint sequence;
			if (!TryReadAuthorityHeader(data, ref idx, out sceneHash, out hostTerm, out generation, out sequence))
			{
				return false;
			}
			if (idx + 12 > data.Length)
			{
				return false;
			}
			netId = BitConverter.ToInt32(data, idx);
			stateHash = BitConverter.ToInt32(data, idx + 4);
			normalizedTime = BitConverter.ToSingle(data, idx + 8);
			return true;
		}

		public static byte[] BuildSceneEnemyHost(string scene, uint hostTerm, string hostName)
		{
			byte[] array = Encoding.UTF8.GetBytes(scene ?? string.Empty);
			byte[] array2 = Encoding.UTF8.GetBytes(hostName ?? string.Empty);
			if (array.Length > 255)
			{
				Array.Resize(ref array, 255);
			}
			if (array2.Length > 255)
			{
				Array.Resize(ref array2, 255);
			}
			List<byte> list = new List<byte>();
			list.Add((byte)array.Length);
			list.AddRange(array);
			list.AddRange(BitConverter.GetBytes(hostTerm));
			list.Add((byte)array2.Length);
			list.AddRange(array2);
			return list.ToArray();
		}

		public static bool TryReadSceneEnemyHost(byte[] data, int idx, out string scene, out uint hostTerm, out string hostName)
		{
			scene = string.Empty;
			hostTerm = 0u;
			hostName = string.Empty;
			if (data == null || idx >= data.Length)
			{
				return false;
			}
			byte b = data[idx];
			if (idx + 1 + b + 4 + 1 > data.Length)
			{
				return false;
			}
			scene = Encoding.UTF8.GetString(data, idx + 1, b);
			hostTerm = BitConverter.ToUInt32(data, idx + 1 + b);
			int num = idx + 1 + b + 4;
			byte b2 = data[num];
			if (num + 1 + b2 > data.Length)
			{
				return false;
			}
			hostName = Encoding.UTF8.GetString(data, num + 1, b2);
			return true;
		}

		public static byte[] PrefixName(string name, byte[] body)
		{
			byte[] array = Encoding.UTF8.GetBytes(name ?? string.Empty);
			if (array.Length > 255)
			{
				Array.Resize(ref array, 255);
			}
			List<byte> list = new List<byte>(1 + array.Length + ((body != null) ? body.Length : 0));
			list.Add((byte)array.Length);
			list.AddRange(array);
			if (body != null && body.Length != 0)
			{
				list.AddRange(body);
			}
			return list.ToArray();
		}

		public static bool TryExtractName(byte[] data, out string name, out int bodyIndex)
		{
			name = string.Empty;
			bodyIndex = 0;
			if (data == null || data.Length < 1)
			{
				return false;
			}
			byte b = data[0];
			if (1 + b > data.Length)
			{
				return false;
			}
			name = Encoding.UTF8.GetString(data, 1, b);
			bodyIndex = 1 + b;
			return true;
		}
	}
}

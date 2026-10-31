using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Blasphemous.Multiplayer.Common.Networking
{
	public static class FrameCodec
	{
		public struct Frame
		{
			public byte Type;

			public byte[] Payload;

			public Frame(byte type, byte[] payload)
			{
				Type = type;
				Payload = payload;
			}
		}

		public const int HeaderSize = 3;

		public static byte[] Wrap(byte type, byte[] payload)
		{
			if (payload == null)
			{
				payload = new byte[0];
			}
			if (payload.Length > 32768)
			{
				throw new ArgumentOutOfRangeException("payload", "Payload exceeds MAX_FRAME_PAYLOAD");
			}
			List<byte> list = new List<byte>(3 + payload.Length);
			list.AddRange(BitConverter.GetBytes((ushort)payload.Length));
			list.Add(type);
			list.AddRange(payload);
			return list.ToArray();
		}

		public static List<Frame> Drain(List<byte> buffer, Action<string> onError = null)
		{
			List<Frame> list = new List<Frame>();
			if (buffer == null || buffer.Count == 0)
			{
				return list;
			}
			int i;
			int num2;
			for (i = 0; buffer.Count - i >= 3; i += num2)
			{
				ushort num = BitConverter.ToUInt16(buffer.GetRange(i, 2).ToArray(), 0);
				if (num > 32768)
				{
					if (onError != null)
					{
						onError(string.Format("Frame length {0} exceeds max; dropping buffer", num));
					}
					buffer.Clear();
					return list;
				}
				num2 = 3 + num;
				if (buffer.Count - i < num2)
				{
					break;
				}
				byte type = buffer[i + 2];
				byte[] array = new byte[num];
				for (int j = 0; j < num; j++)
				{
					array[j] = buffer[i + 3 + j];
				}
				list.Add(new Frame(type, array));
			}
			if (i > 0)
			{
				buffer.RemoveRange(0, i);
			}
			return list;
		}

		public static List<Frame> Drain(byte[] data, List<byte> carry, Action<string> onError = null)
		{
			if (data != null && data.Length != 0)
			{
				carry.AddRange(data);
			}
			return Drain(carry, onError);
		}
	}
}

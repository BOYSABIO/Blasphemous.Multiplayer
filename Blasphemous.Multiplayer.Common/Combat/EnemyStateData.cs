using System;
using System.IO;
using System.Text;

namespace Blasphemous.Multiplayer.Common.Combat;

public sealed class EnemyStateData
{
    public int Id;
    public string Type = string.Empty;
    public float X, Y, Life, AnimationTime;
    public bool Alive, FacingRight;
    public int AnimationHash;

    public byte[] Encode(bool snapshot)
    {
        using (var stream = new MemoryStream())
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write(Id);
            writer.Write((byte)((Alive ? 1 : 0) | (FacingRight ? 2 : 0)));
            writer.Write(X); writer.Write(Y); writer.Write(Life);
            writer.Write(AnimationHash); writer.Write(AnimationTime);
            if (snapshot)
            {
                byte[] text = Encoding.UTF8.GetBytes(Type);
                if (text.Length > byte.MaxValue) throw new ArgumentException("Enemy type exceeds 255 UTF-8 bytes");
                writer.Write((byte)text.Length); writer.Write(text);
            }
            return stream.ToArray();
        }
    }

    public static EnemyStateData Decode(byte[] bytes, int offset, bool snapshot)
    {
        using (var stream = new MemoryStream(bytes, offset, bytes.Length - offset))
        using (var reader = new BinaryReader(stream))
        {
            var state = new EnemyStateData { Id = reader.ReadInt32() };
            byte flags = reader.ReadByte();
            state.Alive = (flags & 1) != 0; state.FacingRight = (flags & 2) != 0;
            state.X = reader.ReadSingle(); state.Y = reader.ReadSingle(); state.Life = reader.ReadSingle();
            state.AnimationHash = reader.ReadInt32(); state.AnimationTime = reader.ReadSingle();
            if (snapshot)
            {
                int length = reader.ReadByte();
                byte[] text = reader.ReadBytes(length);
                if (text.Length != length) throw new EndOfStreamException();
                state.Type = Encoding.UTF8.GetString(text);
            }
            if ((flags & ~3) != 0 || stream.Position != stream.Length
                || !EnemyHitData.Finite(state.X) || !EnemyHitData.Finite(state.Y)
                || !EnemyHitData.Finite(state.Life) || !EnemyHitData.Finite(state.AnimationTime))
                throw new InvalidDataException("Invalid enemy state packet");
            return state;
        }
    }
}

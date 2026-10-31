using System;
using System.IO;
using System.Text;

namespace Blasphemous.Multiplayer.Common.Combat;

/// <summary>Combat packet bodies shared by the client and regression tests (protocol 13).</summary>
public static class EnemyPackets
{
    public static byte[] WriteDamage(int id, string type, float x, float y, EnemyHitData hit)
    {
        using (var stream = new MemoryStream())
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write(id); WriteText(writer, type); writer.Write(x); writer.Write(y); hit.Write(writer);
            return stream.ToArray();
        }
    }

    public static void ReadDamage(byte[] bytes, int offset, out int id, out string type, out float x, out float y, out EnemyHitData hit)
    {
        using (var stream = new MemoryStream(bytes, offset, bytes.Length - offset))
        using (var reader = new BinaryReader(stream))
        {
            id = reader.ReadInt32(); type = ReadText(reader); x = reader.ReadSingle(); y = reader.ReadSingle();
            hit = EnemyHitData.Read(reader);
            if (!EnemyHitData.Finite(x) || !EnemyHitData.Finite(y) || stream.Position != stream.Length)
                throw new InvalidDataException("Invalid enemy damage packet");
        }
    }

    public static byte[] WritePlayerHit(string receiver, EnemyHitData hit)
    {
        using (var stream = new MemoryStream())
        using (var writer = new BinaryWriter(stream))
        {
            WriteText(writer, receiver); hit.Write(writer);
            return stream.ToArray();
        }
    }

    public static void ReadPlayerHit(byte[] bytes, int offset, out string receiver, out EnemyHitData hit)
    {
        using (var stream = new MemoryStream(bytes, offset, bytes.Length - offset))
        using (var reader = new BinaryReader(stream))
        {
            receiver = ReadText(reader); hit = EnemyHitData.Read(reader);
            if (hit.DirectDamage || stream.Position != stream.Length)
                throw new InvalidDataException("Invalid enemy player hit packet");
        }
    }

    private static void WriteText(BinaryWriter writer, string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
        if (bytes.Length > byte.MaxValue) throw new ArgumentException("Combat identifier exceeds 255 UTF-8 bytes");
        writer.Write((byte)bytes.Length); writer.Write(bytes);
    }

    private static string ReadText(BinaryReader reader)
    {
        int length = reader.ReadByte();
        byte[] bytes = reader.ReadBytes(length);
        if (bytes.Length != length) throw new EndOfStreamException();
        return Encoding.UTF8.GetString(bytes);
    }
}

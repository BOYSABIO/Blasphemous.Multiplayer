using System;
using System.IO;

namespace Blasphemous.Multiplayer.Common.Networking;

public enum WardenAttackKind : byte { Area = 0, Mace = 1 }
public enum BossEncounterState : byte { Waiting = 0, Started = 1, Finished = 2 }

public class BossEncounterData
{
    public int SceneHash, SpawnKey;
    public uint Term, Sequence;
    public BossEncounterState State;
}

public sealed class BossHazardData : BossEncounterData
{
    public ulong Id;
    public bool Active, FlipX;
    public WardenAttackKind Kind;
    public short PrefabIndex;
    public byte Stage;
    public float X, Y, Z, Angle, Strength, Damage, Preparation, Loop, Timer, HostTime, AnimationTime, AnimationLength;
    public int AnimationHash, SortingOrder;
}

public static class BossSyncCodec
{
    public const int MaxHazards = 128;
    public const int MaxEncounters = 8;
    public const float MaxSeconds = 120f;
    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    private static bool Bounded(float value, float minimum, float maximum) => Finite(value) && value >= minimum && value <= maximum;
    private static bool HeaderValid(BossEncounterData data) => data.SceneHash != 0 && data.Term != 0 && data.SpawnKey != 0;
    private static void WriteHeader(BinaryWriter writer, BossEncounterData data)
    {
        writer.Write(data.SceneHash); writer.Write(data.Term); writer.Write((ushort)1); writer.Write(data.Sequence); writer.Write(data.SpawnKey);
    }
    private static void ReadHeader(BinaryReader reader, BossEncounterData data)
    {
        data.SceneHash = reader.ReadInt32(); data.Term = reader.ReadUInt32();
        if (reader.ReadUInt16() != 1) throw new IOException("Invalid boss generation");
        data.Sequence = reader.ReadUInt32(); data.SpawnKey = reader.ReadInt32();
    }
    public static byte[] EncodeEncounter(BossEncounterData data)
    {
        if (!HeaderValid(data) || (byte)data.State > 2) throw new ArgumentException("Invalid encounter");
        using (var stream = new MemoryStream()) using (var writer = new BinaryWriter(stream))
        { WriteHeader(writer, data); writer.Write((byte)data.State); return stream.ToArray(); }
    }
    public static bool TryReadEncounter(byte[] bytes, int offset, out BossEncounterData data)
    {
        data = null;
        if (bytes == null || offset < 0 || bytes.Length - offset != 19) return false;
        try
        {
            using (var stream = new MemoryStream(bytes, offset, bytes.Length - offset)) using (var reader = new BinaryReader(stream))
            {
                var result = new BossEncounterData(); ReadHeader(reader, result); result.State = (BossEncounterState)reader.ReadByte();
                if (!HeaderValid(result) || (byte)result.State > 2) return false;
                data = result; return true;
            }
        }
        catch (IOException) { return false; }
    }
    private static bool HazardValid(BossHazardData data)
    {
        return HeaderValid(data) && data.Id != 0 && (byte)data.Kind <= 1 && data.PrefabIndex >= -1 && data.PrefabIndex < 64
            && data.Stage <= 2 && Bounded(data.X, -1000000, 1000000) && Bounded(data.Y, -1000000, 1000000)
            && Bounded(data.Z, -1000000, 1000000) && Bounded(data.Angle, -3600, 3600)
            && Bounded(data.Strength, 0, 10000) && Bounded(data.Damage, 0, 10000)
            && Bounded(data.Preparation, 0, MaxSeconds) && Bounded(data.Loop, 0, MaxSeconds)
            && Bounded(data.Timer, 0, MaxSeconds + 1) && Bounded(data.HostTime, 0, 1000000000)
            && Bounded(data.AnimationTime, 0, 1000000) && Bounded(data.AnimationLength, 0, MaxSeconds)
            && data.SortingOrder >= -32768 && data.SortingOrder <= 32767;
    }
    public static byte[] EncodeHazard(BossHazardData data)
    {
        if (!HazardValid(data)) throw new ArgumentException("Invalid boss hazard");
        using (var stream = new MemoryStream()) using (var writer = new BinaryWriter(stream))
        {
            WriteHeader(writer, data); writer.Write(data.Id); writer.Write((byte)(data.Active ? 1 : 0));
            writer.Write((byte)data.Kind); writer.Write(data.PrefabIndex); writer.Write(data.Stage);
            writer.Write(data.X); writer.Write(data.Y); writer.Write(data.Z); writer.Write(data.Angle);
            writer.Write(data.Strength); writer.Write(data.Damage); writer.Write(data.Preparation); writer.Write(data.Loop);
            writer.Write(data.Timer); writer.Write(data.HostTime); writer.Write(data.AnimationHash);
            writer.Write(data.AnimationTime); writer.Write(data.AnimationLength); writer.Write((byte)(data.FlipX ? 1 : 0));
            writer.Write(data.SortingOrder); return stream.ToArray();
        }
    }
    public static bool TryReadHazard(byte[] bytes, int offset, out BossHazardData data)
    {
        data = null;
        if (bytes == null || offset < 0 || bytes.Length - offset != 88) return false;
        try
        {
            using (var stream = new MemoryStream(bytes, offset, bytes.Length - offset)) using (var reader = new BinaryReader(stream))
            {
                var result = new BossHazardData(); ReadHeader(reader, result); result.Id = reader.ReadUInt64();
                byte active = reader.ReadByte(); result.Active = active == 1; result.Kind = (WardenAttackKind)reader.ReadByte();
                result.PrefabIndex = reader.ReadInt16(); result.Stage = reader.ReadByte();
                result.X = reader.ReadSingle(); result.Y = reader.ReadSingle(); result.Z = reader.ReadSingle(); result.Angle = reader.ReadSingle();
                result.Strength = reader.ReadSingle(); result.Damage = reader.ReadSingle(); result.Preparation = reader.ReadSingle(); result.Loop = reader.ReadSingle();
                result.Timer = reader.ReadSingle(); result.HostTime = reader.ReadSingle(); result.AnimationHash = reader.ReadInt32();
                result.AnimationTime = reader.ReadSingle(); result.AnimationLength = reader.ReadSingle();
                byte flip = reader.ReadByte(); result.FlipX = flip == 1; result.SortingOrder = reader.ReadInt32();
                if (active > 1 || flip > 1 || !HazardValid(result)) return false;
                data = result; return true;
            }
        }
        catch (IOException) { return false; }
    }
    public static byte[] RebaseForReplay(byte[] body, uint term)
    {
        byte[] copy = (byte[])body.Clone();
        Array.Copy(BitConverter.GetBytes(term), 0, copy, 4, 4);
        Array.Copy(BitConverter.GetBytes(0u), 0, copy, 10, 4);
        return copy;
    }

    // Cache lifetimes use remaining native state time. Ten seconds is a bounded
    // allowance for an ending clip whose length is not known before it starts.
    public static float HazardReplayLifetime(BossHazardData data)
    {
        if (data.Stage == 0) return Math.Max(0, data.Preparation - data.Timer) + data.Loop + 10f;
        if (data.Stage == 1) return Math.Max(0, data.Loop - data.Timer) + 10f;
        return Math.Max(0, data.AnimationLength * (1f - Math.Min(1f, data.AnimationTime))) + 0.25f;
    }

    public static bool TryBuildHazardReplay(byte[] body, uint term, float receiptAge, out byte[] replay)
    {
        replay = null;
        if (term == 0 || !Finite(receiptAge) || receiptAge < 0 || !TryReadHazard(body, 0, out var data)
            || !data.Active || receiptAge > HazardReplayLifetime(data)) return false;
        data.Term = term; data.Sequence = 0;
        // Sequence zero identifies cached replay. Its clock field is receipt
        // age, not an absolute time from the previous authority's Unity clock.
        data.HostTime = receiptAge;
        replay = EncodeHazard(data);
        return true;
    }
}

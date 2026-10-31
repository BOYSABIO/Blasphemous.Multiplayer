using System;
using System.IO;

namespace Blasphemous.Multiplayer.Common.Combat;

/// <summary>The transferable part of a game Hit. Unity objects and callbacks stay local.</summary>
public sealed class EnemyHitData
{
    public float Damage;
    public float Force;
    public int DamageType;
    public int DamageElement;
    public ushort Flags;
    public float SourceX;
    public float SourceY;
    public bool DirectDamage;

    public const ushort Unavoidable = 1;
    public const ushort Unparriable = 2;
    public const ushort Unblockable = 4;
    public const ushort ThrowbackByPosition = 8;
    public const ushort ForceGuardDirection = 16;
    public const ushort DestroysProjectiles = 32;
    public const ushort DontSpawnBlood = 64;
    public const ushort ForceGuardSlide = 128;
    public const ushort CheckGuardOrientation = 256;

    public bool IsValid => Finite(Damage) && Damage > 0f && Finite(Force)
        && Finite(SourceX) && Finite(SourceY) && (Flags & ~511) == 0
        && DamageType >= 0 && DamageType <= 32 && DamageElement >= 0 && DamageElement <= 32;

    public static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

    public void Write(BinaryWriter writer)
    {
        if (!IsValid) throw new ArgumentException("Invalid enemy hit");
        writer.Write(Damage);
        writer.Write(Force);
        writer.Write(DamageType);
        writer.Write(DamageElement);
        writer.Write(Flags);
        writer.Write(SourceX);
        writer.Write(SourceY);
        writer.Write(DirectDamage);
    }

    public static EnemyHitData Read(BinaryReader reader)
    {
        var hit = new EnemyHitData
        {
            Damage = reader.ReadSingle(), Force = reader.ReadSingle(),
            DamageType = reader.ReadInt32(), DamageElement = reader.ReadInt32(),
            Flags = reader.ReadUInt16(), SourceX = reader.ReadSingle(), SourceY = reader.ReadSingle(),
            DirectDamage = reader.ReadBoolean(),
        };
        if (!hit.IsValid) throw new InvalidDataException("Invalid enemy hit");
        return hit;
    }
}

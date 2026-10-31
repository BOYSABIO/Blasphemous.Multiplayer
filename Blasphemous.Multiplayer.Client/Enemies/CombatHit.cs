using Blasphemous.Multiplayer.Common.Combat;
using Gameplay.GameControllers.Entities;
using UnityEngine;

namespace Blasphemous.Multiplayer.Client.Enemies;

internal static class CombatHit
{
    public static EnemyHitData Capture(Hit hit)
    {
        Vector3 source = hit.AttackingEntity != null ? hit.AttackingEntity.transform.position : Vector3.zero;
        ushort flags = 0;
        if (hit.Unnavoidable) flags |= EnemyHitData.Unavoidable;
        if (hit.Unparriable) flags |= EnemyHitData.Unparriable;
        if (hit.Unblockable) flags |= EnemyHitData.Unblockable;
        if (hit.ThrowbackDirByOwnerPosition) flags |= EnemyHitData.ThrowbackByPosition;
        if (hit.ForceGuardSlideDirection) flags |= EnemyHitData.ForceGuardDirection;
        if (hit.DestroysProjectiles) flags |= EnemyHitData.DestroysProjectiles;
        if (hit.DontSpawnBlood) flags |= EnemyHitData.DontSpawnBlood;
        if (hit.forceGuardslide) flags |= EnemyHitData.ForceGuardSlide;
        if (hit.CheckOrientationsForGuardslide) flags |= EnemyHitData.CheckGuardOrientation;
        return new EnemyHitData
        {
            Damage = hit.DamageAmount, Force = hit.Force, Flags = flags,
            DamageType = (int)hit.DamageType, DamageElement = (int)hit.DamageElement,
            SourceX = source.x, SourceY = source.y,
        };
    }

    public static Hit Restore(EnemyHitData data, GameObject attacker) => new Hit
    {
        AttackingEntity = attacker, DamageAmount = data.Damage, Force = data.Force,
        DamageType = (DamageArea.DamageType)data.DamageType,
        DamageElement = (DamageArea.DamageElement)data.DamageElement,
        Unnavoidable = (data.Flags & EnemyHitData.Unavoidable) != 0,
        Unparriable = (data.Flags & EnemyHitData.Unparriable) != 0,
        Unblockable = (data.Flags & EnemyHitData.Unblockable) != 0,
        ThrowbackDirByOwnerPosition = (data.Flags & EnemyHitData.ThrowbackByPosition) != 0,
        ForceGuardSlideDirection = (data.Flags & EnemyHitData.ForceGuardDirection) != 0,
        DestroysProjectiles = (data.Flags & EnemyHitData.DestroysProjectiles) != 0,
        DontSpawnBlood = (data.Flags & EnemyHitData.DontSpawnBlood) != 0,
        forceGuardslide = (data.Flags & EnemyHitData.ForceGuardSlide) != 0,
        CheckOrientationsForGuardslide = (data.Flags & EnemyHitData.CheckGuardOrientation) != 0,
    };
}

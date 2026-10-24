namespace Blasphemous.Multiplayer.Common.Enums;

public enum NetworkType
{
    Position = 0,
    Animation = 1,
    Direction = 2,
    EnterScene = 3,
    LeaveScene = 4,
    Skin = 5,
    Team = 6,
    Connection = 7,
    Intro = 8,
    Progress = 9,
    Attack = 10,
    Effect = 11,
    Ping = 12,

    /// <summary>Full enemy state (join catch-up / first register).</summary>
    EnemySnapshot = 13,

    /// <summary>Delta enemy position + HP.</summary>
    EnemyState = 14,

    /// <summary>Client reports damage; only scene host applies it.</summary>
    EnemyDamage = 15,

    /// <summary>Host announces an enemy death.</summary>
    EnemyDeath = 16,

    /// <summary>Non-host asks the scene host to resend all snapshots.</summary>
    EnemySyncRequest = 17,
}

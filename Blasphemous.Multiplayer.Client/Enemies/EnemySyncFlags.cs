using System;

namespace Blasphemous.Multiplayer.Client.Enemies
{
    [Flags]
    public enum EnemySyncFlags : byte
    {
        None = 0,
        Alive = 1 << 0,
        FacingRight = 1 << 1,
    }
}

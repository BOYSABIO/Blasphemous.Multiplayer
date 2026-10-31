using System;

namespace Blasphemous.Multiplayer.Common.Networking
{
	[Flags]
	public enum EnemySyncFlags : byte
	{
		None = 0,
		Alive = 1,
		FacingRight = 2,
		Attacking = 4,
		Parryable = 8,
		Despawned = 0x10,
		Executable = 0x20
	}
}

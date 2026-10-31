using System;

namespace Blasphemous.Multiplayer.Common.Networking
{
	[Flags]
	public enum EnemyHitFlags : byte
	{
		None = 0,
		Unparriable = 1,
		Unblockable = 2,
		Unnavoidable = 4,
		ThrowbackByOwner = 8,
		ForceGuardslide = 0x10,
		CheckOrientationsForGuardslide = 0x20
	}
}

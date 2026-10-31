using Gameplay.GameControllers.Penitent.Attack;
using HarmonyLib;

namespace Blasphemous.Multiplayer.Client.Enemies;

[HarmonyPatch(typeof(PenitentSword), "OnEnemyDamaged")]
internal class PenitentSword_OnEnemyDamaged_Patch
{
	public static bool Prefix()
	{
		EnemyManager enemyManager = Main.Multiplayer?.EnemyManager;
		if (enemyManager != null)
		{
			return !enemyManager.ApplyingRemoteDamage;
		}
		return true;
	}
}

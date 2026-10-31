using Gameplay.GameControllers.Entities;

namespace Blasphemous.Multiplayer.Client.Enemies;

internal static class EnemyDamageHooks
{
	public static bool ShouldBlockLocalDamage(Enemy enemy, Hit hit)
	{
		EnemyManager enemyManager = Main.Multiplayer?.EnemyManager;
		if (enemyManager == null || enemyManager.ApplyingRemoteDamage)
		{
			return false;
		}
		if (enemy == null || !EnemyManager.IsSyncableEnemy(enemy))
		{
			return false;
		}
		if (!Main.Multiplayer.NetworkManager.IsConnected)
		{
			return false;
		}
		if (hit.DamageAmount <= 0f)
		{
			return false;
		}
		if (!EnemyManager.IsLocalPlayerAttack(hit))
		{
			return false;
		}
		if (!enemyManager.TryInterceptDamage(enemy, hit))
		{
			return false;
		}
		enemyManager.PlayLocalHitPresentation(enemy, hit);
		return true;
	}
}

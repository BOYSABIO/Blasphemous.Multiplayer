using Gameplay.GameControllers.Entities;
using HarmonyLib;

namespace Blasphemous.Multiplayer.Client.Enemies;

[HarmonyPatch(typeof(Enemy), "Execution")]
internal class Enemy_Execution_Patch
{
	public static bool Prefix(Enemy __instance)
	{
		EnemyManager enemyManager = Main.Multiplayer?.EnemyManager;
		if (enemyManager == null)
		{
			return true;
		}
		return !enemyManager.ShouldBlockEnemyExecution(__instance);
	}
}

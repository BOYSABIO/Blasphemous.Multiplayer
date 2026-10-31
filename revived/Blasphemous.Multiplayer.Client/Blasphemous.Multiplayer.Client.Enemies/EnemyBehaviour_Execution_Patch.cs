using Gameplay.GameControllers.Enemies.Framework.IA;
using Gameplay.GameControllers.Entities;
using HarmonyLib;

namespace Blasphemous.Multiplayer.Client.Enemies;

[HarmonyPatch(typeof(EnemyBehaviour), "Execution")]
internal class EnemyBehaviour_Execution_Patch
{
	public static void Postfix(EnemyBehaviour __instance)
	{
		EnemyManager enemyManager = Main.Multiplayer?.EnemyManager;
		if (enemyManager != null && enemyManager.IsSceneHost)
		{
			Enemy enemy = ((__instance != null) ? __instance.GetComponentInParent<Enemy>() : null);
			if (!(enemy == null))
			{
				enemyManager.MarkExecutable(enemy);
			}
		}
	}
}

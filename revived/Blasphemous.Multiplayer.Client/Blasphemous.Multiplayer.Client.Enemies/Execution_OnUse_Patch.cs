using Gameplay.GameControllers.Entities;
using HarmonyLib;
using Tools.Level.Interactables;

namespace Blasphemous.Multiplayer.Client.Enemies;

[HarmonyPatch(typeof(Execution), "OnUse")]
internal class Execution_OnUse_Patch
{
	public static void Prefix(Execution __instance)
	{
		EnemyManager enemyManager = Main.Multiplayer?.EnemyManager;
		if (enemyManager == null || !Main.Multiplayer.NetworkManager.IsConnected)
		{
			return;
		}
		Enemy enemy = null;
		try
		{
			enemy = __instance.ExecutedEntity;
		}
		catch
		{
		}
		if (enemy == null)
		{
			enemy = __instance.GetComponentInParent<Enemy>();
		}
		if (!(enemy == null))
		{
			if (enemyManager.IsSceneHost)
			{
				enemyManager.NotifyHostLocalExecution(enemy);
			}
			else
			{
				enemyManager.NotifyLocalExecution(enemy);
			}
		}
	}
}

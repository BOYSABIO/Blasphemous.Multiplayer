using HarmonyLib;
using Tools.Level.Layout;

namespace Blasphemous.Multiplayer.Client.Enemies;

[HarmonyPatch(typeof(EnemySpawnPoint), "CreateEnemy")]
internal class Enemy_SpawnPoint_Create_Patch
{
	public static void Postfix()
	{
		Main.Multiplayer?.EnemyManager?.ForceRescan();
	}
}

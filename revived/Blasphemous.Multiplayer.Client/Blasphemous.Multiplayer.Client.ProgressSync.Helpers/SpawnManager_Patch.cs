using Blasphemous.Multiplayer.Common.Enums;
using Framework.Managers;
using HarmonyLib;

namespace Blasphemous.Multiplayer.Client.ProgressSync.Helpers;

[HarmonyPatch(typeof(SpawnManager), "SetTeleportActive")]
internal class SpawnManager_Patch
{
	public static void Postfix(string teleportId, bool active)
	{
		if (!Main.Multiplayer.ProgressManager.CurrentlyUpdatingProgress && active)
		{
			ProgressUpdate progress = new ProgressUpdate(teleportId, ProgressType.Teleport, 0);
			Main.Multiplayer.NetworkManager.SendProgress(progress);
		}
	}
}

using System.Collections.Generic;
using Blasphemous.Multiplayer.Common.Enums;
using Framework.FrameworkCore;
using Framework.Managers;
using HarmonyLib;

namespace Blasphemous.Multiplayer.Client.ProgressSync.Helpers;

[HarmonyPatch(typeof(SpawnManager), "GetCurrentPersistentState")]
internal class SpawnManagerIntro_Patch
{
	public static bool Prefix(string dataPath, Dictionary<string, TeleportDestination> ___TeleportDict)
	{
		if (dataPath != "intro")
		{
			return true;
		}
		foreach (string key in ___TeleportDict.Keys)
		{
			if (___TeleportDict[key].isActive)
			{
				ProgressUpdate progress = new ProgressUpdate(key, ProgressType.Teleport, 0);
				Main.Multiplayer.NetworkManager.SendProgress(progress);
			}
		}
		return false;
	}
}

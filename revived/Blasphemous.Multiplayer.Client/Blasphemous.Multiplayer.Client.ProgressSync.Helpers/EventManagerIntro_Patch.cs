using System.Collections.Generic;
using Blasphemous.Multiplayer.Client.Data;
using Blasphemous.Multiplayer.Common.Enums;
using Framework.Managers;
using HarmonyLib;

namespace Blasphemous.Multiplayer.Client.ProgressSync.Helpers;

[HarmonyPatch(typeof(EventManager), "GetCurrentPersistentState")]
internal class EventManagerIntro_Patch
{
	public static bool Prefix(string dataPath, Dictionary<string, FlagObject> ___flags, List<string> ___MiriamClosedPortals)
	{
		if (dataPath != "intro")
		{
			return true;
		}
		foreach (string key in ___flags.Keys)
		{
			if (FlagStates.getFlagState(key) != null && ___flags[key].value)
			{
				ProgressUpdate progress = new ProgressUpdate(key, ProgressType.Flag, 0);
				Main.Multiplayer.NetworkManager.SendProgress(progress);
			}
		}
		foreach (string ___MiriamClosedPortal in ___MiriamClosedPortals)
		{
			ProgressUpdate progress2 = new ProgressUpdate(___MiriamClosedPortal, ProgressType.MiriamStatus, 0);
			Main.Multiplayer.NetworkManager.SendProgress(progress2);
		}
		return false;
	}
}

using System.Collections.Generic;
using Framework.Managers;
using HarmonyLib;

namespace Blasphemous.Multiplayer.Client.ProgressSync.Helpers;

[HarmonyPatch(typeof(EventManager), "SetCurrentPersistentState")]
internal class EventManagerMiriamReceive_Patch
{
	public static bool Prefix(PersistentManager.PersistentData data, string dataPath, List<string> ___MiriamClosedPortals)
	{
		if (data == null)
		{
			if (!___MiriamClosedPortals.Contains(dataPath))
			{
				___MiriamClosedPortals.Add(dataPath);
			}
			return false;
		}
		return true;
	}
}

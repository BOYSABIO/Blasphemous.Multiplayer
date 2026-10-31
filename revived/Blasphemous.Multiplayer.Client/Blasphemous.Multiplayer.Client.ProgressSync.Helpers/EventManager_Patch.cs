using Blasphemous.Multiplayer.Client.Data;
using Blasphemous.Multiplayer.Common.Enums;
using Framework.Managers;
using HarmonyLib;

namespace Blasphemous.Multiplayer.Client.ProgressSync.Helpers;

[HarmonyPatch(typeof(EventManager), "SetFlag")]
internal class EventManager_Patch
{
	public static void Postfix(EventManager __instance, string id, bool b)
	{
		if (!Main.Multiplayer.ProgressManager.CurrentlyUpdatingProgress)
		{
			string formattedId = __instance.GetFormattedId(id);
			if (FlagStates.getFlagState(formattedId) != null)
			{
				ProgressUpdate progress = new ProgressUpdate(formattedId, ProgressType.Flag, (!b) ? ((byte)1) : ((byte)0));
				Main.Multiplayer.NetworkManager.SendProgress(progress);
			}
		}
	}
}

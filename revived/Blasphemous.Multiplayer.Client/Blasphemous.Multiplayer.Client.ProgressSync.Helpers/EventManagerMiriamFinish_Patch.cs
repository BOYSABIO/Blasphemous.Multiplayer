using Blasphemous.Multiplayer.Common.Enums;
using Framework.Managers;
using HarmonyLib;

namespace Blasphemous.Multiplayer.Client.ProgressSync.Helpers;

[HarmonyPatch(typeof(EventManager), "FinishMiriamQuest")]
internal class EventManagerMiriamFinish_Patch
{
	public static void Postfix(bool __result)
	{
		if (!Main.Multiplayer.ProgressManager.CurrentlyUpdatingProgress && __result)
		{
			ProgressUpdate progress = new ProgressUpdate("FINISH", ProgressType.MiriamStatus, 0);
			Main.Multiplayer.NetworkManager.SendProgress(progress);
		}
	}
}

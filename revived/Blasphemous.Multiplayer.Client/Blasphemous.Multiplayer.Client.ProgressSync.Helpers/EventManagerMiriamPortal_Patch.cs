using Blasphemous.Multiplayer.Common.Enums;
using Framework.Managers;
using HarmonyLib;

namespace Blasphemous.Multiplayer.Client.ProgressSync.Helpers;

[HarmonyPatch(typeof(EventManager), "EndMiriamPortalAndReturn")]
internal class EventManagerMiriamPortal_Patch
{
	public static void Prefix(EventManager __instance)
	{
		if (!Main.Multiplayer.ProgressManager.CurrentlyUpdatingProgress && __instance.AreInMiriamLevel())
		{
			ProgressUpdate progress = new ProgressUpdate(__instance.MiriamCurrentScenePortal, ProgressType.MiriamStatus, 0);
			Main.Multiplayer.NetworkManager.SendProgress(progress);
		}
	}
}

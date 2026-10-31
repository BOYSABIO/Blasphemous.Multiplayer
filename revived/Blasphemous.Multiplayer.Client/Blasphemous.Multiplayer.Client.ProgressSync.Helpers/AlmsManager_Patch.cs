using Blasphemous.Multiplayer.Common.Enums;
using Framework.Managers;
using HarmonyLib;

namespace Blasphemous.Multiplayer.Client.ProgressSync.Helpers;

[HarmonyPatch(typeof(AlmsManager), "ConsumeTears")]
internal class AlmsManager_Patch
{
	public static void Postfix(AlmsManager __instance)
	{
		if (!Main.Multiplayer.ProgressManager.CurrentlyUpdatingProgress)
		{
			ProgressUpdate progress = new ProgressUpdate("Tears", ProgressType.ChurchDonation, (byte)(__instance.TearsGiven / 1000));
			Main.Multiplayer.NetworkManager.SendProgress(progress);
		}
	}
}

using Framework.Managers;
using HarmonyLib;
using Tools.Level.Interactables;
using UnityEngine;

namespace Blasphemous.Multiplayer.Client.ProgressSync.Helpers.PersistentObjects;

[HarmonyPatch(typeof(Door), "SetCurrentPersistentState")]
internal class DoorLoad_Patch
{
	public static bool Prefix(PersistentManager.PersistentData data, Door __instance, Animator ___interactableAnimator, ref bool ___objectUsed)
	{
		if (data != null)
		{
			return !Main.Multiplayer.ProgressManager.IsObjectInteracted(__instance.GetPersistenID());
		}
		___objectUsed = true;
		__instance.Closed = false;
		___interactableAnimator.SetTrigger("INSTA_OPEN");
		return false;
	}
}

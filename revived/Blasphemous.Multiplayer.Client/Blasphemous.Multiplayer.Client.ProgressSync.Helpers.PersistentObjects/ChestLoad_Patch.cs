using Framework.Managers;
using HarmonyLib;
using Tools.Level.Interactables;
using UnityEngine;

namespace Blasphemous.Multiplayer.Client.ProgressSync.Helpers.PersistentObjects;

[HarmonyPatch(typeof(Chest), "SetCurrentPersistentState")]
internal class ChestLoad_Patch
{
	public static bool Prefix(PersistentManager.PersistentData data, Chest __instance, Animator ___interactableAnimator)
	{
		if (data != null)
		{
			return !Main.Multiplayer.ProgressManager.IsObjectInteracted(__instance.GetPersistenID());
		}
		__instance.Consumed = true;
		___interactableAnimator.SetBool("NOANIMUSED", value: true);
		return false;
	}
}

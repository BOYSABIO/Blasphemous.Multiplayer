using Framework.Managers;
using HarmonyLib;
using UnityEngine;

namespace Blasphemous.Multiplayer.Client.ProgressSync.Helpers.PersistentObjects;

[HarmonyPatch(typeof(CollectibleItem), "SetCurrentPersistentState")]
internal class CollectibleItemLoad_Patch
{
	public static bool Prefix(PersistentManager.PersistentData data, CollectibleItem __instance, Animator ___interactableAnimator)
	{
		if (data != null)
		{
			return !Main.Multiplayer.ProgressManager.IsObjectInteracted(__instance.GetPersistenID());
		}
		__instance.Consumed = true;
		___interactableAnimator.gameObject.SetActive(value: false);
		return false;
	}
}

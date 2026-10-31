using HarmonyLib;
using UnityEngine;

namespace Blasphemous.Multiplayer.Client.ProgressSync.Helpers.PersistentObjects;

[HarmonyPatch(typeof(CollectibleItem), "GetCurrentPersistentState")]
internal class CollectibleItemReceive_Patch
{
	public static bool Prefix(string dataPath, CollectibleItem __instance, Animator ___interactableAnimator)
	{
		if (dataPath != "use")
		{
			return true;
		}
		__instance.Consumed = true;
		___interactableAnimator.gameObject.SetActive(value: false);
		return false;
	}
}

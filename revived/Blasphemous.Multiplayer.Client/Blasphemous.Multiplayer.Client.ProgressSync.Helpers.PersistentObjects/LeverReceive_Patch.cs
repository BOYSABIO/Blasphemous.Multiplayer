using HarmonyLib;
using Tools.Level.Interactables;
using UnityEngine;

namespace Blasphemous.Multiplayer.Client.ProgressSync.Helpers.PersistentObjects;

[HarmonyPatch(typeof(Lever), "GetCurrentPersistentState")]
internal class LeverReceive_Patch
{
	public static bool Prefix(string dataPath, Lever __instance, Animator ___interactableAnimator)
	{
		if (dataPath != "use")
		{
			return true;
		}
		__instance.Consumed = true;
		___interactableAnimator.SetBool("ACTIVE", value: true);
		return false;
	}
}

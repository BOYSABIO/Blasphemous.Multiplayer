using HarmonyLib;
using Tools.Level.Interactables;
using UnityEngine;

namespace Blasphemous.Multiplayer.Client.ProgressSync.Helpers.PersistentObjects;

[HarmonyPatch(typeof(Chest), "GetCurrentPersistentState")]
internal class ChestReceive_Patch
{
	public static bool Prefix(string dataPath, Chest __instance, Animator ___interactableAnimator)
	{
		if (dataPath != "use")
		{
			return true;
		}
		__instance.Consumed = true;
		___interactableAnimator.SetBool("USED", value: true);
		return false;
	}
}

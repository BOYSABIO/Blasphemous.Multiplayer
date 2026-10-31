using Framework.Managers;
using HarmonyLib;
using Tools.Level.Actionables;
using UnityEngine;

namespace Blasphemous.Multiplayer.Client.ProgressSync.Helpers.PersistentObjects;

[HarmonyPatch(typeof(Gate), "SetCurrentPersistentState")]
internal class GateLoad_Patch
{
	public static bool Prefix(PersistentManager.PersistentData data, Gate __instance, ref bool ___open, Animator ___animator, Collider2D ___collision)
	{
		if (data != null)
		{
			return !Main.Multiplayer.ProgressManager.IsObjectInteracted(__instance.GetPersistenID());
		}
		___open = true;
		___collision.enabled = false;
		___animator.SetBool("INSTA_ACTION", value: true);
		___animator.SetBool("OPEN", value: true);
		return false;
	}
}

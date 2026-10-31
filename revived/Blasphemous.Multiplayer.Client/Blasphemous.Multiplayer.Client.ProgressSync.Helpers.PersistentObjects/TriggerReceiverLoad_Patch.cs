using Framework.Managers;
using HarmonyLib;
using Tools.Level.Actionables;
using UnityEngine;

namespace Blasphemous.Multiplayer.Client.ProgressSync.Helpers.PersistentObjects;

[HarmonyPatch(typeof(TriggerReceiver), "SetCurrentPersistentState")]
internal class TriggerReceiverLoad_Patch
{
	public static bool Prefix(PersistentManager.PersistentData data, TriggerReceiver __instance, ref bool ___alreadyUsed)
	{
		if (data != null)
		{
			return !Main.Multiplayer.ProgressManager.IsObjectInteracted(__instance.GetPersistenID());
		}
		___alreadyUsed = true;
		__instance.animator.Play("USED");
		Collider2D component = __instance.GetComponent<Collider2D>();
		if (component != null)
		{
			component.enabled = false;
		}
		return false;
	}
}

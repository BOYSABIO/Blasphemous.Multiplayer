using HarmonyLib;
using Tools.Level.Actionables;
using UnityEngine;

namespace Blasphemous.Multiplayer.Client.ProgressSync.Helpers.PersistentObjects;

[HarmonyPatch(typeof(TriggerReceiver), "GetCurrentPersistentState")]
internal class TriggerReceiverReceive_Patch
{
	public static bool Prefix(string dataPath, TriggerReceiver __instance, ref bool ___alreadyUsed)
	{
		if (dataPath != "use")
		{
			return true;
		}
		___alreadyUsed = true;
		__instance.animator.SetTrigger("ACTIVATE");
		Collider2D component = __instance.GetComponent<Collider2D>();
		if (component != null)
		{
			component.enabled = false;
		}
		return false;
	}
}

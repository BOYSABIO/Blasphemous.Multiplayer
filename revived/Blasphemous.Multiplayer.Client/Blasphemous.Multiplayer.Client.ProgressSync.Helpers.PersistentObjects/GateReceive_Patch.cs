using HarmonyLib;
using Tools.Level.Actionables;
using UnityEngine;

namespace Blasphemous.Multiplayer.Client.ProgressSync.Helpers.PersistentObjects;

[HarmonyPatch(typeof(Gate), "GetCurrentPersistentState")]
internal class GateReceive_Patch
{
	public static bool Prefix(string dataPath, ref bool ___open, Animator ___animator, Collider2D ___collision)
	{
		if (dataPath != "use")
		{
			return true;
		}
		___open = true;
		___collision.enabled = false;
		___animator.SetBool("INSTA_ACTION", value: false);
		___animator.SetBool("OPEN", value: true);
		return false;
	}
}

using Gameplay.GameControllers.AnimationBehaviours.Player.Dash;
using HarmonyLib;
using UnityEngine;

namespace Blasphemous.Multiplayer.Client;

[HarmonyPatch(typeof(RunAfterDashBehaviour), "OnStateUpdate")]
internal class RunAfterDashBehaviourUpdate_Patch
{
	public static bool Prefix(Animator animator)
	{
		return animator.name == "Body";
	}
}

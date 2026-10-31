using Gameplay.GameControllers.AnimationBehaviours.Player.Jump;
using HarmonyLib;
using UnityEngine;

namespace Blasphemous.Multiplayer.Client;

[HarmonyPatch(typeof(JumpForwardBehaviour), "OnStateExit")]
internal class JumpForwardBehaviourExit_Patch
{
	public static bool Prefix(Animator animator)
	{
		return animator.name == "Body";
	}
}

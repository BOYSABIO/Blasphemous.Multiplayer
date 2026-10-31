using Gameplay.GameControllers.AnimationBehaviours.Player.Jump;
using HarmonyLib;
using UnityEngine;

namespace Blasphemous.Multiplayer.Client;

[HarmonyPatch(typeof(FallingForwardBehaviour), "OnStateUpdate")]
internal class FallingForwardBehaviourUpdate_Patch
{
	public static bool Prefix(Animator animator)
	{
		return animator.name == "Body";
	}
}

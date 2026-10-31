using Gameplay.GameControllers.AnimationBehaviours.Player.ClimbLadder;
using HarmonyLib;
using UnityEngine;

namespace Blasphemous.Multiplayer.Client;

[HarmonyPatch(typeof(LadderSlidingBehaviour), "OnStateUpdate")]
internal class LadderSlidingBehaviourUpdate_Patch
{
	public static bool Prefix(Animator animator)
	{
		return animator.name == "Body";
	}
}

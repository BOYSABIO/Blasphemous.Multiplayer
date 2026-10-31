using Gameplay.GameControllers.AnimationBehaviours.Player.ClimbLadder;
using HarmonyLib;
using UnityEngine;

namespace Blasphemous.Multiplayer.Client;

[HarmonyPatch(typeof(LadderSlidingBehaviour), "OnStateExit")]
internal class LadderSlidingBehaviourExit_Patch
{
	public static bool Prefix(Animator animator)
	{
		return animator.name == "Body";
	}
}

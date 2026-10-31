using Gameplay.GameControllers.AnimationBehaviours.Player.ClimbClifLede;
using HarmonyLib;
using UnityEngine;

namespace Blasphemous.Multiplayer.Client;

[HarmonyPatch(typeof(ClimbCliffLedeBehaviour), "OnStateExit")]
internal class ClimbCliffLedeBehaviourExit_Patch
{
	public static bool Prefix(Animator animator)
	{
		return animator.name == "Body";
	}
}

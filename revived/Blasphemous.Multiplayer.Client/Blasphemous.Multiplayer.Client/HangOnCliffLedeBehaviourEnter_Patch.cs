using Gameplay.GameControllers.AnimationBehaviours.Player.ClimbClifLede;
using HarmonyLib;
using UnityEngine;

namespace Blasphemous.Multiplayer.Client;

[HarmonyPatch(typeof(HangOnCliffLedeBehaviour), "OnStateEnter")]
internal class HangOnCliffLedeBehaviourEnter_Patch
{
	public static bool Prefix(Animator animator)
	{
		return animator.name == "Body";
	}
}

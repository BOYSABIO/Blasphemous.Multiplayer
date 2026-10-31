using Gameplay.GameControllers.AnimationBehaviours.Player.RangeAttack;
using HarmonyLib;
using UnityEngine;

namespace Blasphemous.Multiplayer.Client;

[HarmonyPatch(typeof(MidAirRangeAttackBehaviour), "OnStateExit")]
internal class MidAirRangeAttackBehaviourExit_Patch
{
	public static bool Prefix(Animator animator)
	{
		return animator.name == "Body";
	}
}

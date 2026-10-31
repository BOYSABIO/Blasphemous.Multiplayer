using Gameplay.GameControllers.AnimationBehaviours.Player.SubStatesBehaviours;
using HarmonyLib;
using UnityEngine;

namespace Blasphemous.Multiplayer.Client;

[HarmonyPatch(typeof(GroundAttackSubStateBehaviour), "OnStateExit")]
internal class GroundAttackSubStateBehaviourExit_Patch
{
	public static bool Prefix(Animator animator)
	{
		return animator.name == "Body";
	}
}

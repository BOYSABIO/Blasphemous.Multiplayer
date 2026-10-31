using Gameplay.GameControllers.AnimationBehaviours.Player.Attack;
using HarmonyLib;
using UnityEngine;

namespace Blasphemous.Multiplayer.Client;

[HarmonyPatch(typeof(VerticalAttackLandingBehaviour), "OnStateUpdate")]
internal class VerticalAttackLandingBehaviourUpdate_Patch
{
	public static bool Prefix(Animator animator)
	{
		return animator.name == "Body";
	}
}

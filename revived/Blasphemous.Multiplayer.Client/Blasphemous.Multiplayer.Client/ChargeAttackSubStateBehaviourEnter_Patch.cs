using Gameplay.GameControllers.AnimationBehaviours.Player.SubStatesBehaviours;
using HarmonyLib;
using UnityEngine;

namespace Blasphemous.Multiplayer.Client;

[HarmonyPatch(typeof(ChargeAttackSubStateBehaviour), "OnStateEnter")]
internal class ChargeAttackSubStateBehaviourEnter_Patch
{
	public static bool Prefix(Animator animator)
	{
		return animator.name == "Body";
	}
}

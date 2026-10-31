using Gameplay.GameControllers.AnimationBehaviours.Player.Dead;
using HarmonyLib;
using UnityEngine;

namespace Blasphemous.Multiplayer.Client;

[HarmonyPatch(typeof(PlayerDeathAnimationBehaviour), "OnStateEnter")]
internal class PlayerDeathAnimationBehaviourEnter_Patch
{
	public static bool Prefix(Animator animator)
	{
		return animator.name == "Body";
	}
}

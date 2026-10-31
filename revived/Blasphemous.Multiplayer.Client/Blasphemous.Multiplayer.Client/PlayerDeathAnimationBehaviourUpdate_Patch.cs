using Gameplay.GameControllers.AnimationBehaviours.Player.Dead;
using HarmonyLib;
using UnityEngine;

namespace Blasphemous.Multiplayer.Client;

[HarmonyPatch(typeof(PlayerDeathAnimationBehaviour), "OnStateUpdate")]
internal class PlayerDeathAnimationBehaviourUpdate_Patch
{
	public static bool Prefix(Animator animator)
	{
		return animator.name == "Body";
	}
}

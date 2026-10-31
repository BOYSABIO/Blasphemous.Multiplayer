using Gameplay.GameControllers.AnimationBehaviours.Player.Dead;
using HarmonyLib;
using UnityEngine;

namespace Blasphemous.Multiplayer.Client;

[HarmonyPatch(typeof(PlayerDeathFallBehaviour), "OnStateEnter")]
internal class PlayerDeathFallBehaviourEnter_Patch
{
	public static bool Prefix(Animator animator)
	{
		return animator.name == "Body";
	}
}

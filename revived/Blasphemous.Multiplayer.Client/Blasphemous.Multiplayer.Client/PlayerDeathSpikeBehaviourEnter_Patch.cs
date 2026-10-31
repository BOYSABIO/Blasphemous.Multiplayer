using Gameplay.GameControllers.AnimationBehaviours.Player.Dead;
using HarmonyLib;
using UnityEngine;

namespace Blasphemous.Multiplayer.Client;

[HarmonyPatch(typeof(PlayerDeathSpikeBehaviour), "OnStateEnter")]
internal class PlayerDeathSpikeBehaviourEnter_Patch
{
	public static bool Prefix(Animator animator)
	{
		return animator.name == "Body";
	}
}

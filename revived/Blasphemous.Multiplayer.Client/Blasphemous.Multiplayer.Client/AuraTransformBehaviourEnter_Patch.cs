using Gameplay.GameControllers.AnimationBehaviours.Player.Prayer;
using HarmonyLib;
using UnityEngine;

namespace Blasphemous.Multiplayer.Client;

[HarmonyPatch(typeof(AuraTransformBehaviour), "OnStateEnter")]
internal class AuraTransformBehaviourEnter_Patch
{
	public static bool Prefix(Animator animator)
	{
		return animator.name == "Body";
	}
}

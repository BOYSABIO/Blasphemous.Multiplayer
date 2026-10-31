using Gameplay.GameControllers.AnimationBehaviours.Player.Hurt;
using HarmonyLib;
using UnityEngine;

namespace Blasphemous.Multiplayer.Client;

[HarmonyPatch(typeof(HealingBehaviour), "OnStateEnter")]
internal class HealingBehaviourEnter_Patch
{
	public static bool Prefix(Animator animator)
	{
		return animator.name == "Body";
	}
}

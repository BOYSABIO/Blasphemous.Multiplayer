using Gameplay.GameControllers.AnimationBehaviours.Player.Jump;
using HarmonyLib;
using UnityEngine;

namespace Blasphemous.Multiplayer.Client;

[HarmonyPatch(typeof(FallingBehaviour), "OnStateEnter")]
internal class FallingBehaviourEnter_Patch
{
	public static bool Prefix(Animator animator)
	{
		return animator.name == "Body";
	}
}

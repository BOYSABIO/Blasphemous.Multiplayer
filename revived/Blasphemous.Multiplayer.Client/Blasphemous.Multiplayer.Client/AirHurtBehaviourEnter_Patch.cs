using Gameplay.GameControllers.AnimationBehaviours.Player.Hurt;
using HarmonyLib;
using UnityEngine;

namespace Blasphemous.Multiplayer.Client;

[HarmonyPatch(typeof(AirHurtBehaviour), "OnStateEnter")]
internal class AirHurtBehaviourEnter_Patch
{
	public static bool Prefix(Animator animator)
	{
		return animator.name == "Body";
	}
}

using Gameplay.GameControllers.AnimationBehaviours.Player.ClimbClifLede;
using HarmonyLib;
using UnityEngine;

namespace Blasphemous.Multiplayer.Client;

[HarmonyPatch(typeof(ClimbCliffLedeBehaviour), "OnStateUpdate")]
internal class ClimbCliffLedeBehaviourUpdate_Patch
{
	public static bool Prefix(Animator animator)
	{
		return animator.name == "Body";
	}
}

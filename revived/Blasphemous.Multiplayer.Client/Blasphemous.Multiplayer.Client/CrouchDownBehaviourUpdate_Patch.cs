using Gameplay.GameControllers.AnimationBehaviours.Player.Crouch;
using HarmonyLib;
using UnityEngine;

namespace Blasphemous.Multiplayer.Client;

[HarmonyPatch(typeof(CrouchDownBehaviour), "OnStateUpdate")]
internal class CrouchDownBehaviourUpdate_Patch
{
	public static bool Prefix(Animator animator)
	{
		return animator.name == "Body";
	}
}

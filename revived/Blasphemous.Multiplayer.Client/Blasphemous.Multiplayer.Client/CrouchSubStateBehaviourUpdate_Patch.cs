using Gameplay.GameControllers.AnimationBehaviours.Player.SubStatesBehaviours;
using HarmonyLib;
using UnityEngine;

namespace Blasphemous.Multiplayer.Client;

[HarmonyPatch(typeof(CrouchSubStateBehaviour), "OnStateUpdate")]
internal class CrouchSubStateBehaviourUpdate_Patch
{
	public static bool Prefix(Animator animator)
	{
		return animator.name == "Body";
	}
}

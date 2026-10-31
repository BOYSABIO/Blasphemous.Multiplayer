using Gameplay.GameControllers.AnimationBehaviours.Player.Jump;
using HarmonyLib;
using UnityEngine;

namespace Blasphemous.Multiplayer.Client;

[HarmonyPatch(typeof(WallJumpContactBehaviour), "OnStateEnter")]
internal class WallJumpContactBehaviourEnter_Patch
{
	public static bool Prefix(Animator animator)
	{
		return animator.name == "Body";
	}
}

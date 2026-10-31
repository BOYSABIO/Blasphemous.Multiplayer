using Gameplay.GameControllers.AnimationBehaviours.Player.Attack;
using HarmonyLib;
using UnityEngine;

namespace Blasphemous.Multiplayer.Client;

[HarmonyPatch(typeof(DefaultSwordSlashBehaviour), "OnStateEnter")]
internal class DefaultSwordSlashBehaviourEnter_Patch
{
	public static bool Prefix(Animator animator)
	{
		return animator.name == "Slash";
	}
}

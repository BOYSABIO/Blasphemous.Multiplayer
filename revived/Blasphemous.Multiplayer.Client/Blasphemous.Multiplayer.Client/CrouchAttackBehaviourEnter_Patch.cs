using Blasphemous.Multiplayer.Client.PvP.Models;
using Gameplay.GameControllers.AnimationBehaviours.Player.Crouch;
using HarmonyLib;
using UnityEngine;

namespace Blasphemous.Multiplayer.Client;

[HarmonyPatch(typeof(CrouchAttackBehaviour), "OnStateEnter")]
internal class CrouchAttackBehaviourEnter_Patch
{
	public static bool Prefix(Animator animator)
	{
		if (animator.name == "Body")
		{
			Main.Multiplayer.NetworkManager.SendEffect(EffectType.Crouch);
			return true;
		}
		return false;
	}
}

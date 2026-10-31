using Blasphemous.Multiplayer.Client.PvP.Models;
using Gameplay.GameControllers.AnimationBehaviours.Player.RangeAttack;
using HarmonyLib;
using UnityEngine;

namespace Blasphemous.Multiplayer.Client;

[HarmonyPatch(typeof(GroundRangeAttackBehaviour), "OnStateEnter")]
internal class GroundRangeAttackBehaviourEnter_Patch
{
	public static bool Prefix(Animator animator)
	{
		if (animator.name == "Body")
		{
			Main.Multiplayer.NetworkManager.SendEffect(EffectType.Ranged);
			return true;
		}
		return false;
	}
}

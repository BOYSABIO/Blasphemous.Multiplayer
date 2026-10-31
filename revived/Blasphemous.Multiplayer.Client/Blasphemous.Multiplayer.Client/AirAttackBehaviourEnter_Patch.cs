using Blasphemous.Multiplayer.Client.PvP.Models;
using Gameplay.GameControllers.AnimationBehaviours.Player.Attack;
using HarmonyLib;
using UnityEngine;

namespace Blasphemous.Multiplayer.Client;

[HarmonyPatch(typeof(AirAttackBehaviour), "OnStateEnter")]
internal class AirAttackBehaviourEnter_Patch
{
	public static bool Prefix(Animator animator)
	{
		if (animator.name == "Body")
		{
			Main.Multiplayer.NetworkManager.SendEffect(EffectType.SidewaysAir);
			return true;
		}
		return false;
	}
}

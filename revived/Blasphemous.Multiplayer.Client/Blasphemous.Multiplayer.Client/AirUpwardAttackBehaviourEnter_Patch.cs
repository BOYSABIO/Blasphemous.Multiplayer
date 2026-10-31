using Blasphemous.Multiplayer.Client.PvP.Models;
using Gameplay.GameControllers.AnimationBehaviours.Player.Attack;
using HarmonyLib;
using UnityEngine;

namespace Blasphemous.Multiplayer.Client;

[HarmonyPatch(typeof(AirUpwardAttackBehaviour), "OnStateEnter")]
internal class AirUpwardAttackBehaviourEnter_Patch
{
	public static bool Prefix(Animator animator)
	{
		if (animator.name == "Body")
		{
			Main.Multiplayer.NetworkManager.SendEffect(EffectType.UpwardsAir);
			return true;
		}
		return false;
	}
}

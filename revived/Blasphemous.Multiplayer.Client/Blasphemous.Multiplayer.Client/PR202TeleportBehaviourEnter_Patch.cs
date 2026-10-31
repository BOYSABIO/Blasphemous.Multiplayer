using Gameplay.GameControllers.AnimationBehaviours.Player.Prayer;
using HarmonyLib;
using UnityEngine;

namespace Blasphemous.Multiplayer.Client;

[HarmonyPatch(typeof(PR202TeleportBehaviour), "OnStateEnter")]
internal class PR202TeleportBehaviourEnter_Patch
{
	public static bool Prefix(Animator animator)
	{
		return animator.name == "Body";
	}
}

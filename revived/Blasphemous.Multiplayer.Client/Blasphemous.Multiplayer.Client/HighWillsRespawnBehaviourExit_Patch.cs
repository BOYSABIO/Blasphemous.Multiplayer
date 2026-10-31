using Gameplay.GameControllers.AnimationBehaviours.Player.Prayer;
using HarmonyLib;
using UnityEngine;

namespace Blasphemous.Multiplayer.Client;

[HarmonyPatch(typeof(HighWillsRespawnBehaviour), "OnStateExit")]
internal class HighWillsRespawnBehaviourExit_Patch
{
	public static bool Prefix(Animator animator)
	{
		return animator.name == "Body";
	}
}

using Gameplay.GameControllers.AnimationBehaviours.Player.Run;
using HarmonyLib;
using UnityEngine;

namespace Blasphemous.Multiplayer.Client;

[HarmonyPatch(typeof(RunStartBehaviour), "OnStateEnter")]
internal class RunStartBehaviourEnter_Patch
{
	public static bool Prefix(Animator animator)
	{
		return animator.name == "Body";
	}
}

using Gameplay.GameControllers.Combat;
using Gameplay.GameControllers.Entities;
using HarmonyLib;
using Tools.Level.Interactables;
using UnityEngine;

namespace Blasphemous.Multiplayer.Client.Enemies;

[HarmonyPatch(typeof(EntityExecution), "InstantiateExecution")]
internal class EntityExecution_Instantiate_Patch
{
	public static void Postfix(EntityExecution __instance)
	{
		if (__instance == null)
		{
			return;
		}
		Enemy componentInParent = __instance.GetComponentInParent<Enemy>();
		if (componentInParent == null || !EnemyManager.IsSyncableEnemy(componentInParent))
		{
			return;
		}
		Execution[] array = Object.FindObjectsOfType<Execution>();
		Execution execution = null;
		float num = 6f;
		Vector3 position = componentInParent.transform.position;
		foreach (Execution execution2 in array)
		{
			if (!(execution2 == null))
			{
				float num2 = Vector3.Distance(execution2.transform.position, position);
				if (num2 < num)
				{
					num = num2;
					execution = execution2;
				}
			}
		}
		if (execution == null)
		{
			return;
		}
		foreach (Transform item in execution.transform)
		{
			if (!(item == null) && item.name.ToLower().Contains("interactor"))
			{
				(item.gameObject.GetComponent<ExecutionAnimationRelay>() ?? item.gameObject.AddComponent<ExecutionAnimationRelay>()).Setup(execution, componentInParent);
				break;
			}
		}
	}
}

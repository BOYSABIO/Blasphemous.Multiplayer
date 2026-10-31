using System.Collections.Generic;
using Gameplay.GameControllers.Enemies.Framework.IA;
using Gameplay.GameControllers.Entities;
using UnityEngine;

namespace Blasphemous.Multiplayer.Client.Enemies;

public static class EnemyPristineRegistry
{
	private class Entry
	{
		public bool BehaviourEnabled = true;

		public bool FireEvents = true;

		public bool HasFireEvents;

		public readonly List<ColliderSnap> Colliders = new List<ColliderSnap>();
	}

	public struct ColliderSnap
	{
		public Collider2D Collider;

		public bool Enabled;
	}

	private static readonly Dictionary<int, Entry> _byId = new Dictionary<int, Entry>();

	public static void Clear()
	{
		_byId.Clear();
	}

	public static void CaptureIfNeeded(Enemy enemy)
	{
		if (enemy == null || enemy.gameObject == null)
		{
			return;
		}
		int instanceID = enemy.GetInstanceID();
		if (_byId.ContainsKey(instanceID))
		{
			return;
		}
		Entry entry = new Entry();
		EnemyBehaviour enemyBehaviour = enemy.EnemyBehaviour;
		if (enemyBehaviour != null)
		{
			entry.BehaviourEnabled = enemyBehaviour.enabled;
		}
		if (enemy.Animator != null)
		{
			entry.FireEvents = enemy.Animator.fireEvents;
			entry.HasFireEvents = true;
		}
		Collider2D[] componentsInChildren = enemy.GetComponentsInChildren<Collider2D>(includeInactive: true);
		for (int i = 0; i < componentsInChildren.Length; i++)
		{
			if (!(componentsInChildren[i] == null))
			{
				entry.Colliders.Add(new ColliderSnap
				{
					Collider = componentsInChildren[i],
					Enabled = componentsInChildren[i].enabled
				});
			}
		}
		_byId[instanceID] = entry;
	}

	public static bool TryGetFireEvents(Enemy enemy, out bool fireEvents)
	{
		fireEvents = true;
		if (enemy == null)
		{
			return false;
		}
		if (!_byId.TryGetValue(enemy.GetInstanceID(), out var value) || !value.HasFireEvents)
		{
			return false;
		}
		fireEvents = value.FireEvents;
		return true;
	}

	public static bool TryGetColliders(Enemy enemy, out List<ColliderSnap> snaps)
	{
		snaps = null;
		if (enemy == null)
		{
			return false;
		}
		if (!_byId.TryGetValue(enemy.GetInstanceID(), out var value))
		{
			return false;
		}
		snaps = value.Colliders;
		return true;
	}

	public static void Restore(Enemy enemy)
	{
		if (enemy == null || enemy.Dead)
		{
			return;
		}
		CaptureIfNeeded(enemy);
		if (!_byId.TryGetValue(enemy.GetInstanceID(), out var value))
		{
			return;
		}
		EnemyBehaviour enemyBehaviour = enemy.EnemyBehaviour;
		if (enemyBehaviour != null)
		{
			enemyBehaviour.enabled = value.BehaviourEnabled;
			if (value.BehaviourEnabled)
			{
				try
				{
					enemyBehaviour.StartBehaviour();
				}
				catch
				{
				}
			}
		}
		if (enemy.Animator != null && value.HasFireEvents)
		{
			enemy.Animator.fireEvents = value.FireEvents;
		}
		else if (enemy.Animator != null)
		{
			enemy.Animator.fireEvents = true;
		}
		for (int i = 0; i < value.Colliders.Count; i++)
		{
			ColliderSnap colliderSnap = value.Colliders[i];
			if (colliderSnap.Collider != null)
			{
				colliderSnap.Collider.enabled = colliderSnap.Enabled;
			}
		}
	}

	public static void RestoreAllInScene()
	{
		Enemy[] array = Object.FindObjectsOfType<Enemy>();
		for (int i = 0; i < array.Length; i++)
		{
			if (!(array[i] == null) && EnemyManager.IsSyncableEnemy(array[i]))
			{
				Restore(array[i]);
			}
		}
	}
}

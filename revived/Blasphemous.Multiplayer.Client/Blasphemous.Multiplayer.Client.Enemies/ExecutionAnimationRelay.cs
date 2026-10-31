using Framework.Managers;
using Gameplay.GameControllers.Entities;
using Gameplay.GameControllers.Penitent;
using Tools.Level.Interactables;
using UnityEngine;

namespace Blasphemous.Multiplayer.Client.Enemies;

public class ExecutionAnimationRelay : MonoBehaviour
{
	private Animator _animator;

	private Execution _execution;

	private Enemy _enemy;

	private int _lastHash;

	private bool _wasUsing;

	public void Setup(Execution execution, Enemy enemy)
	{
		_execution = execution;
		_enemy = enemy;
		_animator = GetComponent<Animator>();
		if (_animator == null)
		{
			_animator = GetComponentInChildren<Animator>();
		}
	}

	private void Update()
	{
		if (_animator == null || _enemy == null)
		{
			return;
		}
		if (!CoreLogicIsOnExecution())
		{
			_wasUsing = false;
			return;
		}
		_wasUsing = true;
		AnimatorStateInfo currentAnimatorStateInfo = _animator.GetCurrentAnimatorStateInfo(0);
		int shortNameHash = currentAnimatorStateInfo.shortNameHash;
		if (shortNameHash != 0 && shortNameHash != _lastHash)
		{
			_lastHash = shortNameHash;
			Main.Multiplayer.EnemyManager?.NotifyLocalExecutionAnim(_enemy, shortNameHash, currentAnimatorStateInfo.normalizedTime);
		}
	}

	private static bool CoreLogicIsOnExecution()
	{
		try
		{
			Penitent penitent = Core.Logic?.Penitent;
			return penitent != null && penitent.IsOnExecution;
		}
		catch
		{
			return false;
		}
	}
}

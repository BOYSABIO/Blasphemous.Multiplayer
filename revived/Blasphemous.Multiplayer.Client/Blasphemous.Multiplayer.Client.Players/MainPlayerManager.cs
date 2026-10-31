using System.Collections.Generic;
using Blasphemous.ModdingAPI;
using Blasphemous.Multiplayer.Client.Data;
using Framework.Managers;
using UnityEngine;

namespace Blasphemous.Multiplayer.Client.Players;

public class MainPlayerManager
{
	private Vector2 lastPosition;

	private byte lastAnimation;

	private bool lastDirection;

	private readonly float positionCutoff = 0.03f;

	private readonly float totalTimeBeforeSendAnimation = 0.5f;

	private float currentTimeBeforeSendAnimation;

	private readonly HashSet<int> _loggedUnknownAnimHashes = new HashSet<int>();

	private Vector2 CurrentPosition => Core.Logic.Penitent.transform.position;

	private byte CurrentAnimation
	{
		get
		{
			AnimatorStateInfo currentAnimatorStateInfo = Core.Logic.Penitent.Animator.GetCurrentAnimatorStateInfo(0);
			for (byte b = 0; b < AnimationStates.animations.Length; b++)
			{
				if (currentAnimatorStateInfo.IsName(AnimationStates.animations[b].name))
				{
					return b;
				}
			}
			int shortNameHash = currentAnimatorStateInfo.shortNameHash;
			if (_loggedUnknownAnimHashes.Add(shortNameHash))
			{
				ModLog.Warn($"[EnemySync] unknown penitent anim hash={shortNameHash} time={currentAnimatorStateInfo.normalizedTime:0.00}");
			}
			return byte.MaxValue;
		}
	}

	private bool CurrentDirection => Core.Logic.Penitent.SpriteRenderer.flipX;

	public void Update()
	{
		if (Main.Multiplayer.NetworkManager.IsConnected && Core.Logic.Penitent != null)
		{
			Vector2 currentPosition = CurrentPosition;
			if (Mathf.Abs(currentPosition.x - lastPosition.x) > positionCutoff || Mathf.Abs(currentPosition.y - lastPosition.y) > positionCutoff)
			{
				Main.Multiplayer.NetworkManager.SendPosition(currentPosition);
				lastPosition = currentPosition;
			}
			byte currentAnimation = CurrentAnimation;
			if (currentAnimation != lastAnimation)
			{
				if (currentTimeBeforeSendAnimation <= 0f && currentAnimation != byte.MaxValue)
				{
					Main.Multiplayer.NetworkManager.SendAnimation(currentAnimation);
				}
				lastAnimation = currentAnimation;
			}
			bool currentDirection = CurrentDirection;
			if (currentDirection != lastDirection)
			{
				Main.Multiplayer.NetworkManager.SendDirection(currentDirection);
				lastDirection = currentDirection;
			}
		}
		if (currentTimeBeforeSendAnimation > 0f)
		{
			currentTimeBeforeSendAnimation -= Time.deltaTime;
		}
	}

	public void SendAllLocationData()
	{
		Main.Multiplayer.NetworkManager.SendPosition(lastPosition = CurrentPosition);
		Main.Multiplayer.NetworkManager.SendAnimation(lastAnimation = 0);
		Main.Multiplayer.NetworkManager.SendDirection(lastDirection = CurrentDirection);
	}

	public void UseSpecialAnimation(byte animation)
	{
		currentTimeBeforeSendAnimation = totalTimeBeforeSendAnimation;
		Main.Multiplayer.NetworkManager.SendAnimation(animation);
	}
}

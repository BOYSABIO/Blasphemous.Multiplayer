using Framework.Managers;
using Gameplay.GameControllers.Penitent.Attack;
using Gameplay.UI.Others.UIGameLogic;
using UnityEngine;

namespace Blasphemous.Multiplayer.Client;

public static class UnityReferences
{
	private static Transform m_canvas;

	private static GameObject m_textObject;

	private static RuntimeAnimatorController m_playerAnimator;

	private static RuntimeAnimatorController m_SwordAnimator;

	private static Material m_playerMaterial;

	public static Transform CanvasObject
	{
		get
		{
			if (m_canvas == null)
			{
				Canvas[] array = Object.FindObjectsOfType<Canvas>();
				foreach (Canvas canvas in array)
				{
					if (canvas.name == "Game UI")
					{
						m_canvas = canvas.transform;
						break;
					}
				}
			}
			return m_canvas;
		}
	}

	public static GameObject TextObject
	{
		get
		{
			if (m_textObject == null)
			{
				PlayerPurgePoints[] array = Object.FindObjectsOfType<PlayerPurgePoints>();
				foreach (PlayerPurgePoints playerPurgePoints in array)
				{
					if (((Object)(object)playerPurgePoints).name == "PurgePoints")
					{
						m_textObject = ((Component)(object)playerPurgePoints).transform.GetChild(1).gameObject;
						break;
					}
				}
			}
			return m_textObject;
		}
	}

	public static RuntimeAnimatorController PlayerAnimator
	{
		get
		{
			if (m_playerAnimator == null)
			{
				m_playerAnimator = Core.Logic.Penitent?.Animator.runtimeAnimatorController;
			}
			return m_playerAnimator;
		}
	}

	public static RuntimeAnimatorController PlayerSwordAnimator
	{
		get
		{
			if (m_SwordAnimator == null)
			{
				m_SwordAnimator = Core.Logic.Penitent?.GetComponentInChildren<SwordAnimatorInyector>().GetComponent<Animator>().runtimeAnimatorController;
			}
			return m_SwordAnimator;
		}
	}

	public static Material PlayerMaterial
	{
		get
		{
			if (m_playerMaterial == null)
			{
				m_playerMaterial = Core.Logic.Penitent?.SpriteRenderer.material;
			}
			return m_playerMaterial;
		}
	}
}

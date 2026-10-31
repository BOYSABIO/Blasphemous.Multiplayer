using Blasphemous.ModdingAPI;
using UnityEngine;

namespace Blasphemous.Multiplayer.Client.Players;

public class SpecialAnimationChecker : MonoBehaviour
{
	private string lastAnimation;

	private Animator anim;

	private static string[] specialAnimations = new string[21]
	{
		"Waiting", "Kneeing", "Knee (No Aura)", "Knee (Aura ON)", "Knee (Aura OFF)", "Stand Up", "Turning ON", "Idle", "Halfheight Collection", "Floor Collection",
		"Unused", "Opening", "Switch", "Lever Down", "Lever Up", "(Open) Entering", "(Closed) Entering", "(KEY) Entering", "FakePenitent laydown", "FakePenitent gettingUp",
		"FakePenitent end"
	};

	private void Start()
	{
		lastAnimation = string.Empty;
		anim = GetComponent<Animator>();
	}

	private void Update()
	{
		AnimatorStateInfo currentAnimatorStateInfo = anim.GetCurrentAnimatorStateInfo(0);
		if (currentAnimatorStateInfo.IsName(lastAnimation))
		{
			return;
		}
		for (int i = 0; i < specialAnimations.Length; i++)
		{
			if (currentAnimatorStateInfo.IsName(specialAnimations[i]))
			{
				playSpecialAnimation(specialAnimations[i]);
				lastAnimation = specialAnimations[i];
				return;
			}
		}
		ModLog.Info("Unknown animation: " + base.transform.parent.name);
	}

	private void playSpecialAnimation(string anim)
	{
		if (anim == null)
		{
			return;
		}
		switch (anim.Length)
		{
		case 10:
			switch (anim[0])
			{
			default:
				return;
			case 'T':
				if (anim == "Turning ON")
				{
					Main.Multiplayer.MainPlayerManager.UseSpecialAnimation(240);
				}
				return;
			case 'L':
				break;
			}
			if (!(anim == "Lever Down"))
			{
				break;
			}
			goto IL_01ea;
		case 7:
			switch (anim[0])
			{
			case 'K':
				if (anim == "Kneeing")
				{
					Main.Multiplayer.MainPlayerManager.UseSpecialAnimation(241);
				}
				break;
			case 'O':
				if (anim == "Opening")
				{
					Main.Multiplayer.MainPlayerManager.UseSpecialAnimation(245);
				}
				break;
			}
			break;
		case 8:
			switch (anim[0])
			{
			default:
				return;
			case 'S':
				if (anim == "Stand Up")
				{
					Main.Multiplayer.MainPlayerManager.UseSpecialAnimation(242);
				}
				return;
			case 'L':
				break;
			}
			if (!(anim == "Lever Up"))
			{
				break;
			}
			goto IL_01ea;
		case 21:
			if (anim == "Halfheight Collection")
			{
				Main.Multiplayer.MainPlayerManager.UseSpecialAnimation(243);
			}
			break;
		case 16:
			if (anim == "Floor Collection")
			{
				Main.Multiplayer.MainPlayerManager.UseSpecialAnimation(244);
			}
			break;
		case 15:
			if (anim == "(Open) Entering")
			{
				Main.Multiplayer.MainPlayerManager.UseSpecialAnimation(247);
			}
			break;
		case 17:
			if (anim == "(Closed) Entering")
			{
				Main.Multiplayer.MainPlayerManager.UseSpecialAnimation(248);
			}
			break;
		case 14:
			if (anim == "(KEY) Entering")
			{
				Main.Multiplayer.MainPlayerManager.UseSpecialAnimation(249);
			}
			break;
		case 20:
			if (anim == "FakePenitent laydown")
			{
				Main.Multiplayer.MainPlayerManager.UseSpecialAnimation(250);
			}
			break;
		case 22:
			if (anim == "FakePenitent gettingUp")
			{
				Main.Multiplayer.MainPlayerManager.UseSpecialAnimation(251);
			}
			break;
		case 9:
		case 11:
		case 12:
		case 13:
		case 18:
		case 19:
			break;
			IL_01ea:
			Main.Multiplayer.MainPlayerManager.UseSpecialAnimation(246);
			break;
		}
	}
}

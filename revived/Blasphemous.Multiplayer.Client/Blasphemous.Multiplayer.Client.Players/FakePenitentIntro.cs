using UnityEngine;

namespace Blasphemous.Multiplayer.Client.Players;

public class FakePenitentIntro : MonoBehaviour
{
	private Animator anim;

	private void Start()
	{
		anim = GetComponent<Animator>();
	}

	private void LateUpdate()
	{
		if (anim.GetCurrentAnimatorStateInfo(0).IsName("FakePenitent end"))
		{
			base.gameObject.GetComponent<OtherPlayerScript>().FinishSpecialAnimation();
		}
	}
}

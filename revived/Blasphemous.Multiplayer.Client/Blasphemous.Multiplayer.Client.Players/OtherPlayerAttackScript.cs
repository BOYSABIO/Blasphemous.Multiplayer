using System.Collections;
using Blasphemous.ModdingAPI;
using Blasphemous.Multiplayer.Client.PvP.Models;
using Framework.Managers;
using Gameplay.GameControllers.Bosses.Quirce.Attack;
using Gameplay.GameControllers.Enemies.Projectiles;
using Gameplay.GameControllers.Entities;
using UnityEngine;

namespace Blasphemous.Multiplayer.Client.Players;

public class OtherPlayerAttackScript : MonoBehaviour
{
	private const float DEBLA_DURATION = 1.2f;

	private const float VERDIALES_DURATION = 2f;

	private SpriteRenderer SwordRenderer { get; set; }

	private Animator SwordAnim { get; set; }

	private BoxCollider2D DamageArea { get; set; }

	public void SetupAttack()
	{
		base.transform.localPosition = Vector3.zero;
		SwordRenderer = base.gameObject.AddComponent<SpriteRenderer>();
		SwordAnim = base.gameObject.AddComponent<Animator>();
		SwordAnim.runtimeAnimatorController = UnityReferences.PlayerSwordAnimator;
		base.gameObject.layer = LayerMask.NameToLayer("Enemy");
		DamageArea = base.gameObject.AddComponent<BoxCollider2D>();
		DamageArea.offset = new Vector2(0f, 0.92f);
		DamageArea.size = new Vector2(0.665f, 1.866f);
		DamageArea.isTrigger = true;
	}

	public void SetHitboxStatus(bool active)
	{
		DamageArea.enabled = active;
	}

	public void PlayEffectAnimation(EffectType effect, bool facingRight)
	{
		SwordRenderer.flipX = !facingRight;
		switch (effect)
		{
		case EffectType.SidewaysGrounded:
			SwordAnim.Play("Basic1_Lv1");
			break;
		case EffectType.UpwardsGrounded:
			SwordAnim.Play("BasicUpward_Lv1");
			break;
		case EffectType.SidewaysAir:
			SwordAnim.Play("Air1_Lv1");
			break;
		case EffectType.UpwardsAir:
			SwordAnim.Play("AirUpward_Lv1");
			break;
		case EffectType.Crouch:
			SwordAnim.Play("Crouch_Lv1");
			break;
		case EffectType.Debla:
			StartCoroutine(DisplayDebla());
			break;
		}
	}

	private IEnumerator DisplayDebla()
	{
		ModLog.Info("Spawning debla object for " + base.transform.parent.name);
		if (!(Core.Logic.Penitent == null))
		{
			GameObject debla = Object.Instantiate(Core.Logic.Penitent.PrayerCast.lightBeamPrayer.areaPrefab, base.transform);
			debla.name = "Debla";
			debla.transform.localPosition = Vector3.zero;
			Object.Destroy(debla.GetComponent<BossSpawnedAreaAttack>());
			Object.Destroy(debla.transform.GetChild(0).GetChild(0).gameObject);
			debla.GetComponent<Animator>().Play("AttackLoop");
			yield return new WaitForSecondsRealtime(1.2f);
			Object.Destroy(debla);
		}
	}

	private IEnumerator DisplayVerdiales()
	{
		ModLog.Info("Spawning verdiales object for " + base.transform.parent.name);
		if (!(Core.Logic.Penitent == null))
		{
			GameObject crawlerright = Object.Instantiate(Core.Logic.Penitent.PrayerCast.crawlerBallsPrayer.projectilePrefab, base.transform);
			crawlerright.name = "Crawler right";
			crawlerright.transform.localPosition = Vector3.zero;
			crawlerright.GetComponentInChildren<AttackArea>().enabled = false;
			crawlerright.GetComponentInChildren<PolygonCollider2D>().enabled = false;
			crawlerright.GetComponentInChildren<StraightProjectile>().Init(crawlerright.transform.position, crawlerright.transform.position + Vector3.right, 16f);
			crawlerright.transform.position += Vector3.right * 0.01f;
			GameObject crawlerleft = Object.Instantiate(Core.Logic.Penitent.PrayerCast.crawlerBallsPrayer.projectilePrefab, base.transform);
			crawlerleft.name = "Crawler left";
			crawlerleft.transform.localPosition = Vector3.zero;
			crawlerleft.GetComponentInChildren<AttackArea>().enabled = false;
			crawlerleft.GetComponentInChildren<PolygonCollider2D>().enabled = false;
			crawlerleft.GetComponentInChildren<StraightProjectile>().Init(crawlerleft.transform.position, crawlerleft.transform.position + Vector3.left, 16f);
			crawlerleft.transform.position += Vector3.left * 0.01f;
			yield return new WaitForSecondsRealtime(2f);
			Object.Destroy(crawlerright);
			Object.Destroy(crawlerleft);
		}
	}
}

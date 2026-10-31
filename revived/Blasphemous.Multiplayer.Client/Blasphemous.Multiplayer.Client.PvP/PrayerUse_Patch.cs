using Blasphemous.Multiplayer.Client.PvP.Models;
using Framework.Inventory;
using Gameplay.GameControllers.Penitent.Abilities;
using HarmonyLib;

namespace Blasphemous.Multiplayer.Client.PvP;

[HarmonyPatch(typeof(PrayerUse), "StartUsingPrayer")]
internal class PrayerUse_Patch
{
	public static void Postfix(Prayer prayer)
	{
		if (prayer.id == "PR03")
		{
			Main.Multiplayer.NetworkManager.SendEffect(EffectType.Debla);
		}
	}
}

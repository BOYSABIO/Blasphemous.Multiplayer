using Blasphemous.ModdingAPI;
using Gameplay.GameControllers.Entities;
using Gameplay.GameControllers.Penitent.Damage;
using HarmonyLib;

namespace Blasphemous.Multiplayer.Client;

[HarmonyPatch(typeof(ThrowBack), "OnDestroy")]
internal class ThrowBack_Patch
{
	public static bool Prefix(PenitentDamageArea ____damageArea)
	{
		if (____damageArea != null && ____damageArea.OnDamaged == null)
		{
			ModLog.Warn("OnDamaged was null!");
			return false;
		}
		return true;
	}
}

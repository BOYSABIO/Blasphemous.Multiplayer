using Gameplay.GameControllers.Entities;
using HarmonyLib;

namespace Blasphemous.Multiplayer.Client.PvP;

[HarmonyPatch(typeof(Entity), "Damage")]
internal class Entity_Damage_Patch
{
	public static void Prefix(ref string impactAudioId)
	{
		impactAudioId = impactAudioId?.Replace("PVP", string.Empty);
	}
}

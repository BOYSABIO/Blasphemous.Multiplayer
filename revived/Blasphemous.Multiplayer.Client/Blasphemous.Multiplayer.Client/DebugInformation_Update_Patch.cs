using Gameplay.UI.Widgets;
using HarmonyLib;

namespace Blasphemous.Multiplayer.Client;

[HarmonyPatch(typeof(DebugInformation), "Update")]
internal class DebugInformation_Update_Patch
{
	public static bool Prefix()
	{
		return false;
	}
}

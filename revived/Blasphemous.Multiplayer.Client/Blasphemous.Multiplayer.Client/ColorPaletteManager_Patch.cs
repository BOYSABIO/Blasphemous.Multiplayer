using Framework.Managers;
using HarmonyLib;

namespace Blasphemous.Multiplayer.Client;

[HarmonyPatch(typeof(ColorPaletteManager), "SetCurrentColorPaletteId")]
internal class ColorPaletteManager_Patch
{
	public static void Postfix(string colorPaletteId)
	{
		// Native startup can select the default skin before the mod's Start.
		// The connection handshake sends the current skin once it is ready.
		Main.Multiplayer?.NetworkManager?.SendSkin(colorPaletteId);
	}
}

using Framework.Managers;
using HarmonyLib;

namespace Blasphemous.Multiplayer.Client;

[HarmonyPatch(typeof(ColorPaletteManager), "SetCurrentColorPaletteId")]
internal class ColorPaletteManager_Patch
{
	public static void Postfix(string colorPaletteId)
	{
		Main.Multiplayer.NetworkManager.SendSkin(colorPaletteId);
	}
}

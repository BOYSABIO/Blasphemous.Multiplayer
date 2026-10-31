using Gameplay.UI.Others.MenuLogic;
using HarmonyLib;

namespace Blasphemous.Multiplayer.Client.Map;

[HarmonyPatch(typeof(NewMapMenuWidget), "OnShow")]
internal class MapMenuWidget_Patch
{
	public static void Postfix()
	{
		Main.Multiplayer.MapManager.RefreshMap(forceRecalculate: true);
	}
}

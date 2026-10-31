using Blasphemous.Multiplayer.Common.Enums;
using Framework.Managers;
using Framework.Map;
using HarmonyLib;
using UnityEngine;

namespace Blasphemous.Multiplayer.Client.ProgressSync.Helpers;

[HarmonyPatch(typeof(NewMapManager), "RevealCellInPosition")]
internal class NewMapManager_Patch
{
	public static bool Prefix(NewMapManager __instance, Vector2 position, MapData ___CurrentMap)
	{
		if (Main.Multiplayer.ProgressManager.CurrentlyUpdatingProgress)
		{
			int num = Mathf.RoundToInt(position.x);
			if (___CurrentMap != null && num >= 0 && num < ___CurrentMap.Cells.Count)
			{
				___CurrentMap.Cells[num].Revealed = true;
			}
			return false;
		}
		if (___CurrentMap == null || !___CurrentMap.CellsByZone.ContainsKey(__instance.CurrentScene))
		{
			return false;
		}
		foreach (CellData item in ___CurrentMap.CellsByZone[__instance.CurrentScene])
		{
			if (item.Bounding.Contains(position) && !item.Revealed)
			{
				ProgressUpdate progress = new ProgressUpdate(___CurrentMap.Cells.IndexOf(item).ToString(), ProgressType.MapCell, 0);
				Main.Multiplayer.NetworkManager.SendProgress(progress);
			}
		}
		return true;
	}
}

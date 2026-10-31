using System;
using Blasphemous.ModdingAPI;
using Framework.Managers;
using Framework.Map;
using HarmonyLib;
using UnityEngine;

namespace Blasphemous.Multiplayer.Client.Map;

[HarmonyPatch(typeof(NewMapManager), "GetCellKeyFromPosition", new Type[]
{
	typeof(string),
	typeof(Vector2)
})]
internal class MapManagerRoom_Patch
{
	public static bool Prefix(string scene, Vector2 position, MapData ___CurrentMap)
	{
		if (position.x > -999f)
		{
			return true;
		}
		if (scene == null || scene.Length != 9)
		{
			Main.Multiplayer.MapManager.ActivePlayerPosition = new Vector2(-1f, -1f);
			ModLog.Info("Player zone '" + scene + "' does not exist!");
			return false;
		}
		ZoneKey key = new ZoneKey(scene.Substring(0, 3), scene.Substring(3, 3), scene.Substring(6, 3));
		if (!___CurrentMap.CellsByZone.ContainsKey(key))
		{
			Main.Multiplayer.MapManager.ActivePlayerPosition = new Vector2(-1f, -1f);
			ModLog.Info("Player zone '" + scene + "' does not exist!");
			return false;
		}
		Vector2 zero = Vector2.zero;
		foreach (CellData item in ___CurrentMap.CellsByZone[key])
		{
			zero += new Vector2(item.CellKey.X, item.CellKey.Y);
		}
		int count = ___CurrentMap.CellsByZone[key].Count;
		Vector2 activePlayerPosition = new Vector2(zero.x / (float)count, zero.y / (float)count);
		Main.Multiplayer.MapManager.ActivePlayerPosition = activePlayerPosition;
		return false;
	}
}

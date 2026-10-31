using Framework.Managers;
using Framework.Map;
using HarmonyLib;
using UnityEngine;

namespace Blasphemous.Multiplayer.Client.Map;

[HarmonyPatch(typeof(NewMapManager), "GetAllMarks")]
internal class MapManagerConfessor_Patch
{
	public static void Prefix(NewMapManager __instance, MapData ___CurrentMap)
	{
		if (Core.Events.GetFlag("CONFESSOR_1_ARENACOMPLETED"))
		{
			RemoveConfessorMarker(new Vector2(-100f, -13f));
		}
		if (Core.Events.GetFlag("CONFESSOR_2_ARENACOMPLETED"))
		{
			RemoveConfessorMarker(new Vector2(-480f, 96f));
		}
		if (Core.Events.GetFlag("CONFESSOR_3_ARENACOMPLETED"))
		{
			RemoveConfessorMarker(new Vector2(-660f, -170f));
		}
		if (Core.Events.GetFlag("CONFESSOR_4_ARENACOMPLETED"))
		{
			RemoveConfessorMarker(new Vector2(-880f, -4f));
		}
		if (Core.Events.GetFlag("CONFESSOR_5_ARENACOMPLETED"))
		{
			RemoveConfessorMarker(new Vector2(260f, 42f));
		}
		if (Core.Events.GetFlag("CONFESSOR_6_ARENACOMPLETED"))
		{
			RemoveConfessorMarker(new Vector2(280f, 10f));
		}
		if (Core.Events.GetFlag("CONFESSOR_7_ARENACOMPLETED"))
		{
			RemoveConfessorMarker(new Vector2(0f, 120f));
		}
		void RemoveConfessorMarker(Vector2 position)
		{
			CellKey cellKeyFromPosition = __instance.GetCellKeyFromPosition(position);
			if (___CurrentMap.CellsByCellKey.ContainsKey(cellKeyFromPosition))
			{
				___CurrentMap.CellsByCellKey[cellKeyFromPosition].Type = EditorMapCellData.CellType.Normal;
			}
		}
	}
}

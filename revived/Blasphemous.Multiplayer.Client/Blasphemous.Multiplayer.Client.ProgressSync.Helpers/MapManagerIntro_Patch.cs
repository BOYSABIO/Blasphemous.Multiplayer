using Blasphemous.Multiplayer.Common.Enums;
using Framework.Managers;
using Framework.Map;
using HarmonyLib;

namespace Blasphemous.Multiplayer.Client.ProgressSync.Helpers;

[HarmonyPatch(typeof(NewMapManager), "GetCurrentPersistentState")]
internal class MapManagerIntro_Patch
{
	public static bool Prefix(string dataPath, MapData ___CurrentMap)
	{
		if (dataPath != "intro")
		{
			return true;
		}
		for (int i = 0; i < ___CurrentMap.Cells.Count; i++)
		{
			if (___CurrentMap.Cells[i].Revealed)
			{
				ProgressUpdate progress = new ProgressUpdate(i.ToString(), ProgressType.MapCell, 0);
				Main.Multiplayer.NetworkManager.SendProgress(progress);
			}
		}
		return false;
	}
}

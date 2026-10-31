using System.Collections.Generic;
using Blasphemous.Multiplayer.Common.Enums;
using Framework.FrameworkCore;
using Framework.Managers;
using HarmonyLib;

namespace Blasphemous.Multiplayer.Client.ProgressSync.Helpers;

[HarmonyPatch(typeof(SkillManager), "GetCurrentPersistentState")]
internal class SkillManagerIntro_Patch
{
	public static bool Prefix(string dataPath, Dictionary<string, UnlockableSkill> ___allSkills)
	{
		if (dataPath != "intro")
		{
			return true;
		}
		foreach (string key in ___allSkills.Keys)
		{
			if (___allSkills[key].unlocked)
			{
				ProgressUpdate progress = new ProgressUpdate(key, ProgressType.SwordSkill, 0);
				Main.Multiplayer.NetworkManager.SendProgress(progress);
			}
		}
		return false;
	}
}

using System.Collections.Generic;
using Blasphemous.Multiplayer.Common.Enums;
using Framework.FrameworkCore;
using Framework.Managers;
using HarmonyLib;

namespace Blasphemous.Multiplayer.Client.ProgressSync.Helpers;

[HarmonyPatch(typeof(SkillManager), "UnlockSkill")]
internal class SkilManager_Patch
{
	public static bool Prefix(string skill, bool ignoreChecks, Dictionary<string, UnlockableSkill> ___allSkills)
	{
		if (Main.Multiplayer.ProgressManager.CurrentlyUpdatingProgress)
		{
			___allSkills[skill].unlocked = true;
			return false;
		}
		if (!Main.Multiplayer.RandomizerMode || ignoreChecks)
		{
			ProgressUpdate progress = new ProgressUpdate(skill, ProgressType.SwordSkill, 0);
			Main.Multiplayer.NetworkManager.SendProgress(progress);
		}
		return true;
	}
}

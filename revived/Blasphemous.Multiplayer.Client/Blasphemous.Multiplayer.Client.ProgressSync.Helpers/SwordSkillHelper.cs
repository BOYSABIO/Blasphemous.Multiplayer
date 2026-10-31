using Framework.FrameworkCore;
using Framework.Managers;

namespace Blasphemous.Multiplayer.Client.ProgressSync.Helpers;

public class SwordSkillHelper : IProgressHelper
{
	public void ApplyProgress(ProgressUpdate progress)
	{
		Core.SkillManager.UnlockSkill(progress.Id, ignoreChecks: true);
	}

	public string GetProgressNotification(ProgressUpdate progress)
	{
		UnlockableSkill skill = Core.SkillManager.GetSkill(progress.Id);
		if (!(skill != null))
		{
			return null;
		}
		return Main.Multiplayer.LocalizationHandler.Localize("sklnot") + " " + skill.caption;
	}

	public void SendAllProgress()
	{
		Core.SkillManager.GetCurrentPersistentState("intro", fullSave: false);
	}
}

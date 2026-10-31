using Blasphemous.Multiplayer.Common.Enums;
using Framework.FrameworkCore.Attributes;
using Framework.FrameworkCore.Attributes.Logic;
using Framework.Managers;
using HarmonyLib;

namespace Blasphemous.Multiplayer.Client.ProgressSync.Helpers;

[HarmonyPatch(typeof(Attribute), "Upgrade")]
public class AttributeUpgrade_Patch
{
	public static bool Prefix(Attribute __instance)
	{
		if (Main.Multiplayer.ProgressManager.CurrentlyUpdatingProgress)
		{
			return true;
		}
		if (!Main.Multiplayer.CanObtainStatUpgrades)
		{
			return false;
		}
		string text = null;
		if (__instance.GetType() == typeof(Life))
		{
			text = "LIFE";
		}
		else if (__instance.GetType() == typeof(Fervour))
		{
			text = "FERVOUR";
		}
		else if (__instance.GetType() == typeof(Strength))
		{
			text = "STRENGTH";
		}
		else if (__instance.GetType() == typeof(MeaCulpa))
		{
			text = "MEACULPA";
		}
		else if (__instance.GetType() == typeof(BeadSlots))
		{
			text = "BEADSLOTS";
		}
		else if (__instance.GetType() == typeof(Flask))
		{
			text = "FLASK";
		}
		else if (__instance.GetType() == typeof(FlaskHealth))
		{
			text = "FLASKHEALTH";
		}
		int num = __instance.GetUpgrades() + 1;
		if (text == "FLASK")
		{
			num += Core.Logic.Penitent.Stats.FlaskHealth.GetUpgrades();
		}
		if (text != null && num > 0)
		{
			ProgressUpdate progress = new ProgressUpdate(text, ProgressType.PlayerStat, (byte)num);
			Main.Multiplayer.NetworkManager.SendProgress(progress);
		}
		return true;
	}
}

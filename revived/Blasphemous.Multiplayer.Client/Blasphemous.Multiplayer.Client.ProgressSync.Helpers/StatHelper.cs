using Blasphemous.ModdingAPI;
using Blasphemous.Multiplayer.Common.Enums;
using Framework.FrameworkCore.Attributes.Logic;
using Framework.Managers;

namespace Blasphemous.Multiplayer.Client.ProgressSync.Helpers;

public class StatHelper : IProgressHelper
{
	public void ApplyProgress(ProgressUpdate progress)
	{
		Attribute attribute;
		switch (progress.Id)
		{
		case "LIFE":
			attribute = Core.Logic.Penitent.Stats.Life;
			break;
		case "FERVOUR":
			attribute = Core.Logic.Penitent.Stats.Fervour;
			break;
		case "STRENGTH":
			attribute = Core.Logic.Penitent.Stats.Strength;
			break;
		case "MEACULPA":
			attribute = Core.Logic.Penitent.Stats.MeaCulpa;
			break;
		case "BEADSLOTS":
			attribute = Core.Logic.Penitent.Stats.BeadSlots;
			break;
		case "FLASKHEALTH":
			attribute = Core.Logic.Penitent.Stats.FlaskHealth;
			break;
		case "FLASK":
			Core.Logic.Penitent.Stats.Flask.SetPermanentBonus((progress.Value == byte.MaxValue) ? (-1) : progress.Value);
			return;
		default:
			ModLog.Error("Error: Unknown stat received - " + progress.Id);
			return;
		}
		while (attribute.GetUpgrades() < progress.Value)
		{
			attribute.Upgrade();
		}
	}

	public string GetProgressNotification(ProgressUpdate progress)
	{
		string key;
		switch (progress.Id)
		{
		case "LIFE":
			key = "stlife";
			break;
		case "FERVOUR":
			key = "stferv";
			break;
		case "STRENGTH":
			key = "stmeas";
			break;
		case "MEACULPA":
			key = "stmeat";
			break;
		case "BEADSLOTS":
			key = "stbead";
			break;
		case "FLASK":
			key = "stfknm";
			break;
		case "FLASKHEALTH":
			key = "stfkhl";
			break;
		default:
			ModLog.Error("Error: Unknown stat received - " + progress.Id);
			return null;
		}
		return Main.Multiplayer.LocalizationHandler.Localize("stnot") + " " + Main.Multiplayer.LocalizationHandler.Localize(key);
	}

	public void SendAllProgress()
	{
		byte b = (byte)Core.Logic.Penitent.Stats.Life.GetUpgrades();
		if (b > 0)
		{
			ProgressUpdate progress = new ProgressUpdate("LIFE", ProgressType.PlayerStat, b);
			Main.Multiplayer.NetworkManager.SendProgress(progress);
		}
		byte b2 = (byte)Core.Logic.Penitent.Stats.Fervour.GetUpgrades();
		if (b2 > 0)
		{
			ProgressUpdate progress2 = new ProgressUpdate("FERVOUR", ProgressType.PlayerStat, b2);
			Main.Multiplayer.NetworkManager.SendProgress(progress2);
		}
		byte b3 = (byte)Core.Logic.Penitent.Stats.Strength.GetUpgrades();
		if (b3 > 0)
		{
			ProgressUpdate progress3 = new ProgressUpdate("STRENGTH", ProgressType.PlayerStat, b3);
			Main.Multiplayer.NetworkManager.SendProgress(progress3);
		}
		byte b4 = (byte)Core.Logic.Penitent.Stats.MeaCulpa.GetUpgrades();
		if (b4 > 0)
		{
			ProgressUpdate progress4 = new ProgressUpdate("MEACULPA", ProgressType.PlayerStat, b4);
			Main.Multiplayer.NetworkManager.SendProgress(progress4);
		}
		byte b5 = (byte)Core.Logic.Penitent.Stats.BeadSlots.GetUpgrades();
		if (b5 > 0)
		{
			ProgressUpdate progress5 = new ProgressUpdate("BEADSLOTS", ProgressType.PlayerStat, b5);
			Main.Multiplayer.NetworkManager.SendProgress(progress5);
		}
		byte b6 = (byte)Core.Logic.Penitent.Stats.FlaskHealth.GetUpgrades();
		byte b7 = (byte)(Core.Logic.Penitent.Stats.Flask.GetUpgrades() + b6);
		if (b7 > 0)
		{
			ProgressUpdate progress6 = new ProgressUpdate("FLASK", ProgressType.PlayerStat, b7);
			Main.Multiplayer.NetworkManager.SendProgress(progress6);
		}
		if (b6 > 0)
		{
			ProgressUpdate progress7 = new ProgressUpdate("FLASKHEALTH", ProgressType.PlayerStat, b6);
			Main.Multiplayer.NetworkManager.SendProgress(progress7);
		}
	}
}

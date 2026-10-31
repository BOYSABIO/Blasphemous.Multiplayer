using Blasphemous.Multiplayer.Common.Enums;

namespace Blasphemous.Multiplayer.Client.ProgressSync;

public class ProgressUpdate
{
	private readonly string id;

	private readonly ProgressType type;

	private readonly byte value;

	public string Id => id;

	public ProgressType Type => type;

	public byte Value => value;

	public ProgressUpdate(string id, ProgressType type, byte value)
	{
		this.id = id;
		this.type = type;
		this.value = value;
	}

	public bool ShouldSyncProgress(Config config)
	{
		switch (type)
		{
		case ProgressType.Bead:
		case ProgressType.Prayer:
		case ProgressType.Relic:
		case ProgressType.Heart:
		case ProgressType.Collectible:
		case ProgressType.QuestItem:
			return config.syncSettings.inventoryItems;
		case ProgressType.PlayerStat:
			return config.syncSettings.playerStats;
		case ProgressType.SwordSkill:
			return config.syncSettings.swordSkills;
		case ProgressType.MapCell:
			return config.syncSettings.mapCells;
		case ProgressType.Flag:
		case ProgressType.PersistentObject:
		case ProgressType.Teleport:
		case ProgressType.ChurchDonation:
		case ProgressType.MiriamStatus:
			return config.syncSettings.worldState;
		default:
			return false;
		}
	}
}

using System;

namespace Blasphemous.Multiplayer.Client;

[Serializable]
public class Config
{
	[Serializable]
	public class SyncSettings
	{
		public bool inventoryItems;

		public bool playerStats;

		public bool swordSkills;

		public bool worldState;

		public bool mapCells;

		public SyncSettings()
		{
			inventoryItems = true;
			playerStats = true;
			swordSkills = true;
			worldState = true;
			mapCells = true;
		}
	}

	public float notificationDisplaySeconds;

	public bool displayNametags;

	public bool displayOwnNametag;

	public bool showPlayersOnMap;

	public bool showOtherTeamOnMap;

	public bool enablePvP;

	public bool enableFriendlyFire;

	public string preferredEnemyHost;

	public SyncSettings syncSettings;

	public Config()
	{
		notificationDisplaySeconds = 4f;
		displayNametags = true;
		displayOwnNametag = true;
		showPlayersOnMap = true;
		showOtherTeamOnMap = false;
		enablePvP = true;
		enableFriendlyFire = false;
		preferredEnemyHost = string.Empty;
		syncSettings = new SyncSettings();
	}
}

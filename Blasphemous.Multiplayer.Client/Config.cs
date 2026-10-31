namespace Blasphemous.Multiplayer.Client
{
    [System.Serializable]
    public class Config
    {
        public float notificationDisplaySeconds;
        public bool displayNametags;
        public bool displayOwnNametag;
        public bool showPlayersOnMap;
        public bool showOtherTeamOnMap;
        public bool enablePvP;
        public bool enableFriendlyFire;
        /// <summary>
        /// Legacy setting retained for existing config files. Enemy authority now follows
        /// the server's session host so clients cannot select conflicting owners.
        /// </summary>
        public string preferredEnemyHost;
        public SyncSettings syncSettings;

        // Default config
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

        [System.Serializable]
        public class SyncSettings
        {
            public bool inventoryItems;
            public bool playerStats;
            public bool swordSkills;
            public bool worldState; // Split into further sections maybe
            public bool mapCells;

            // Default sync settings
            public SyncSettings()
            {
                inventoryItems = true;
                playerStats = true;
                swordSkills = true;
                worldState = true;
                mapCells = true;
            }
        }
    }
}

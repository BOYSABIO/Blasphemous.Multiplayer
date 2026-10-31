using Blasphemous.CheatConsole;
using Blasphemous.ModdingAPI;
using Blasphemous.ModdingAPI.Helpers;
using Blasphemous.ModdingAPI.Persistence;
using Blasphemous.Multiplayer.Client.Data;
using Blasphemous.Multiplayer.Client.Enemies;
using Blasphemous.Multiplayer.Client.Map;
using Blasphemous.Multiplayer.Client.Network;
using Blasphemous.Multiplayer.Client.Notifications;
using Blasphemous.Multiplayer.Client.Ping;
using Blasphemous.Multiplayer.Client.Players;
using Blasphemous.Multiplayer.Client.ProgressSync;
using Blasphemous.Multiplayer.Client.PvP;
using Blasphemous.Multiplayer.Common.Enums;
using Framework.Managers;
using Tools.Level.Interactables;
using UnityEngine;

namespace Blasphemous.Multiplayer.Client;

public class Multiplayer : BlasMod, ISlotPersistentMod<MultiplayerSlotData>, IGlobalPersistentMod<MultiplayerGlobalData>
{
	public AttackManager AttackManager { get; private set; }

	public EnemyManager EnemyManager { get; private set; }

	public MainPlayerManager MainPlayerManager { get; private set; }

	public Blasphemous.Multiplayer.Client.Map.MapManager MapManager { get; private set; }

	public NetworkManager NetworkManager { get; private set; }

	public NotificationManager NotificationManager { get; private set; }

	public OtherPlayerManager OtherPlayerManager { get; private set; }

	public ProgressManager ProgressManager { get; private set; }

	public DamageCalculator DamageCalculator { get; private set; }

	public PingManager PingManager { get; private set; }

	public bool RandomizerMode => ModHelper.IsModLoadedByName("Randomizer");

	public Config config { get; private set; }

	public bool CurrentlyInLevel { get; private set; }

	public string PlayerName { get; private set; }

	public byte PlayerTeam { get; private set; }

	public bool CanObtainStatUpgrades { get; set; }

	public ConnectionInfo LastConnectionInfo { get; set; } = new ConnectionInfo();

	internal Multiplayer()
		: base(ModInfo.MOD_ID, ModInfo.MOD_NAME, ModInfo.MOD_AUTHOR, ModInfo.MOD_VERSION)
	{
	}

	protected override void OnInitialize()
	{
		base.LocalizationHandler.RegisterDefaultLanguage("en");
		AttackManager = new AttackManager();
		EnemyManager = new EnemyManager();
		MainPlayerManager = new MainPlayerManager();
		MapManager = new Blasphemous.Multiplayer.Client.Map.MapManager();
		NetworkManager = new NetworkManager();
		NotificationManager = new NotificationManager();
		OtherPlayerManager = new OtherPlayerManager();
		ProgressManager = new ProgressManager();
		DamageCalculator = new DamageCalculator();
		PingManager = new PingManager();
		NetworkManager.OnConnect += OnConnect;
		config = base.ConfigHandler.Load<Config>();
		PersistentStates.loadPersistentObjects();
		PlayerName = string.Empty;
		PlayerTeam = 1;
	}

	protected override void OnRegisterServices(ModServiceProvider provider)
	{
		provider.RegisterCommand(new MultiplayerCommand());
	}

	public void OnConnect(string ipAddress, string playerName, string password)
	{
		PlayerName = playerName;
	}

	public void OnDisconnect()
	{
		NotificationManager.DisplayNotification(base.LocalizationHandler.Localize("dcon"));
		ProgressManager.ResetProgressSentStatus();
		OtherPlayerManager.RemoveAllConnectedPlayers();
		OtherPlayerManager.RemoveAllActivePlayers();
		EnemyManager.OnDisconnect();
		PlayerName = string.Empty;
	}

	protected override void OnLevelLoaded(string oldLevel, string newLevel)
	{
		CurrentlyInLevel = newLevel != "MainMenu";
		NotificationManager.LevelLoaded();
		OtherPlayerManager.LevelLoaded(newLevel);
		ProgressManager.LevelLoaded(newLevel);
		CanObtainStatUpgrades = true;
		if (CurrentlyInLevel && NetworkManager.IsConnected)
		{
			ModLog.Info("Entering new scene: " + newLevel);
			MainPlayerManager.SendAllLocationData();
			NetworkManager.SendEnterScene(newLevel);
			ProgressManager.SendAllProgress();
			EnemyManager.LevelLoaded(newLevel);
		}
		else
		{
			EnemyManager.LevelUnloaded();
		}
		if (newLevel == "D06Z01S01")
		{
			FixElevatorLevers();
		}
	}

	protected override void OnLevelUnloaded(string oldLevel, string newLevel)
	{
		if (CurrentlyInLevel && NetworkManager.IsConnected)
		{
			ModLog.Info("Leaving old scene");
			NetworkManager.SendLeaveScene();
		}
		EnemyManager.LevelUnloaded();
		CurrentlyInLevel = false;
	}

	protected override void OnLateUpdate()
	{
		NetworkManager.ProcessQueue();
		MapManager.Update();
		NotificationManager.Update();
		PingManager.OnUpdate();
		if (CurrentlyInLevel)
		{
			MainPlayerManager.Update();
			OtherPlayerManager.Update();
			ProgressManager.Update();
			EnemyManager.Update();
		}
		NetworkManager.SendQueue();
	}

	public void SetPlayerData(string player, byte team)
	{
		PlayerName = player;
		PlayerTeam = team;
	}

	public void RefreshPlayerColors()
	{
		OtherPlayerManager.RefreshNametagColors();
		MapManager.QueueMapUpdate();
	}

	private void OnConnect(bool success, RefusalType refusal)
	{
		if (!success)
		{
			NotificationManager.DisplayNotification(base.LocalizationHandler.Localize("refuse") + ": " + base.LocalizationHandler.Localize(refusal switch
			{
				RefusalType.Connection => "refcon", 
				RefusalType.Protocol => "refpro", 
				RefusalType.DuplicateIp => "refipa", 
				RefusalType.DuplicateName => "refnam", 
				RefusalType.Password => "refpas", 
				RefusalType.PlayerLimit => "refmax", 
				RefusalType.Banned => "refban", 
				_ => "refunk", 
			}));
			return;
		}
		NetworkManager.SendSkin(Core.ColorPaletteManager.GetCurrentColorPaletteId());
		NetworkManager.SendTeam(PlayerTeam);
		if (CurrentlyInLevel)
		{
			MainPlayerManager.SendAllLocationData();
			NetworkManager.SendEnterScene(Core.LevelManager.currentLevel.LevelName);
			OtherPlayerManager.AddNametag(PlayerName, friendlyTeam: true);
			ProgressManager.SendAllProgress();
			EnemyManager.LevelLoaded(Core.LevelManager.currentLevel.LevelName);
		}
		NotificationManager.DisplayNotification(base.LocalizationHandler.Localize("con"));
	}

	public void ProcessRecievedStat(string playerName, ProgressUpdate progress)
	{
		PlayerStatus playerStatus = OtherPlayerManager.FindConnectedPlayer(playerName);
		if (playerStatus != null && CurrentlyInLevel && !RandomizerMode && progress.Type == ProgressType.PlayerStat && !(Core.LevelManager.currentLevel.LevelName != playerStatus.CurrentScene) && (progress.Id == "LIFE" || progress.Id == "FERVOUR" || progress.Id == "STRENGTH" || progress.Id == "MEACULPA"))
		{
			CanObtainStatUpgrades = false;
			ModLog.Warn("Received stat upgrade from player in the same room!");
		}
	}

	private void FixElevatorLevers()
	{
		Lever[] array = Object.FindObjectsOfType<Lever>();
		Lever lever = null;
		Lever lever2 = null;
		Lever[] array2 = array;
		foreach (Lever lever3 in array2)
		{
			if (lever3.GetPersistenID() == "fc76ec10-a6e2-4465-ade0-642520e84efc")
			{
				lever = lever3;
			}
			else if (lever3.GetPersistenID() == "14b5e15b-2178-4677-8a54-468d5496037d")
			{
				lever2 = lever3;
			}
		}
		if (lever2 == null || lever == null)
		{
			ModLog.Warn("Could not find elevator levers!");
		}
		else if (Core.Events.GetFlag("ELEVATOR_POSITION_1"))
		{
			lever.SetLeverDownInstantly();
			if (Core.Events.GetFlag("ELEVATOR_POSITION_2_UNLOCKED"))
			{
				lever2.SetLeverUpInstantly();
			}
		}
		else if (Core.Events.GetFlag("ELEVATOR_POSITION_2"))
		{
			lever.SetLeverUpInstantly();
			if (Core.Events.GetFlag("ELEVATOR_POSITION_3_UNLOCKED"))
			{
				lever2.SetLeverUpInstantly();
			}
		}
		else if (Core.Events.GetFlag("ELEVATOR_POSITION_3"))
		{
			lever.SetLeverUpInstantly();
			if (Core.Events.GetFlag("ELEVATOR_FULL_UNLOCKED"))
			{
				lever2.SetLeverUpInstantly();
			}
		}
	}

	public MultiplayerSlotData SaveSlot()
	{
		return new MultiplayerSlotData
		{
			InteractedPersistenceObjects = ProgressManager.SaveInteractedObjects()
		};
	}

	public void LoadSlot(MultiplayerSlotData data)
	{
		ProgressManager.LoadInteractedObjects(data.InteractedPersistenceObjects);
	}

	public void ResetSlot()
	{
		ProgressManager.ClearInteractedObjects();
	}

	public MultiplayerGlobalData SaveGlobal()
	{
		return new MultiplayerGlobalData
		{
			LastConnection = LastConnectionInfo
		};
	}

	public void LoadGlobal(MultiplayerGlobalData data)
	{
		LastConnectionInfo = data.LastConnection;
	}
}


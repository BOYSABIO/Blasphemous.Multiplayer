using System;
using System.Collections.Generic;
using System.Reflection;
using Basalt.CommandParser;
using Blasphemous.Multiplayer.Server.Models;

namespace Blasphemous.Multiplayer.Server;

internal static class Core
{
	private static Server server;

	private static readonly Dictionary<byte, TeamInfo> teamGameDatas = new Dictionary<byte, TeamInfo>();

	private static string ApplicationTitle
	{
		get
		{
			string text = Assembly.GetExecutingAssembly().GetName().Version.ToString(3) ?? "Unknown";
			return "Blasphemous Multiplayer Server" + " v" + text;
		}
	}

	private static void Main(string[] P_0)
	{
		Console.Title = ApplicationTitle;
		Console.WriteLine(string.Empty);
		ServerArguments serverArguments = CommandParser.ProcessArguments<ServerArguments>(P_0);
		server = new Server(serverArguments.MaxPlayers, serverArguments.Password);
		if (!server.Start(serverArguments.Port))
		{
			Logger.Error($"Server failed to start on port {serverArguments.Port}");
			return;
		}
		Logger.Info($"Server has been started on port {serverArguments.Port}");
		Logger.Info("Press 'esc' to exit");
		while (true)
		{
			if (Console.ReadKey(true).Key == ConsoleKey.Escape)
			{
				Logger.Info("Shutting down server");
				Environment.Exit(0);
			}
		}
	}

	public static TeamInfo getTeamData(byte P_0)
	{
		if (teamGameDatas.ContainsKey(P_0))
		{
			return teamGameDatas[P_0];
		}
		Logger.Info("Creating new game data for team " + P_0);
		TeamInfo teamInfo = new TeamInfo();
		teamGameDatas.Add(P_0, teamInfo);
		return teamInfo;
	}

	public static void removeUnusedGameData(Dictionary<string, PlayerInfo> P_0)
	{
		for (byte b = 1; b <= 10; b++)
		{
			if (teamGameDatas.ContainsKey(b))
			{
				bool flag = false;
				foreach (PlayerInfo value in P_0.Values)
				{
					if (value.team == b)
					{
						flag = true;
						break;
					}
				}
				if (!flag)
				{
					Logger.Info("Removing game data for team " + b);
					teamGameDatas.Remove(b);
				}
			}
		}
	}
}

using System;
using System.Collections.Generic;

namespace Blasphemous.Multiplayer.Server.Models;

public class TeamInfo
{
	private readonly Dictionary<string, byte>[] progressSets;

	public TeamInfo()
	{
		progressSets = new Dictionary<string, byte>[14];
		for (int i = 0; i < 14; i++)
		{
			progressSets[i] = new Dictionary<string, byte>();
		}
	}

	public bool AddTeamProgress(string P_0, byte P_1, byte P_2)
	{
		Dictionary<string, byte> dictionary = progressSets[P_1];
		if (!dictionary.ContainsKey(P_0))
		{
			dictionary.Add(P_0, P_2);
			return true;
		}
		byte b = dictionary[P_0];
		if (P_2 > b)
		{
			dictionary[P_0] = P_2;
			return true;
		}
		return false;
	}

	public Dictionary<string, byte> GetTeamProgressSet(int P_0)
	{
		if (P_0 >= 0 && P_0 < progressSets.Length)
		{
			return progressSets[P_0];
		}
		throw new ArgumentOutOfRangeException("Tried to get an invalid team progress set: " + P_0);
	}

	public byte GetTeamProgressValue(int P_0, string P_1)
	{
		if (GetTeamProgressSet(P_0).TryGetValue(P_1, out var result))
		{
			return result;
		}
		return 0;
	}
}

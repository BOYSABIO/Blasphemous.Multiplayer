using Newtonsoft.Json;

namespace Blasphemous.Multiplayer.Client.Network;

public readonly struct ConnectionInfo
{
	public string ServerIp { get; }

	public string RoomName { get; }

	public string PlayerName { get; }

	public string Password { get; }

	public byte TeamNumber { get; }

	[JsonConstructor]
	public ConnectionInfo(string serverIp, string roomName, string playerName, string password, byte teamNumber)
	{
		ServerIp = serverIp ?? string.Empty;
		RoomName = roomName ?? string.Empty;
		PlayerName = playerName ?? string.Empty;
		Password = password ?? string.Empty;
		TeamNumber = teamNumber;
	}

	public ConnectionInfo()
	{
		ServerIp = "localhost";
		RoomName = string.Empty;
		PlayerName = string.Empty;
		Password = string.Empty;
		TeamNumber = 1;
	}
}

using Blasphemous.ModdingAPI.Persistence;
using Blasphemous.Multiplayer.Client.Network;

namespace Blasphemous.Multiplayer.Client;

public class MultiplayerGlobalData : GlobalSaveData
{
	public ConnectionInfo LastConnection { get; set; }
}

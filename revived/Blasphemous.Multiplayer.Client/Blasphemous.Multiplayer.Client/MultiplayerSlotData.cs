using System.Collections.Generic;
using Blasphemous.ModdingAPI.Persistence;

namespace Blasphemous.Multiplayer.Client;

public class MultiplayerSlotData : SlotSaveData
{
	public List<string> InteractedPersistenceObjects { get; set; }
}

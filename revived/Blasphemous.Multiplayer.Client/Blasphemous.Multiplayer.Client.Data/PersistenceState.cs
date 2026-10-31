using System;

namespace Blasphemous.Multiplayer.Client.Data;

[Serializable]
public class PersistenceState
{
	public string id;

	public string scene;

	public byte type;

	public PersistenceState(string id, string scene, byte type)
	{
		this.id = id;
		this.scene = scene;
		this.type = type;
	}
}

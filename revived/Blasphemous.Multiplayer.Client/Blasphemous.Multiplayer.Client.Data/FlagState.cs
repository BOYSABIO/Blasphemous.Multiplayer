using System;

namespace Blasphemous.Multiplayer.Client.Data;

[Serializable]
public class FlagState
{
	public string id;

	public string notification;

	public FlagState(string id, string notification)
	{
		this.id = id;
		this.notification = notification;
	}
}

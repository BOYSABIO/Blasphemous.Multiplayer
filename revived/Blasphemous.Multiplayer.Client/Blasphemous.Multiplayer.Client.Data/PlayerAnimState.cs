using System;

namespace Blasphemous.Multiplayer.Client.Data;

[Serializable]
public class PlayerAnimState
{
	public string name;

	public string[] parameterNames;

	public bool[] parameterValues;

	public PlayerAnimState(string name, string[] parameterNames, bool[] parameterValues)
	{
		this.name = name;
		this.parameterNames = parameterNames;
		this.parameterValues = parameterValues;
	}
}

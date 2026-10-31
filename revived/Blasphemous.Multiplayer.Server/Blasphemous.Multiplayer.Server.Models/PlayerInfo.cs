using System.Text;

namespace Blasphemous.Multiplayer.Server.Models;

public class PlayerInfo
{
	public string name;

	public byte team;

	public string room;

	public float xPos;

	public float yPos;

	public bool facingDirection;

	public byte animation;

	public byte[] skin;

	public ushort ping;

	public string sceneName;

	public PlayerInfo(string P_0, byte P_1, string P_2 = "")
	{
		name = P_0;
		team = P_1;
		room = P_2 ?? string.Empty;
		sceneName = string.Empty;
		skin = Encoding.UTF8.GetBytes("PENITENT_DEFAULT");
		ping = 0;
	}

	public bool isInSameScene(PlayerInfo P_0)
	{
		if (string.IsNullOrEmpty(sceneName) || sceneName != P_0.sceneName)
		{
			return false;
		}
		if (!string.IsNullOrEmpty(room) || !string.IsNullOrEmpty(P_0.room))
		{
			return room == P_0.room;
		}
		return true;
	}

	public override string ToString()
	{
		return $"{name} is at position ({xPos},{yPos}) facing {(facingDirection ? "right" : "left")} in the animation {animation} in room {room}/{sceneName}";
	}
}

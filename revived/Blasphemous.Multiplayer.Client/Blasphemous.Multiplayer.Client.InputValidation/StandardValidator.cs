using System.Linq;
using System.Text;

namespace Blasphemous.Multiplayer.Client.InputValidation;

internal class StandardValidator : ISanitizer, IValidator
{
	private const int SERVER_LENGTH = 64;

	private const int ROOM_LENGTH = 16;

	private const int PLAYER_LENGTH = 16;

	private const int PASSWORD_LENGTH = 32;

	private const string SERVER_CHARS = "-:.";

	private const string ROOM_CHARS = "_-";

	private const string PLAYER_CHARS = "_-.' ";

	private const string PASSWORD_CHARS = "_-.";

	public string CleanServer(string text)
	{
		return CleanStringValue(text, 64, "-:.");
	}

	public bool IsServerValid(string text)
	{
		bool flag = !string.IsNullOrEmpty(text) && text.Length <= 64 && text.All((char c) => char.IsLetterOrDigit(c) || "-:.".Contains(c));
		if (flag)
		{
			flag = text.Count((char c) => c == ':') switch
			{
				0 => true, 
				1 => ushort.TryParse(text.Substring(text.IndexOf(':') + 1), out var _), 
				_ => false, 
			};
		}
		return flag;
	}

	public string CleanRoom(string text)
	{
		return CleanStringValue(text, 16, "_-");
	}

	public bool IsRoomValid(string text)
	{
		if (!string.IsNullOrEmpty(text) && text.Length <= 16)
		{
			return text.All((char c) => char.IsLetterOrDigit(c) || "_-".Contains(c));
		}
		return false;
	}

	public string CleanPlayer(string text)
	{
		return CleanStringValue(text, 16, "_-.' ");
	}

	public bool IsPlayerValid(string text)
	{
		if (!string.IsNullOrEmpty(text) && text.Length <= 16)
		{
			return text.All((char c) => char.IsLetterOrDigit(c) || "_-.' ".Contains(c));
		}
		return false;
	}

	public string CleanPassword(string text)
	{
		return CleanStringValue(text, 32, "_-.");
	}

	public bool IsPasswordValid(string text)
	{
		if (!string.IsNullOrEmpty(text))
		{
			if (text.Length <= 32)
			{
				return text.All((char c) => char.IsLetterOrDigit(c) || "_-.".Contains(c));
			}
			return false;
		}
		return true;
	}

	public int CleanTeam(int value)
	{
		return CleanIntegerValue(value, 1, 8);
	}

	public bool IsTeamValid(int value)
	{
		if (value >= 0)
		{
			return value <= 8;
		}
		return false;
	}

	private string CleanStringValue(string text, int maxLength, string validChars)
	{
		StringBuilder stringBuilder = new StringBuilder(text.Length);
		foreach (char c in text)
		{
			if (stringBuilder.Length < maxLength && (char.IsLetterOrDigit(c) || validChars.Contains(c)))
			{
				stringBuilder.Append(c);
			}
		}
		return stringBuilder.ToString();
	}

	private int CleanIntegerValue(int value, int min, int max)
	{
		if (value < min || value > max)
		{
			return min;
		}
		return value;
	}
}

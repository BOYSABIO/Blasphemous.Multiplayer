namespace Blasphemous.Multiplayer.Client.InputValidation;

internal interface IValidator
{
	bool IsServerValid(string text);

	bool IsRoomValid(string text);

	bool IsPlayerValid(string text);

	bool IsPasswordValid(string text);

	bool IsTeamValid(int value);
}

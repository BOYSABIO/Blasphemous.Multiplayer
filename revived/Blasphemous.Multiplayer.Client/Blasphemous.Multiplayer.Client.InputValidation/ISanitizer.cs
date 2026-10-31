namespace Blasphemous.Multiplayer.Client.InputValidation;

internal interface ISanitizer
{
	string CleanServer(string text);

	string CleanRoom(string text);

	string CleanPlayer(string text);

	string CleanPassword(string text);

	int CleanTeam(int value);
}

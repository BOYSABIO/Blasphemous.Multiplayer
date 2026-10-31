namespace Blasphemous.Multiplayer.Client.ProgressSync.Helpers;

public interface IProgressHelper
{
	void ApplyProgress(ProgressUpdate progress);

	void SendAllProgress();

	string GetProgressNotification(ProgressUpdate progress);
}

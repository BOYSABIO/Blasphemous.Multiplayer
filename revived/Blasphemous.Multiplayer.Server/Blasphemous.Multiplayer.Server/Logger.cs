using System;

namespace Blasphemous.Multiplayer.Server;

public static class Logger
{
	public static void Info(object P_0)
	{
		Console.ForegroundColor = ConsoleColor.White;
		Print(P_0);
	}

	public static void Warn(object P_0)
	{
		Console.ForegroundColor = ConsoleColor.Yellow;
		Print(P_0);
	}

	public static void Error(object P_0)
	{
		Console.ForegroundColor = ConsoleColor.Red;
		Print(P_0);
	}

	public static void ProgressGood(object P_0)
	{
		Console.ForegroundColor = ConsoleColor.Green;
		Print(P_0);
	}

	public static void ProgressBad(object P_0)
	{
		Console.ForegroundColor = ConsoleColor.DarkGreen;
		Print(P_0);
	}

	public static void Print(object P_0)
	{
		Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {P_0}");
	}
}

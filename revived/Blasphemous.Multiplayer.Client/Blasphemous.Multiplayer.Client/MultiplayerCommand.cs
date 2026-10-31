using System;
using System.Collections.Generic;
using System.Globalization;
using Blasphemous.CheatConsole;
using Blasphemous.ModdingAPI;
using Blasphemous.Multiplayer.Client.Enemies;
using Blasphemous.Multiplayer.Client.PvP.Models;

namespace Blasphemous.Multiplayer.Client;

public class MultiplayerCommand : ModCommand
{
	protected override string CommandName => "multiplayer";

	protected override bool AllowUppercase => true;

	protected override Dictionary<string, Action<string[]>> AddSubCommands()
	{
		return new Dictionary<string, Action<string[]>>
		{
			{ "help", Obsolete },
			{ "status", Obsolete },
			{ "connect", Obsolete },
			{ "disconnect", Obsolete },
			{ "players", Obsolete },
			{ "enemy", Enemy }
		};
	}

	private void Obsolete(string[] parameters)
	{
		Write("The multiplayer command has been removed.  Open the connection menu on the bottom of the screen.");
		Write("Enemy sync test: multiplayer enemy hit [dmg] | enemy die | enemy ids");
	}

	private void Enemy(string[] parameters)
	{
		if (parameters == null || parameters.Length == 0)
		{
			Write("Usage: multiplayer enemy hit [dmg] | enemy die | enemy ids");
			return;
		}
		EnemyManager enemyManager = Main.Multiplayer?.EnemyManager;
		if (enemyManager == null)
		{
			Write("EnemyManager not ready");
			return;
		}
		switch (parameters[0].ToLowerInvariant())
		{
		case "hit":
		{
			float amount = 25f;
			if (parameters.Length >= 2 && float.TryParse(parameters[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var result))
			{
				amount = result;
			}
			Write(enemyManager.DebugFakeHit(amount));
			break;
		}
		case "die":
		case "death":
			Write(enemyManager.DebugFakeDeath());
			break;
		case "ids":
		case "id":
			enemyManager.LogEnemyIdTable("console");
			Write("Enemy id table written to BepInEx log ([EnemySync] IdTable).");
			break;
		default:
			Write("Unknown enemy subcommand '" + parameters[0] + "'. Use hit | die | ids");
			break;
		}
	}

	private void Damage(string[] parameters)
	{
		if (ValidateParameterList(parameters, 2))
		{
			AttackType attackType = (AttackType)Enum.Parse(typeof(AttackType), parameters[0]);
			ValidateIntParameter(parameters[1], 0, 255, out var result);
			ModLog.Warn($"Testing attack {attackType} with damage {result}");
			Main.Multiplayer.AttackManager.DamagePlayer_Internal(attackType, (byte)result);
		}
	}
}

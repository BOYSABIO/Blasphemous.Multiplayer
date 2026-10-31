# Shared combat testing (protocol 13)

All players and the server must run version 2.4.2 from this fork. Protocol 12 and older clients are refused.

## What this build implements

Room enemies have one scene host. That host runs AI and accepts complete sword and prayer hits from other clients. Other clients mirror position, health, orientation and animation. Real enemy hit volumes on the host deliver hits to remote players; the receiving game still applies its normal dodge, block, parry and resistance rules. The old distance-based damage pulses are removed.

The investigation found that patching `DamageArea.TakeDamage` only blocked the base event, while `EnemyDamageArea` still continued into `Entity.Damage`. That forwarded one swing twice. Hits now enter through each enemy's complete `IDamageable` method. TCP streams also retain incomplete frames rather than dropping partial combat packets.

Bosses share host-authoritative health and damage. Each client keeps its own boss AI, arena scripts, projectiles and cutscenes running. Boss attacks and positions can therefore differ between screens. This is experimental cooperative boss damage, not complete synchronization of every boss encounter. Both players should enter the arena before attacking. Multi-part bosses, scripted invulnerability and multi-phase encounters need individual in-game verification.

Boss death is replayed through the boss's normal `IDamageable.Damage(Hit)` method so its death sequence can run. Boss objects are never hidden using the regular enemy corpse timer. Live boss snapshots with zero HP are not treated as final death, since a phase transition can temporarily use zero HP.

The first player to connect is the preferred scene host whenever present. When absent, the scene elects the lowest player name using ordinal comparison. The server and clients use the same rule. The legacy `preferredEnemyHost` client setting is ignored to prevent conflicting authorities.

## Automated checks

Requires the .NET 8 SDK. Run from the repository root:

```powershell
dotnet build Blasphemous.Multiplayer.sln -c Release
dotnet run --project tools/EnemyProtocolSmoke
dotnet run --project tools/CombatSyncSmoke
```

`EnemyProtocolSmoke` exercises production codecs and TCP framing, including every split position, bytewise delivery, truncated payloads, UTF-8 identifiers, complete Hit fields and host election. It also starts a temporary local server with three TCP clients and checks room filtering, damage routing, authority enforcement and host changes.

`CombatSyncSmoke` links the production combat manager, patches, Hit converter and enemy wrapper against small headless game doubles. It checks one packet per swing, normal damage replay, boss health and death, corpse tombstones, respawn IDs, unknown deaths, animation timing, host migration and disconnect recovery. These tests do not run Unity or validate actual boss scenes.

## Installing the build

1. Close the game and the old server.
2. Extract `publish/BlasClient.zip` into the game's `Modding` directory on every player's PC. Merge `plugins`, `data` and `localization` directories. Keep the Modding API and Cheat Console installed.
3. Use the new server from `publish/BlasServer-win-x64.zip` (or the framework-dependent `publish/BlasServer.zip`, which requires .NET 8).
4. Start the new server and connect with different player names. Use the same team for cooperative progress sharing.

A normal build writes packages under `publish` and does not overwrite an installed game. Developers can opt in with `-p:DeployToGame=true` and optionally `-p:DevFolder=...`.

## In-game checks: two clients and one server

- Enter an early room with Flagellants or Acolytes. Confirm matching positions and HP. Let each player hit separately, then together.
- Test sword attacks and a prayer. Check that one hit does not count twice and guarded enemies still guard.
- Let an enemy hit each player. Confirm attacks can be dodged and normal defenses still apply. Standing near an inactive enemy must not cause periodic fabricated damage.
- Kill enemies from each client. Leave and re-enter with the other client. Confirm dead enemies do not return and a dead enemy's packet never kills its living neighbour.
- Leave a room as the host while the other player remains. Confirm AI and attacks resume under the new host. Return and check that authority moves back once.
- Disconnect the guest while enemies are alive. Confirm normal single-player AI, targeting and colliders recover.
- Enter the Warden arena together. Hit from both clients; compare boss HP. Confirm both clients receive the death sequence and the arena exit opens.
- Repeat with Ten Piedad. Then test a boss with several phases and a boss with several body parts. Record the boss, scene, phase and both `BepInEx/LogOutput.log` files for any mismatch.

No live two-player gameplay session was performed by the automated checks. Treat boss support as experimental until these checks pass on both PCs.

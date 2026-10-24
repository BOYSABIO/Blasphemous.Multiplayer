# Room enemy sync — smoke checklist

## Automated
- `dotnet run --project tools/EnemyProtocolSmoke` — packet layout (protocol v4)

## In-game (two clients + BlasServer)

Both clients must run this fork (protocol 4). Old 2.2.2 clients will be refused.

1. Start `BlasServer` (Release build under `Blasphemous.Multiplayer.Server/bin/Release/net8.0/`)
2. Launch two Blasphemous instances (or two PCs) with this `BlasClient.dll`
3. Connect both to the same server (different names)
4. Meet in an early room with Flagellants / Acolytes (e.g. Mercy Dreams / Holy Line)
5. Confirm:
   - [ ] Both see the same enemy positions (non-host puppets track host)
   - [ ] Either player can damage; HP drops on both screens
   - [ ] Enemy dies once (no second local clone still fighting)
   - [ ] Late joiner entering the room gets living enemies via snapshots
   - [ ] Log lines include `[EnemySync] Register` / `sceneHost=`

Boss rooms are out of scope for this phase.

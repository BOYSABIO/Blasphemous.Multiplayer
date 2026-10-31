# Prepared Blasphemous co-op candidate: 2.8.2

The existing installed mod is 2.8.1, protocol 16. The source workspace and its
`publish/BlasClient.zip` are 2.4.2, protocol 13. Those packages are incompatible.
Do not use the workspace's old packages to update a protocol-16 installation.

The prepared candidate uses the author's
[2.8.2 release](https://github.com/BOYSABIO/Blasphemous.Multiplayer/releases/tag/v2.8.2).
It fixes a shadow-access exception which is present in the existing 2.8.1 game
log. Its downloaded client and server archives passed SHA-256 verification.

## Scope and current evidence

- Shared player visibility, progress and ordinary room enemies are the intended
  features. Actual Unity gameplay has not been tested in this session.
- **Bosses are excluded from this release's enemy synchronization.** Full shared
  boss combat is unfinished work. The experimental boss-health code described
  elsewhere in this workspace belongs to a different build.
- The exact published 2.8.2 server passed 19 scripted TCP checks using its own
  codecs: connections, version rejection, positions, combat relay, room filtering,
  authority, host migration and stored deaths. A deliberately omitted damage
  packet made the test fail, confirming that the damage check detects a failure.
- No real game clients were launched. Inventory, world state, AI behavior,
  animations, boss encounters and save isolation have not passed live QA.
- The existing game installation and saves have not been modified.

## Prepared files

`publish/Blasphemous-Coop-2.8.2-candidate.zip` contains the original client archive,
the extracted server, this guide, a server launcher, hashes and the smoke test.
It contains no game files, saves or personal configuration. The client still
requires the Modding API and Cheat Console listed in the
[upstream installation instructions](https://github.com/BrandenEK/Blasphemous.Multiplayer#installation).
Those dependencies already exist in the inspected installation.

## Planned setup after approval

1. Prepare isolated copies of the owned game and isolated save/configuration
   locations. Keep the original install and saves unchanged. Installing the mod
   and using Steam require the owner's approval under Co-op Anything AGENTS.md.
2. In each isolated copy, back up the files to be replaced, then merge the client
   archive's `plugins`, `data` and `localization` directories into `Modding`.
3. Every player uses the same 2.8.2 client. The host uses the included 2.8.2 server.
4. Run `Start-Server.cmd` without additional arguments. The tested server listens
   on **TCP 27051**, with an empty password. Its default player limit has not
   been independently verified.
   Its command-line options currently crash during argument parsing; custom
   ports, passwords and player limits have not been validated.
5. In the in-game connection panel, the host uses `localhost:27051`. Other PCs use
   the host's reachable address followed by `:27051`. Choose unique player names
   and the same team, such as team 1, for shared progress. Keep friendly fire off.
6. LAN players use the host's LAN address. Across the internet this release needs
   a working private-network connection or TCP forwarding; Steam friend invites
   and relay transport are not implemented. No network settings were changed.

Automated gameplay validation is the next step before calling this candidate
playable. It must use in-game bots and compare at least two real clients, rather
than ask the owner to playtest.

## Repeat the network check

With the included server already running, execute in PowerShell:

```powershell
.\Test-ReleaseProtocol.ps1 -ReleaseDirectory .
```

The test only connects to localhost and needs no .NET SDK. A successful run
prints `ReleaseProtocolSmoke: PASS (19 checks; ...)`. Press Escape in the server
window afterward. These results establish network behavior, not gameplay QA.

## Rebuild limitation

The public v2.8.1/v2.8.2 tags point to source reporting 2.4.1. They do not identify
the matching source for these release binaries. Obtain that source before trying
to extend the published build. Preserve the current uncommitted workspace work.

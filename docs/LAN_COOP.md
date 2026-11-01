# Blasphemous co-op on separate PCs on the same LAN

The source builds an **experimental 2.9.0 / protocol-17 candidate**. The Warden
is the only boss admitted to synchronization; other bosses remain excluded and
full-campaign co-op is unsupported. Gameplay verification is still in progress.
The scripts below accept the matching server folder; they do not select or
install an older build automatically.

Every player needs an owned game copy, the same client package, Modding API and
Cheat Console. The host also needs the matching server. Keep co-op testing in
isolated game/save copies; the normal install and saves remain unchanged.

Prepare each player's separate copy using the package's self-contained
`Setup.exe` builder and `payload-manifest.json`:

```powershell
.\Setup.exe --game-directory '<owned game directory with prerequisites>' `
  --destination '<new co-op directory>' --payload-manifest .\payload-manifest.json
```

The builder checks the supported game identity, base loader, Modding API,
Console and payload hashes before copying; see
[player-copy setup](../tools/CoopPlaySetup/README.md) for prerequisites and limits.
Then launch `Play-Coop.cmd` inside that new copy. Its saves are in `saves/` under
the copy. Keep Steam running with the owned game. Each PC supplies its own game;
the mod package does not contain game files.

## Host

From the package folder in PowerShell:

```powershell
.\tools\CoopLan\Start-CoopHost.ps1 -ServerDirectory .\server -MaxPlayers 4 -Password coopparty
```

The script prints the server version, the host player's `localhost:27051`
address, and LAN IPv4 addresses for friends. Pick the address on the same Wi-Fi
or Ethernet network as the other PCs. Addresses are detected at runtime and are
not stored in the repository. The server remains in this console; press Escape
to stop it.

The default port is **TCP 27051**. If it is already occupied, the script leaves
the existing process alone. Choose another unused port with `-Port`, then use
that same port on every client. `-MaxPlayers` controls the server limit.

Passwords must fit the in-game field: up to 32 letters, digits, underscores,
dots or hyphens. An omitted password means an empty password. The script does
not print it. Set the same password on each client.

## Friends join

Use the host's printed LAN address, not `localhost`. This example uses a
placeholder; replace it with the address printed by the host:

```powershell
.\tools\CoopLan\Join-CoopLan.ps1 -HostAddress <host-LAN-IPv4> -Port 27051 -CheckConnection
```

The optional connection check opens a temporary TCP connection and closes it.
It establishes reachability only; the game's version/protocol/password
handshake is checked when joining in-game.

Load a level in each isolated co-op copy, then click **Multiplayer connection
info** at the bottom of the game window:

1. Host player: Server IP `localhost:27051`.
2. Other players: Server IP `<host-LAN-IPv4>:27051`.
3. Choose different player names and the same team, such as team 1, for shared
   progress. Keep friendly fire off and enter the same server password.
4. Click Connect.

These PCs are on the same LAN. Steam relay, router port forwarding and public
IP lookup are not required.

## Optional host firewall rule

No firewall changes happen by default. If Windows blocks inbound connections,
open PowerShell **as Administrator** on the host and use:

```powershell
.\tools\CoopLan\Start-CoopHost.ps1 -ServerDirectory .\server -MaxPlayers 4 -Password coopparty -ConfigureFirewall
```

That explicit switch creates or updates one app-specific inbound rule for this
server executable, the selected TCP port, **Private** profile and **LocalSubnet**
remote addresses. It does not change network categories, other rules, router
configuration, firewall defaults or service settings. Without elevation it
stops with an actionable message. `-WhatIf` previews the firewall and server
actions without performing them.

The rule only applies while the trusted LAN is marked Private in Windows
network settings. The script reports when no active Private profile is found
and leaves the setting unchanged. If addresses and port match but guests still
cannot connect, check whether a guest Wi-Fi network or access-point isolation
prevents PCs from reaching each other.

The host script requires `server/coop-release.json`, verifies the executable's
SHA-256 and prints the matching version/protocol. A missing manifest or failed
hash check stops startup. A TCP connection alone is not gameplay verification.

## Portable Windows server preparation

The source build uses pinned .NET dependencies. A self-contained, untrimmed
Windows x64 server can include its runtime, so friends do not need a separate
.NET SDK or runtime installation. Do not trim the server: its command parser
sets option properties through reflection.

`tools/CoopLan/Publish-CoopServer.ps1` publishes that single-file server and
creates a hash manifest with `gameplayVerified: false`. It checks the compiled
common assembly's protocol against the explicitly supplied value. The output
directory must be new or empty. It does not start the server or mark gameplay as
verified. The currently maintained candidate uses protocol 17:

```powershell
.\tools\CoopLan\Publish-CoopServer.ps1 `
  -ServerProject .\revived\Blasphemous.Multiplayer.Server\Blasphemous.Multiplayer.Server.csproj `
  -OutputDirectory .\local\server-build -ExpectedProtocol 17 `
  -DotnetPath .\local\tools\dotnet\dotnet.exe -PackageCache .\artifacts\packages
```

This developer example uses the workspace's pinned SDK 8.0.426 and cached
runtime/apphost 8.0.32. It has built successfully offline. `-PackageCache` limits
restore to that existing local source; omit it for the project's normal NuGet
sources. The publish settings explicitly disable trimming and debug symbols.
Final shipping must use the exact client/server version and protocol after
gameplay checks pass. Protocol-16 baseline clients cannot join this protocol-17
server.

No game assemblies, private paths, saves, reference dumps or QA bot plugins
belong in that portable server package. Gameplay verification and the final
player package remain pending; these instructions do not claim the development
build is ready for a playthrough.

## Reproducible candidate package

After building matching protocol-17 components, a developer can assemble a new
local candidate folder:

```powershell
.\tools\CoopLan\Package-CoopRelease.ps1 `
  -ClientBuildDirectory '<matching client bin/Release/net35>' `
  -ServerDirectory '<self-contained server with coop-release.json>' `
  -SetupExecutable '<self-contained CoopPlaySetup.exe>' `
  -OutputDirectory '<new candidate directory>'
```

The builder checks numeric client/common/server versions, protocol 17 and the
server hash before writing anything. It selects exactly seven mod files for
`BlasClient.zip`, using the published plugin/data/localization layout. Build
references are excluded even when present in the input directory. ZIP entry
order and timestamps are fixed. It copies the server and setup executables,
host/join helpers and license, then records their SHA-256 values.

The generated README identifies the Warden adapter as experimental and says
that other bosses remain excluded and full-campaign co-op is unsupported. Both
package manifests keep `gameplayVerified: false`; creating a package does not
establish gameplay or remote-PC LAN results. Existing nonempty output folders
are refused, and partial failed outputs are kept for inspection.

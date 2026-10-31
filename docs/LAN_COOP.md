# Blasphemous co-op on separate PCs on the same LAN

The planned package is a **2.9 development candidate, unverified**. Its final
version, protocol and gameplay evidence are still pending boss-sync work. The
scripts below accept the final matching server folder; they do not select or
install an older build automatically.

Every player needs an owned game copy, the same client package, Modding API and
Cheat Console. The host also needs the matching server. Keep co-op testing in
isolated game/save copies; the normal install and saves remain unchanged.

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

Load each isolated co-op copy and open its multiplayer connection panel:

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

If the package provides `server/coop-release.json`, the host script verifies
the executable's SHA-256 and prints the matching version/protocol. A failed
hash check stops startup. A TCP connection alone is not gameplay verification.

## Portable Windows server preparation

The source build uses pinned .NET dependencies. A self-contained, untrimmed
Windows x64 server can include its runtime, so friends do not need a separate
.NET SDK or runtime installation. Do not trim the server: its command parser
sets option properties through reflection.

The workspace already caches the .NET 8.0.32 Windows runtime pack and apphost,
so an offline feasibility build can use SDK 8.0.426 with
`RuntimeFrameworkVersion=8.0.32`, `PublishSingleFile=true`,
`IncludeNativeLibrariesForSelfExtract=true`, `EnableCompressionInSingleFile=true`
and `PublishTrimmed=false`. Final shipping must use the exact client/server
version and protocol after gameplay checks pass.

No game assemblies, private paths, saves, reference dumps or QA bot plugins
belong in that portable server package. Final packaging remains pending; these
instructions do not claim the development build is ready for a playthrough.

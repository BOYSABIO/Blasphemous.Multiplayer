# Recovered Blasphemous Multiplayer 2.9.0-dev

This separate solution extends the protocol-16 baseline recovered from the published
[BOYSABIO v2.8.2 release](https://github.com/BOYSABIO/Blasphemous.Multiplayer/releases/tag/v2.8.2).
It is recovered MIT mod source, decompiled with ILSpy 9.1 and repaired for a
normal build. It is **not a byte-identical rebuild or the author's original
source revision**. The experimental client and server now use **protocol 17**
for a Warden-only boss adapter. Other bosses remain excluded. This is development
source, not a completed campaign co-op release. The root solution and its
pre-existing protocol-13 changes remain the legacy development tree.

## Provenance

The public v2.8.1 and v2.8.2 tags and their assembly informational versions point
to `b85cc879c801667b159dad28c5e7acfb0b1f293f`, but that commit reports source
version 2.4.1. Checking out those tags does not reproduce the release binaries.
This recovery instead used the release assets whose published SHA-256 digests
were verified:

| v2.8.2 input | SHA-256 |
| --- | --- |
| `Blasphemous.Multiplayer-EnemySync-2.8.2.zip` | `c548796aff1bb37f9ebf99386c17cb760a076528cf75cd99661f154546ac5476` |
| `BlasServer.zip` | `c7854d02c4eac7d5778210d8a80deddd1eacc18b11aba0831bf2c5c5dc3cdb5a` |

Only the multiplayer mod, codecs and server source are included. No game files,
reference DLL dumps, saves, local paths or debug symbols were copied here. The
upstream MIT license and attribution are retained in `LICENSE.md`.

## Build

Requires the .NET 8 SDK; SDK 8.0.426 was used for validation. From the repository
root:

```powershell
dotnet build revived/Blasphemous.Multiplayer.Revived.sln -c Release
```

Dependencies are pinned to official NuGet packages: GameLibs 4.0.67, ModdingAPI
3.0.0, CheatConsole 1.1.0 and Basalt.CommandParser 2.0.0. SimpleTCP uses the
existing repository resource `resources/data/SimpleTCP.dll`. No installed game
directory is needed for compilation.

For the current workspace's offline cache and isolated SDK:

```powershell
$env:NUGET_PACKAGES = Join-Path (Get-Location) 'artifacts/packages'
$env:DOTNET_CLI_HOME = Join-Path (Get-Location) 'local/dotnet-home'
./local/tools/dotnet/dotnet.exe build revived/Blasphemous.Multiplayer.Revived.sln -c Release --source $env:NUGET_PACKAGES -m:1 -p:UseSharedCompilation=false
```

Builds write only their project `bin`/`obj` directories. There are no deployment
targets and building does not overwrite the installed game or the root
solution's published packages. Numeric plugin version `2.9.0` and informational
version `2.9.0-dev` are defined in `Directory.Build.props`; the client plugin
metadata is generated from the numeric version.

## Repairs carried forward

- Restored public setters on the server's port, player limit and password
  options. They were absent from the trimmed published DLL, so reflective
  argument parsing crashed. This project defaults to an untrimmed server.
- Replaced private `BlobShadow.blobSpriteRenderer` access with public
  `GetComponentsInChildren<SpriteRenderer>(true)` toggles.
- Removed compiler-reserved metadata and incomplete compiler helper output;
  replaced the one-item player-display collection helper with an equivalent
  array. These are recovery repairs rather than new game logic.
- Added explicit Warden encounter and typed Area/Mace hazard replication,
  bounded server replay caches, authority terms, exact prefab selection, and
  native boss health/death paths. Replicas suppress local AI and outbound hazard
  damage; promotion restores their native controls and collision state.
- Replaced unsupported `System.IO.InvalidDataException` with `IOException` in
  the boss codec. The former exists in normal .NET build reference assemblies
  but is absent from this game's stripped Mono framework. Referencing it in an
  exception handler caused a native Mono crash. Compilation and headless .NET
  tests alone could not detect that runtime-profile mismatch.

## Verification and limits

This reorganized solution built successfully offline with SDK 8.0.426: zero
errors and two existing unused-variable warnings in the client. Common and
server built without warnings.

The maintained headless suite runs against this solution's actual codec and
server cache logic:

```powershell
dotnet run --project tools/BossProtocolSmoke/BossProtocolSmoke.csproj -c Release
```

Its 49 checks pass, including 300 deterministic valid roundtrips and 10,000
malformed packets. It covers authority rejection, sequences, cache bounds,
despawns, replay receipt age, and expiry without starting a game or server.
The earlier recovered baseline also passed 19 protocol-16 wire checks and six
checks of custom port, password and player limit. Those earlier results are
baseline evidence, not validation of this protocol-17 build.

Two-client Unity tests, native encounter events, physical melee and hazard
collisions, phases, death, late joining and migration require separate gameplay
evidence. In particular, the latest private full Warden test found health and
AI resetting when the preferred session host returned to the arena; that
server now keeps the surviving scene owner while that player remains present.
Session-owner preference applies only to initial and replacement elections.
The updated return path still needs its gameplay regression. Do not describe
this source as a finished boss or campaign co-op release.

Actual game-member and framework-type audits are also required against the
supported game build, because build-time publicized game libraries and full
.NET reference assemblies can expose members or types the installed runtime
does not contain. The maintained build has passed those focused static audits;
they do not replace Unity tests.

All players and the server must use a matched protocol-17 set. The root
protocol-13 boss-health experiment and older protocol-16 releases remain
separate implementations; version labels alone do not establish compatibility.

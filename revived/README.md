# Recovered Blasphemous Multiplayer 2.8.3

This separate solution preserves protocol 16 from the published
[BOYSABIO v2.8.2 release](https://github.com/BOYSABIO/Blasphemous.Multiplayer/releases/tag/v2.8.2).
It is recovered MIT mod source, decompiled with ILSpy 9.1 and repaired for a
normal build. It is **not a byte-identical rebuild or the author's original
source revision**. The root solution and its pre-existing protocol-13 changes
are left as the legacy development tree.

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
solution's published packages. Version 2.8.3 is defined in
`Directory.Build.props`; the client plugin metadata is generated from it.

## Repairs carried forward

- Restored public setters on the server's port, player limit and password
  options. They were absent from the trimmed published DLL, so reflective
  argument parsing crashed. This project defaults to an untrimmed server.
- Replaced private `BlobShadow.blobSpriteRenderer` access with public
  `GetComponentsInChildren<SpriteRenderer>(true)` toggles.
- Removed compiler-reserved metadata and incomplete compiler helper output;
  replaced the one-item player-display collection helper with an equivalent
  array. These are recovery repairs rather than new game logic.

## Verification and limits

This reorganized solution built successfully offline with SDK 8.0.426: zero
errors and two existing unused-variable warnings in the client. Common and
server built without warnings.

The recovered baseline built successfully before this source was reorganized.
Its server passed 19 protocol-16 wire checks using the original published
client's codecs, plus six checks of custom port, password and player limit.
Those results do not prove Unity gameplay or boss synchronization. Gameplay
work and its evidence must be recorded separately before claiming playability.

The baseline still excludes bosses from enemy synchronization. The root
protocol-13 boss-health experiment is separate code and is not silently mixed
into this recovery. All players and the server must use a compatible protocol;
version labels alone do not establish compatibility.

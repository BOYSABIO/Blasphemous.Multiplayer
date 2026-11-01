# Separate co-op player copy

`CoopPlaySetup` creates a physical, private-profile copy of an owned Blasphemous
installation. It never launches a game, opens a network port, or writes into the
source installation. It refuses an existing nonempty destination, reparse-point
paths, unsupported game identity, unknown payload files and mismatched hashes.

```powershell
local/tools/dotnet/dotnet.exe run --project tools/CoopPlaySetup -- `
  --game-directory '<owned game directory>' `
  --destination '<new co-op directory>' `
  --payload-manifest '<prepared package>/payload-manifest.json'
```

The manifest selects the actual client build rather than assuming a protocol:

```json
{
  "schemaVersion": 1,
  "clientArchive": "BlasClient.zip",
  "clientSha256": "<64 hexadecimal SHA-256 characters>",
  "clientVersion": "2.9.0.0",
  "protocol": 17
}
```

This is a schema example, not a claim that the example build was tested. The
archive path is relative to the manifest. The builder verifies both the archive
digest and the assembly version/protocol inside it. Only the multiplayer plugin,
its common/TCP libraries, JSON data and localization are accepted. Game libraries,
game assets, other mods and QA plugins are rejected.

`clientVersion` is the numeric assembly version, for example `2.9.0`, rather than
a display label such as `2.9.0-dev`. An omitted build or revision number means zero.

A self-contained Windows setup executable can be prepared without trimming:

```powershell
local/tools/dotnet/dotnet.exe publish tools/CoopPlaySetup -c Release -r win-x64 `
  --self-contained true -p:RuntimeFrameworkVersion=8.0.32 `
  -p:PublishSingleFile=true -p:PublishTrimmed=false `
  -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None `
  -m:1 -nr:false -p:UseSharedCompilation=false --disable-build-servers `
  -o '<setup output directory>'
```

Ship the generated `CoopPlaySetup.exe` with the payload manifest and client archive.
A separate .NET installation is unnecessary. A prepared dependency bundle also
supports an unmodded owned game:

```powershell
CoopPlaySetup.exe --game-directory '<owned game directory>' `
  --destination '<new co-op directory>' `
  --payload-manifest '<package>/payload-manifest.json' `
  --dependency-manifest '<package>/dependencies/dependencies.json'
```

The optional bundle pins BepInEx 5.4.21 x64, MultiFolderLoader 1.3.1, Modding API
3.0.1 and Cheat Console 1.1.0 to official release URLs and SHA-256 digests. Setup
checks every digest, managed assembly identity, archive path and required notice
before creating the destination. It extracts these components only into the new
physical copy. It does not execute an installer, download files or import the
source's existing mods, configuration, logs or saves.

Without `--dependency-manifest`, the source must already have BepInEx 5, Modding
API >=3.0.0, Cheat Console >=1.1.0 and MultiFolderLoader. Missing prerequisites
fail before copying, with the
[official installer link](https://github.com/BrandenEK/Blasphemous.Modding.Installer).

Package maintainers prepare the pinned bundle with:

```powershell
./tools/CoopPlaySetup/Prepare-CoopDependencies.ps1 -OutputDirectory '<new bundle directory>'
```

The preparer downloads only its nine pinned release assets, license notices and
corresponding source archive. `-AssetDirectory '<verified cache>'` copies and
checks the same inputs offline. Its output is `dependencies.json` plus the named
files. Existing nonempty outputs are refused. For package acceptance,
`CoopPlaySetup.exe --check-dependencies '<bundle>/dependencies.json'` runs the
production bundle validator without creating a game copy.

The dependency manifest has schema version 1 and `assets` entries containing
`id`, `file`, `sha256`, and `url`; `notices` omit `id`. Paths are relative to the
manifest. Exact pins and required notices are recorded in
`Prepare-CoopDependencies.ps1` and enforced by `DependencyBundle.cs`. Release-tag
license texts and source provenance travel with the bundle; the pinned BepInEx
5.4.21 tag's own license is MIT.

The new copy has:

- Native Unity product identity `BlasCoopLAN`, isolating native product preferences.
- Save and Modding API persistence rooted at `<co-op copy>/saves/`.
- An inert restart-via-Steam helper, preventing a relaunch of the normal install.
- Inert Steam remote-storage APIs for this separate local profile.
- **Unchanged Steam achievements and user-stat methods.** The QA local-achievement
  factory and QA stat stubs are not applied to player copies.
- Cooperative defaults: PvP/friendly fire off, progress sharing on.
- `Play-Coop.cmd`, which sets the copy's working directory and a profile-local log.
- Verified backups and hashes of every patched pre-existing file under
  `coop-profile/setup-backups` and `coop-profile/setup-manifest.json`.

Steam initialization is retained for the owned game. Native Steam client
Auto-Cloud behavior requires observation; no account settings are changed. The
private save path is outside the original product directory. Existing normal
saves are not imported. Setup leaves partial destinations for inspection after
failure instead of deleting unknown files or overwriting an existing directory.

The executable is pinned to the recorded Windows SHA-256
`3FF1A014FD0366747FB6D283C9494A5A8CFF9BC19239FE97B9DC12D831369477`.
The UnityFS parser also verifies the observed UnityFS v6/SerializedFile v17
PlayerSettings layout before copying. `UnityBundle.cs` derives directly from the
runtime-verified `CoopQa.Isolation` parser. It is duplicated here to keep the QA
project unchanged; no QA patch paths are included in this player builder.

The player package should contain this builder and its runtime/dependencies,
the mod-only client archive, payload manifest, license, server package and clear
host/join instructions. It must contain no game files, private profiles, saves,
QA plugins or test-slot backups. Publishing and distribution are separate from
creating a local player copy.

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
  "clientVersion": "2.8.4",
  "protocol": 17
}
```

This is a schema example, not a claim that the example build was tested. The
archive path is relative to the manifest. The builder verifies both the archive
digest and the assembly version/protocol inside it. Only the multiplayer plugin,
its common/TCP libraries, JSON data and localization are accepted. Game libraries,
game assets, other mods and QA plugins are rejected.

The source needs BepInEx 5, Modding API >=3.0.0, Cheat Console >=1.1.0 and the
MultiFolderLoader patcher. A clean install fails before copying, with the
[official installer link](https://github.com/BrandenEK/Blasphemous.Modding.Installer).
Prepare those prerequisites in a separate source copy. This tool does not silently
download or run an unpinned installer. Existing source mods/configurations/logs
are not propagated: only the game runtime, base loader, API and Console are copied.

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

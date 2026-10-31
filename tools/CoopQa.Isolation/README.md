# Isolated Blasphemous QA copies

This tool patches only physical game copies under `<workspace>/local/slots/0`
through `9`. It accepts either game files directly in the slot or a `game`
subdirectory. It never starts the game or accesses an account.

```powershell
local/tools/dotnet/dotnet.exe run --project tools/CoopQa.Isolation -- --slot-directory local/slots/0
```

The tool verifies the copied executable against `local/identity.json`, refuses
hardlinked/reparse-point targets, and backs up and hashes every changed file.
It changes serialized PlayerSettings' native productName to `BlasCoopQA0` so
native Unity preferences use a separate product identity. The original game's
preferences are not copied. It changes the save-path function to `qa-saves/`
inside the slot, including the Modding API's global and slot data.

The achievement factory uses the local helper, Steam user-stat mutators are
inert, and Steam remote-storage methods return empty/default values. Steam
initialization remains available. The restart-via-Steam helper returns false so
the test copy cannot relaunch the owner's normal installed executable. This
changes no external service or DRM mechanism. This Steam build's GOG
manager already destroys itself without initialization. The slot-local save path
is outside the original product's save directory; native client Auto-Cloud
behavior still requires observation during the live suite.

The UnityFS parser is deliberately limited to the observed format. It parses
SerializedFile v17's class-129 PlayerSettings object and changes only the product
string after `TheGameKitchen`; another `Blasphemous` string is left intact.
Only the affected data block is re-encoded (uncompressed). Every other compressed
block is copied unchanged. Reopening the output verifies the product and checks
that the extracted serialized file differs only in those eleven bytes.

Before gameplay tests, an in-game bot must assert `Application.productName` and
`PersistentManager.GetPathAppSettings("")` match the slot's manifest. Launch with
the game-copy directory as its working directory and an explicit slot-local
`-logFile` path. Hash original saves and export original preference values before
and after the suite. Those runtime assertions are required; preparing the copy
alone does not establish a successful isolated game launch.

`qa-isolation.json` records backups and final hashes. Repeated runs verify those
hashes and make no further changes. A partial run fails closed so its backups can
be restored before retrying.

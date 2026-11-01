# Experimental boss protocol checks

Run from the repository root with the .NET 8 SDK:

```powershell
dotnet run --project tools/BossProtocolSmoke/BossProtocolSmoke.csproj -c Release
```

This exercises the maintained `revived/` protocol-17 codec and the server's
actual acceptance/cache logic. It starts no server, opens no sockets, loads no
game files, and changes no saves. Tests use reflection to reach the existing
private cache handlers without adding test entry points to the production API.

The 49 checks cover binary roundtrips, malformed/truncated packets, enum and
floating-point bounds, authority terms and sequences, bounded caches, active
and inactive hazards, replay receipt age, expiry, and stable scene ownership
when a preferred player returns. They also include 300
deterministic valid roundtrips and 10,000 malformed packet inputs. A JSON result
is written beside the built test executable.

These checks do not establish Unity compatibility, collision behavior, boss
encounter completion, or playable campaign co-op. The actual stripped game
framework must be checked separately: a type present in normal .NET reference
assemblies may be absent from the game's runtime.

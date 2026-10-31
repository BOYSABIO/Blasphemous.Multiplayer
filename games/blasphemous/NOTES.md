# Blasphemous co-op investigation

Date: 2026-10-31. This is the mod's source repository, not a checkout of the
Co-op Anything hub. Machine-specific paths, hashes, and logs belong in ignored
`local/`. No account actions or publications have been performed.

## Observations

- Read [Co-op Anything AGENTS.md](https://github.com/cranebeaker/coop-anything/blob/main/AGENTS.md),
  its `coop-research` and `coop-qa` skills, and triage, research and automated-QA playbooks.
- Existing work: [BrandenEK/Blasphemous.Multiplayer](https://github.com/BrandenEK/Blasphemous.Multiplayer),
  MIT license (`docs/LICENSE.md`). Its README advertises player visibility, inventory,
  stats, skills, map and world progress sharing, but explicitly excludes enemy and boss sync.
- The upstream releases page identifies 2.2.2 as latest when inspected. Release
  freshness is independent of the installed fork's version.
- The current workspace contains pre-existing, uncommitted combat changes and
  protocol 13, with version 2.4.2. Existing documentation describes room enemy
  authority and shared boss health, with boss AI and arenas still local.
- The locally installed client identifies itself as 2.8.1. This contradicts the
  assumption that the workspace package is an upgrade. Deployment is held pending
  comparison; a lower version must not be installed automatically.
- The Steam install identifies Unity 2017.4.40, Windows x64. Game/store identity
  is recorded in `local/identity.json`; the game's own version string is unknown.
- No .NET SDK is currently available; a previous build log references a temporary
  SDK which no longer exists. The .NET 8 runtime is available.
- Existing `CombatSyncSmoke.dll` ran: PASS, 34 production combat checks using
  headless game doubles. This does not validate Unity scenes or a full campaign.
- Existing `EnemyProtocolSmoke.dll` initially hit sandbox socket error 10013.
  Retried with localhost access: PASS, 173 checks, protocol 13, real server relay.
  Neither existing test binary was rebuilt this session. Their results apply to
  the workspace's old protocol, not the currently installed release.
- Installed 2.8.1 uses protocol 16. Its DLL hash matches the previously downloaded
  official 2.8.1 archive, whose hash matches GitHub's published asset digest.
- The installed game's existing log contains repeated inaccessible
  `BlobShadow.SetHidden` errors. The
  [2.8.2 release](https://github.com/BOYSABIO/Blasphemous.Multiplayer/releases/tag/v2.8.2)
  describes this exact failure. Inspection of the published 2.8.2 client confirms
  that its shadow handling uses public APIs instead.
- Downloaded the 2.8.2 client (113,507 bytes) and server (7,657,966 bytes) into
  ignored `local/releases/2.8.2`; both SHA-256 values match GitHub's asset digests.
  No package was installed into the game.
- The published 2.8.2 server crashes with `Property set method not found` when
  invoked with `--port`, `--max-players` and `--password`. This is an observed
  startup failure. Trimming/reflection is a suspected cause, not yet proven.
- Starting the same server without arguments succeeds on TCP port 27051.
- `tools/Test-ReleaseProtocol.ps1` ran against that exact published server:
  PASS, 19 checks. It uses the release's production codecs and scripted TCP peers
  to check joining, old-protocol and duplicate-name refusal, player positions,
  fragmented combat frames, room filtering, authority, damage routing, death
  relay and death replay during host migration. This is not a Unity gameplay test.
- Mutation control: a local copy of that test omitting the guest's damage packet
  fails waiting for packet 15 on the host, as expected. The production test was
  not changed by the mutation.
- The temporary server was stopped with Escape after the tests. Original game
  executable and installed client hashes remain unchanged.
- Prepared `publish/Blasphemous-Coop-2.8.2-candidate.zip`, containing only the
  published mod/server, setup guide, launcher, license, hashes and protocol test.
  It is a candidate, not a gameplay-verified release.
- Did not pull into the dirty source checkout. Public branch metadata confirms
  that both remote main/codex-time still point to the existing local HEAD.
- Published 2.8.2 explicitly excludes enemy types under
  `Gameplay.GameControllers.Bosses` and types whose names contain `Boss`.
  Do not carry the workspace's experimental boss-health claims over to this release.
- Public release tags v2.8.1 and v2.8.2 both resolve to commit
  `b85cc879c801667b159dad28c5e7acfb0b1f293f`, whose source version is 2.4.1.
  The published binaries therefore cannot be reproduced just by checking out
  those tags. Their matching source revisions are unknown.

## Searched

- Co-op Anything directory YAML: no exact `Blasphemous` match in the fetched page.
- Co-op Anything current PR listing: no exact `Blasphemous` match. After retrying
  with network access, unauthenticated GitHub issue search for
  `repo:cranebeaker/coop-anything Blasphemous` returned zero issues/PRs.
- Web queries for Blasphemous co-op/multiplayer, Nexus and Spanish `multijugador`.
- Upstream README, releases, issues; the author's Reddit announcement also surfaced.
- Direct shell requests to public GitHub failed DNS resolution in the sandbox;
  public unauthenticated requests succeeded after retrying with network access.
  No Discord account or private community was accessed.
- Queried upstream forks and the BOYSABIO fork's branches and release assets.
  The recently updated `5192570` fork still identifies as 2.2.2, repeats the
  upstream enemy/boss exclusion, and returned no published releases.

## Derived

- Extend and verify existing work rather than start networking from zero.
- Current source documentation is insufficient to promise synchronized boss
  encounters. Shared health alone cannot establish shared attacks or phases.
- Current version labels are insufficient to select a package: compare the
  installed implementation and supported game identity before any deployment.

## Guesses

- None used to authorize deployment.

## Unknown / open

- Matching source revisions for the published 2.8.1/2.8.2 binaries.
- Whether packaged 2.4.2 binaries exactly match all current sources.
- Live two-client Unity results, save isolation, boss phases and late joining.
- Desired online versus local play and party size.
- Steam relay transport is not established; current source uses TCP direct IP.
- Permission before installing a mod into isolated test copies or using the
  owner's Steam session, as required by upstream AGENTS.md ground rule 1.

## Proposed playbook clarification

Triage should record both game identity and the identity of every already
installed co-op component (version, hash, source revision/protocol when known).
If these differ from the workspace, compare them before offering installation.
Also verify that release tags actually contain source matching their binaries.
Prepare this as a draft contribution only after owner review and account-use
authorization required by upstream AGENTS.md.

## Authorized development and live QA

The owner approved the setup/software/Steam use needed for isolated testing and
development. Their friends will play on separate PCs on the same LAN. No public
posting, account changes, or messages to other people have been requested.

- Installed the pinned official .NET SDK 8.0.426 inside ignored `local/tools`,
  checked against Microsoft's published SHA-512 and recorded SHA-256 locally.
- Recovered the MIT protocol-16 mod/server source from the verified release.
  `revived/Blasphemous.Multiplayer.Revived.sln` is a separate maintainable build;
  the root solution and pre-existing work are preserved. See `revived/README.md`.
- Rebuilt server CLI fixes passed the 19 protocol checks and six additional
  password/player-limit checks. The missing trimmed property setters were
  confirmed in the published DLL.
- `tools/CoopQa.Isolation` prepares two physical copies, with product names
  `BlasCoopQA0/1`, copy-local saves, local achievements, cloud-write stubs, and
  disabled restart-via-Steam. No game assets are included in the tool's source.
- `tools/CoopQa` is an opt-in in-game bridge/bot implementing the hub's JSON-lines
  bridge protocol. It exits the owned test process on an isolation-guard failure.
  The hub's gamepool/gameslot tools own and lease the processes.
- Runtime guards confirmed separate native Unity product identities and private
  save roots. Original-state verification passed after the gameplay tests:
  original executable/client, four isolation-input game files, 17 save files,
  and original registry preferences unchanged.
- Two real Unity clients passed normal combat (14 assertions), shared enemy
  death (17), and host leave/return with stored death persistence (19). These
  are focused mechanics checks, not a campaign playthrough or a remote-PC test.
- Two QA-action defects were found and corrected: Landing waits for a title
  button before activating its preloaded menu; menu Awake methods disable
  background execution. The bot now performs the native transition and has a
  test-only background guard. It also waits for actual level-load completion.
- Direct transform teleporting was unsuitable for arena entry: SmartCollider's
  cached-position sweep hit intervening geometry. The same symptom occurred in
  disconnected solo control. The bot now uses Penitent.Teleport and native
  collider/controller cache resets; this was not a co-op defect.
- Corrected the earlier boss-room guesses using the official Randomizer's
  [structured item locations](https://github.com/BrandenEK/Blasphemous.Randomizer/blob/main/resources/data/Randomizer/locations_items.json):
  Warden `BS13` is `D17Z01S11`; Ten Piedad `BS01` is `D01Z04S18`.
  Raw scene-name presence did not establish a boss-to-room mapping.
- The Warden-only adapter is still experimental. Runtime inspection confirms
  matching boss position/animation and one AI owner, but the current test fails:
  the host's native fight starts and attacks, while the guest's native fight
  remains unstarted and its intro dummy stays visible. Explicit encounter and
  hazard replication are being implemented before this can be called working.

Outstanding: Warden encounter/hazards/death/late-join proof, further boss adapters,
LAN setup/package verification, and honest installation/QA documentation. The
existing candidate ZIP is not a completed co-op release.

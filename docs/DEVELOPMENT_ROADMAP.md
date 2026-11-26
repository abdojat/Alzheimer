# Alzheimer — implementation roadmap and verified status

**Status updated:** 2026-11-25 on `feat/p3a-multiplayer-server`. P0 device smoke-test status is owner-reported; P1/P2A/P2B/P2C validation reflects commands run for those milestones, and P3A results are recorded below.

## Already present (by code inspection)

- [x] Pure C# primitives: cards, 1–4 shuffled decks, player-count settings, rank/Queen-window matching, scoring rules.
- [x] No-NuGet executable smoke suite covering core rules, P1–P2C regressions, round reset, privacy, and deterministic match simulation (1,869 assertions in the P2C validation run).
- [x] ASP.NET Core 10 SignalR room create/join/leave and in-memory lobby state.
- [x] Unity 6 Android project with MainMenu/GameTable scene files, runtime-generated uGUI preview, Android build scripts, URP 2D assets.
- [x] P1 deterministic pure-C# round foundation for initial peeks, ordinary draw resolution, post-resolution decision and privacy-safe projections.
- [x] P2A Jack, Queen, and King abilities with injected-clock 30-second ability deadlines, private-safe projections, Queen reaction metadata, and deterministic timeout handling.
- [x] P2B rank-only multiplayer reactions with persistent Queen matching, per-player Done, authoritative 30-second deadlines, invalid-throw penalties, privacy-safe projections, and automatic reaction calls.
- [x] P2C executable final turns, exhaustion-aware round scoring, cumulative match completion, fresh round resets, structured score/reveal projections, and deterministic full-match simulation.
- [x] P3A authoritative SignalR gameplay with stable room-player mapping, per-room engine serialization, private/public projections, deadline processing, disconnect cleanup, and a real two-client full-match integration scenario.
- [x] Lightweight .NET 10 GitHub Actions workflow for core/server builds plus smoke/game-engine and SignalR integration tests; Unity/Android builds are excluded.
- [ ] Connected Unity online lobby/client (not implemented).
- [x] Owner-confirmed Android build installed and launched on a physical phone (reported 2026-11-25; not independently reproduced in this work session).

## P0 — stabilize the Unity prototype

1. [x] Inspected Unity project version (`6000.6.10f1`), input settings, package manifest, scene bootstrap and scene generator.
2. [x] Declared `com.unity.inputsystem` 1.20.1, referenced its assembly, retained `activeInputHandler: 1`, and configured `InputSystemUIInputModule` for mouse and touch through the default UI actions.
3. [x] Replaced runtime `DestroyImmediate()` calls with lifecycle-safe `Destroy()` or object reuse/deactivation. The bootstrap reconciles one EventSystem, one runtime UI canvas, and one set of persistent managers.
4. [x] Added static-state and `sceneLoaded` reset handling for Enter/Exit Play Mode and disabled domain reload. Added repeated-transition Play Mode tests.
5. [ ] Repeated scene-transition runtime execution, Editor compilation, 9:16/touch validation, and automated Unity tests remain unverified. Earlier Unity test attempts were blocked by entitlement and package/cache issues; do not treat those attempts as passing.
6. [x] The owner reports that an Android APK was built, installed, and launched on a physical phone. This agent has not independently repeated the build or device test.

**P0 acceptance:** Android build/install/launch has owner-reported physical-device confirmation. Unity automated tests, repeated navigation, and touch-input checks remain unverified; this distinction must be preserved in status reports. P1 core work can proceed. No fake online buttons or gameplay completion are implied.

## P1 — deterministic server-authoritative round engine

1. [x] Add explicit internal match/round/player/turn state, phases, ordered seats, unique physical IDs, per-slot private knowledge, caller and cumulative-score ledger to pure `core`.
2. [x] Implement deterministic round setup, four-card deal, exactly two owner-only peeks, mandatory draw, discard/replace, post-resolution call/end decision, and invalid-action rejection.
3. [x] Provide public and per-player projections; keep hidden identities out of public DTOs and transition events.
4. [x] Inject seeded randomness, physical ID generation, and a UTC clock; add deterministic multi-turn, privacy, rejection, card conservation, configuration, call timing and last-draw tests to the no-dependency smoke runner.
5. [x] Validate with the P1 commands listed below; retain the existing .NET smoke checks.

**P1 acceptance:** met for the implemented slice. Rounds can be deterministically initialized for 2–10 players and 1–4 decks, and ordinary turns can be drawn/resolved without Unity while private projections keep hidden identities private. P1 does not constitute a complete or playable match.

### P1 API and operational boundary

- Each authoritative room will create one `AlzheimerRoundEngine` and submit trusted player IDs with `ProcessAction`. Client-supplied ranks, scores, identities, or timestamps are not accepted as commands.
- Project `GetPublicView()` to all room members and `GetPlayerView(playerId)` only to that player. Never serialize or broadcast internal state.
- The engine runs synchronous transitions but does not synchronize concurrent access. The server must serialize each room's commands through a per-room lock, actor, or queue before accepting multiplayer actions.
- `CompleteReactionWindow()` remains a trusted progression hook and refuses to close an early window; it closes only after the authoritative deadline. All players marking Done closes the window immediately.
- `IGameClock` supplies transition timestamps plus P2A ability and P2B reaction deadlines. `ProcessTimeouts()` expires due ability/reaction windows, and each action checks for elapsed deadlines before validation.
- `ThrowReactionCard` and `MarkReactionDone` carry the active reaction-window ID and a unique command ID. The engine rejects stale/replayed commands, validates actual server-owned card ranks, and is intentionally synchronous; a future server must serialize each room's commands.
- The P3A server now uses this contract; Unity remains disconnected until P3B.

## P2A — special-card abilities and .NET CI

1. [x] Activate J/Q/K only for a special card drawn during the active player's normal turn; preserve keep/discard alternatives.
2. [x] Resolve Jack peeks privately, Queen discards with retained two-rank reaction metadata and final-top restoration, and blind King swaps with received-card knowledge invalidation.
3. [x] Enforce exact 30-second ability deadlines using the injected UTC game clock, including deterministic expiry before accepting a late resolution.
4. [x] Add deterministic ability/privacy/conservation tests and a .NET-only GitHub Actions workflow.

**P2A acceptance:** implemented in the pure C# engine. At that milestone the API was disconnected from SignalR; P3A now consumes its actions. The no-target choice policy is tested directly; P2C executes final turns through the ordinary draw/resolution/ability flow.

## P2B — reaction gameplay (complete)

1. [x] Implement repeated multiplayer reaction throws, rank-only matching against the current top, and persistent Queen dual-rank matching; restore the Queen-selected card as final top.
2. [x] Add irreversible per-player Done, unique command IDs, stale-window rejection, and injected-clock 30-second deadlines with exact boundary handling.
3. [x] Apply private knowledge reveals and immediate unknown-card penalties for invalid throws; record +10 when the draw pile is empty and keep penalty depletion inside the ongoing window.
4. [x] Automatically register a first empty-hand reaction caller, stop reactions immediately, preserve active-seat context, and record subsequent-turn player order for P2C execution.
5. [x] Cover matching/nonmatching/suit-only attempts, multiple throws, unknown-card guesses, Queen exception/final top, Done/deadlines, penalties/depletion, privacy, command replays, automatic calls, and card conservation with deterministic smoke scenarios.

**P2B acceptance:** complete in the pure C# engine. Actions are synchronous and are now serialized per room by P3A; no persistence or Unity work is included. P2C consumes `RequiredFinalTurnPlayerIds`; `RoundPenaltyPoints` feed round scoring.

## P2C — final turns, round scoring, and match progression (complete)

1. [x] Execute voluntary and automatic-call final turns from a pending queue; skip the caller, preserve active-turn accounting after reaction calls, and reuse normal draw/resolution/ability commands without reactions or further calls.
2. [x] Resolve last-card draws and penalty depletion at their specified boundaries; retain reaction windows after penalty depletion and apply +10 penalties when no card remains.
3. [x] Apply caller/no-caller scoring and doubling after adding hand values and penalties; accumulate scores and complete the match at cumulative >=100 with all tied-lowest winners.
4. [x] Reset round-only cards, penalties, caller, and knowledge; preserve player identity/seating/host/cumulative scores; reshuffle with deterministic randomness, deal four cards, require fresh peeks, and use globally advancing reaction-window IDs.
5. [x] Expose structured round scores/reason/cumulative scores/winners/final-turn queues and reveal hands only after round scoring.
6. [x] Add deterministic 2–10 player voluntary-call scenarios, four-player automatic-call ordering, final abilities/timeouts, depletion/penalty cases, score-rule variants, reset checks, idempotency checks, and a complete multi-round match simulation.

**P2C acceptance:** complete for the pure C# engine. No new rule ambiguity was found; edge timing follows rulebook v1.1. P2C did not itself implement SignalR/Unity integration; P3A now provides server gameplay, while Unity remains out of scope.

## P3A — authoritative multiplayer server (implemented)

1. [x] Keep one authoritative `AlzheimerRoundEngine` per active room; preserve lobby methods/settings/seating, host-only match start, and reject active-match joins/duplicate starts.
2. [x] Define fixed `GameplayCommandV1` payloads and versioned responses/events; derive the actor from connection membership, allowlist command-to-core mappings, reject duplicate IDs and stale reaction windows, and expose monotonic state sequences.
3. [x] Deliver public engine projections to the room and private projections only to mapped player connections; no internal game state or opponents' pending/hidden cards are sent.
4. [x] Serialize player actions, timeouts, start, join, and leave operations per room. A single hosted deadline worker checks core UTC deadlines every 100 ms through the same room gate; it holds no per-room timer callbacks.
5. [x] Define disconnect MVP: any active-match disconnect abandons and removes the entire room; remaining players receive `RoomClosed`. No reconnect, room-code rejoin, or identity recovery is supported. Empty lobby rooms are removed immediately.
6. [x] Add an in-process Kestrel SignalR test with independent clients. It validates lobby/start authorization, private peek serialization, Jack and King privacy, duplicate/stale/unauthorized commands, concurrent throws, server-driven reaction and ability expiry, room isolation/disconnection, a complete scored round, and match completion.
7. [x] Add the integration runner to `.github/workflows/dotnet.yml`; GitHub Actions status remains unverified until the workflow runs remotely.

**P3A acceptance:** the local server integration runner passed with two gameplay clients completing actual core-scored rounds through match completion and observing authorized public/private state. This does not claim Unity transport, production authentication, reconnect support, or a successful remote CI run.

## P3B — Unity multiplayer client (not started)

1. Select an Android/IL2CPP-compatible SignalR client transport (or choose and version a native WebSocket protocol); verify package/AOT compatibility before adding dependencies.
2. Add Unity lobby Create/Join/Leave UI and map the server's stable room player ID to the local connection session. Do not trust client-supplied actor IDs.
3. Start matches via `StartMatchV1`, submit `GameplayCommandV1`, and apply only public events plus the current player's `GamePrivateStateUpdatedV1`; discard stale update sequences.
4. Add reconnect UX only after an explicit identity/authentication policy exists; current P3A disconnect behavior abandons the match.
5. Verify Android Editor/IL2CPP builds, two-device transport, privacy, touch interaction, and a full match before claiming P3B complete.

## P4 — release readiness

- Authentication / guest identity; reconnect and timeout policy; persistence; abusive-client protections; sound, animation, accessibility, localization; Android release signing and store requirements; Unity build validation.
- Do not treat this as implemented until verified.

## Known technical findings

- `SceneBootstrap.cs` now subscribes to scene loads and rebuilds only the active prototype screen, while deduplicating runtime UI and using `InputSystemUIInputModule`; Unity Editor validation remains required.
- `manifest.json` declares `com.unity.inputsystem`; Package Manager import and package-lock restoration remain to be confirmed by Unity.
- `NetworkManager.Connect` explicitly reports no real network transport; `LobbyHub` now hosts authoritative SignalR game commands, but Unity does not connect to it yet.
- `MainMenu` Create/Join/Settings are placeholder status actions.
- `GameTableScreen` draws four placeholders and starts/stops its timer immediately; it has no actual gameplay mechanics.
- `.github/workflows/dotnet.yml` now restores/builds the .NET projects and runs both the core smoke suite and SignalR integration executable on pushes and pull requests targeting `main`; it intentionally has no Unity or Android build.

## Update policy

When completing a milestone, link its commit/PR, add the validation command/results and keep unchecked items unchecked until verified. Rules remain authoritative in `docs/Alzheimer_Card_Game_Rules_v1.1.md` unless the owner explicitly approves a new version.

### P2A validation run (2026-11-25)

- `dotnet run --project tests/Alzheimer.SmokeTests` — passed; 842 assertions, no warnings.
- `dotnet build core/Alzheimer.Core` — passed; 0 warnings, 0 errors.
- `dotnet build server/Alzheimer.Server` — passed; 0 warnings, 0 errors.
- CI workflow was added but cannot be reported as a successful GitHub Actions run until pushed and executed by GitHub.
- Unity Editor tests and Android build were not run for this core-only change. The Android install/launch result above is owner-reported, not independently re-tested here.

### P2B validation run (2026-11-25)

- `dotnet run --project tests/Alzheimer.SmokeTests` — passed; 897 assertions, including the P1 and P2A regression scenarios.
- `dotnet build core/Alzheimer.Core` — passed; 0 warnings, 0 errors.
- `dotnet build server/Alzheimer.Server` — passed; 0 warnings, 0 errors.
- Unity, SignalR gameplay, persistence, and multiplayer transport tests were not run; they are outside P2B and are not implemented.

### P2C validation run (2026-11-25)

- `dotnet run --project tests/Alzheimer.SmokeTests` — passed; 1,869 assertions, including existing P1/P2A/P2B regressions and P2C scenarios.
- `dotnet build core/Alzheimer.Core -c Release` — passed; 0 warnings, 0 errors.
- `dotnet build server/Alzheimer.Server -c Release` — passed; 0 warnings, 0 errors.
- The deterministic full-match test completed through two exhausted rounds to match end. Seed 1901: round 1 P1 43→86 (cumulative 86), P2 24→48; round 2 P1 26→52 (cumulative 138), P2 54→108 (cumulative 156); P1 won. Round resets preserved host/player identity while using new card IDs and resetting private knowledge.
- Unity Editor/tests, Android APK build, SignalR gameplay, persistence, and two-client multiplayer were not run; they are outside P2C. The Android device confirmation remains owner-reported only.
- No unresolved rule ambiguity was found in the implemented P2C paths; the canonical v1.1 rulebook was unchanged.

### P3A validation run (2026-11-25)

- Confirmed `feat/p3a-multiplayer-server` is checked out, clean at task start, and based on `main`; P2C commit `bca1993` is an ancestor of `main`.
- `dotnet run --project tests/Alzheimer.SmokeTests -c Release` — passed; 1,869 assertions.
- `dotnet build core/Alzheimer.Core -c Release --no-restore` and `dotnet build server/Alzheimer.Server -c Release --no-restore` — passed; both with 0 warnings and 0 errors.
- `dotnet run --project tests/Alzheimer.Server.IntegrationTests -c Release --no-restore` — passed; 214 assertions over five independent SignalR client connections, including full two-player match completion.
- The integration scenario uses a controllable core clock and seeded deck; ability and reaction deadlines were advanced without waiting 30 real seconds. It inspected the serialized public/private SignalR event objects for hidden card IDs.
- Final `git diff --check` passed. GitHub Actions has not run. Unity/Android builds, Unity transport, production authentication, and reconnect were not run.

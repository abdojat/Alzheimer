# Alzheimer — implementation roadmap and verified status

**Status updated:** 2026-11-25 on `feat/p2a-special-abilities`. P0 device smoke-test status is owner-reported; P1/P2A checks below reflect commands run for this change.

## Already present (by code inspection)

- [x] Pure C# primitives: cards, 1–4 shuffled decks, player-count settings, rank/Queen-window matching, scoring rules.
- [x] Executable console smoke test program with 18 original utility checks (re-run with P1 scenarios on 2026-11-25).
- [x] ASP.NET Core 10 SignalR room create/join/leave and in-memory lobby state.
- [x] Unity 6 Android project with MainMenu/GameTable scene files, runtime-generated uGUI preview, Android build scripts, URP 2D assets.
- [x] P1 deterministic pure-C# round engine for initial peeks, ordinary draw resolution, post-resolution decision and privacy-safe projections (see P2 for remaining gameplay).
- [x] P2A Jack, Queen, and King abilities with injected-clock 30-second ability deadlines, private-safe projections, Queen reaction metadata, and deterministic timeout handling.
- [x] Lightweight .NET 10 GitHub Actions workflow for core/server builds and smoke/game-engine tests; Unity/Android builds are excluded.
- [ ] Connected Unity online lobby/client (not implemented).
- [ ] Authoritative multiplayer gameplay (the server remains lobby-only).
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
- `CompleteReactionWindow()` remains a trusted simulation/progression hook; P2A does not implement reactions or a reaction timer. P2B must use server-owned reaction deadlines and processing in a production room; clients cannot decide that a phase has expired.
- `IGameClock` supplies transition timestamps and P2A ability deadlines. `ProcessTimeouts()` expires due abilities; P2B must add authoritative reaction deadlines.
- P3 will connect room creation/lifecycle to match lifecycle. P1 does not change `LobbyHub` or create a live network game.

## P2A — special-card abilities and .NET CI

1. [x] Activate J/Q/K only for a special card drawn during the active player's normal turn; preserve keep/discard alternatives.
2. [x] Resolve Jack peeks privately, Queen discards with retained two-rank reaction metadata and final-top restoration, and blind King swaps with received-card knowledge invalidation.
3. [x] Enforce exact 30-second ability deadlines using the injected UTC game clock, including deterministic expiry before accepting a late resolution.
4. [x] Add deterministic ability/privacy/conservation tests and a .NET-only GitHub Actions workflow.

**P2A acceptance:** implemented in the pure C# engine. The API remains disconnected from live SignalR gameplay. The no-target choice policy is tested directly; a no-target state cannot currently be reached through P1/P2A normal-turn transitions because empty-hand calls end the current engine pipeline and reaction throws/final turns are not implemented.

## P2B — reaction gameplay (not complete)

1. [ ] Implement simultaneous rank-only reaction throws and persistent Queen dual-rank matching; integrate the existing Queen final-top restoration with reaction throws.
2. [ ] Add Done states and a server-authoritative 30-second reaction deadline.
3. [ ] Implement invalid-throw penalties, empty-pile +10 penalties, reaction depletion, and automatic calls from reaction throws.
4. [ ] Test simultaneous submissions, action ordering, privacy, and all reaction edge cases.

## P2C — final-turn accounting and scoring (not complete)

1. [ ] Implement voluntary/automatic caller final-turn accounting, including empty-hand calls outside the active player's turn.
2. [ ] Implement draw-pile depletion timing, end-of-round scoring/doubling, cumulative scores, match completion at 100, and round reset.
3. [ ] Add deterministic scoring, tie, depletion, and multi-round match tests.

**P2B/P2C acceptance:** neither phase is complete. Full rulebook simulation, scoring, and match completion remain outstanding.

## P3 — connected lobby + game UI

1. Define a versioned message contract; connect Unity client to the existing room lobby with an Android IL2CPP-compatible network stack.
2. Show Create/Join/Leave room with real responses; host settings and 2–10 player list.
3. Start real matches from the lobby; display only per-player authorized card information, turn phase/timers, reaction Done and scores.
4. Exercise two Android/Editor clients simultaneously; test reconnect, cleanup and stale action protection.

**P3 acceptance:** at least two clients complete a real round using the server and observe consistent, private information.

## P4 — release readiness

- Authentication / guest identity; reconnect and timeout policy; persistence; abusive-client protections; sound, animation, accessibility, localization; Android release signing and store requirements; Unity build validation.
- Do not treat this as implemented until verified.

## Known technical findings

- `SceneBootstrap.cs` now subscribes to scene loads and rebuilds only the active prototype screen, while deduplicating runtime UI and using `InputSystemUIInputModule`; Unity Editor validation remains required.
- `manifest.json` declares `com.unity.inputsystem`; Package Manager import and package-lock restoration remain to be confirmed by Unity.
- `NetworkManager.Connect` explicitly reports no real network transport; `LobbyHub` is lobby-only.
- `MainMenu` Create/Join/Settings are placeholder status actions.
- `GameTableScreen` draws four placeholders and starts/stops its timer immediately; it has no actual gameplay mechanics.
- `.github/workflows/dotnet.yml` runs .NET restore, core/server builds, and the smoke/game-engine test executable on pushes and pull requests targeting `main`; it intentionally has no Unity or Android build.

## Update policy

When completing a milestone, link its commit/PR, add the validation command/results and keep unchecked items unchecked until verified. Rules remain authoritative in `docs/Alzheimer_Card_Game_Rules_v1.1.md` unless the owner explicitly approves a new version.

### P2A validation run (2026-11-25)

- `dotnet run --project tests/Alzheimer.SmokeTests` — passed; 842 assertions, no warnings.
- `dotnet build core/Alzheimer.Core` — passed; 0 warnings, 0 errors.
- `dotnet build server/Alzheimer.Server` — passed; 0 warnings, 0 errors.
- CI workflow was added but cannot be reported as a successful GitHub Actions run until pushed and executed by GitHub.
- Unity Editor tests and Android build were not run for this core-only change. The Android install/launch result above is owner-reported, not independently re-tested here.

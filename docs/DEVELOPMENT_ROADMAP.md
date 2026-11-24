# Alzheimer — implementation roadmap and verified status

**Audit baseline:** repository `abdojat/Alzheimer`, `main` at commit `63a1b11e7b5397bdde678c566c2a28970badb2b0` (audited 2026-11-24). P0 implementation status is updated below after direct workspace validation.

## Already present (by code inspection)

- [x] Pure C# primitives: cards, 1–4 shuffled decks, player-count settings, rank/Queen-window matching, scoring rules.
- [x] Executable console smoke test program with 17 rule checks (re-run and passed 2026-11-24).
- [x] ASP.NET Core 10 SignalR room create/join/leave and in-memory lobby state.
- [x] Unity 6 Android project with MainMenu/GameTable scene files, runtime-generated uGUI preview, Android build scripts, URP 2D assets.
- [ ] Complete playable local game engine (not implemented).
- [ ] Connected Unity online lobby/client (not implemented).
- [ ] Authoritative multiplayer gameplay (not implemented).
- [ ] Successful APK built and installed on a physical device (not verified in repository inspection).

## P0 — stabilize the Unity prototype

1. [x] Inspected Unity project version (`6000.6.10f1`), input settings, package manifest, scene bootstrap and scene generator.
2. [x] Declared `com.unity.inputsystem` 1.14.2, referenced its assembly, retained `activeInputHandler: 1`, and configure `InputSystemUIInputModule` for mouse and touch through the default UI actions.
3. [x] Replaced runtime `DestroyImmediate()` calls with lifecycle-safe `Destroy()` or object reuse/deactivation. The bootstrap reconciles one EventSystem, one runtime UI canvas, and one set of persistent managers.
4. [x] Added static-state and `sceneLoaded` reset handling for Enter/Exit Play Mode and disabled domain reload. Added repeated-transition Play Mode tests.
5. [ ] Repeated scene-transition runtime execution, Editor compilation, 9:16/touch validation, and automated Unity tests remain blocked. Unity resolves a Personal entitlement. Script compilation exposed `com.unity.inputsystem@1.14.2` as incompatible with Unity `6000.6.10f1` APIs (CS0619 obsolete-API errors). The manifest has been updated to the 6000.6-compatible `1.20.1`; a bounded re-run restored the stale lock/cache state and did not execute tests, so package-lock restoration and test execution remain manual/Editor validation work.
6. [ ] Development Android APK generation remains blocked by the licensing problem.

**P0 acceptance:** P0 is **blocked**, not completed. The Unity source/configuration and lifecycle test implementation are prepared, but Unity compilation, repeated runtime navigation, mouse/touch input, and APK generation have no passing runtime evidence. Existing .NET smoke tests and server build must be verified in the final validation pass. No fake online buttons or gameplay completion are included.

## P1 — deterministic server-authoritative round engine

1. Add explicit round state, phase enum, ordered players, deck/discard and private knowledge, caller and score ledgers to pure `core`.
2. Introduce server-validated commands for dealing, initial peek, mandatory draw, keep/replace/discard, and legal/illegal phase transitions.
3. Unit tests for draw/resolve/call timing, knowledge and hand-size changes; fake clock and seeded RNG.

**P1 acceptance:** deterministic 2–10-player simulation can deal, draw and resolve a turn without Unity and without leaking hidden card data; comprehensive tests pass.

## P2 — all abilities and reaction/final phase

1. Jack/Queen/King ability resolution and 30s timeouts.
2. 30s simultaneous reaction windows, irreversible Done, rank-only matches, special Queen dual-target behavior.
3. Invalid reaction penalties, empty-pile +10, end-on-depletion and last-card reaction window.
4. Manual and automatic Alzheimer calls, full final-turn accounting, scores, deck reset, match ends at >=100.
5. Add integration/simulation tests covering race conditions, ties, edge cases and view redaction.

**P2 acceptance:** full rulebook simulation runs headlessly on the server with deterministic tests and consistent private state views.

## P3 — connected lobby + game UI

1. Define a versioned message contract; connect Unity client to the existing room lobby with an Android IL2CPP-compatible network stack.
2. Show Create/Join/Leave room with real responses; host settings and 2–10 player list.
3. Start real matches from the lobby; display only per-player authorized card information, turn phase/timers, reaction Done and scores.
4. Exercise two Android/Editor clients simultaneously; test reconnect, cleanup and stale action protection.

**P3 acceptance:** at least two clients complete a real round using the server and observe consistent, private information.

## P4 — release readiness

- Authentication / guest identity; reconnect and timeout policy; persistence; abusive-client protections; sound, animation, accessibility, localization; Android release signing and store requirements; CI for .NET tests and Unity build validation.
- Do not treat this as implemented until verified.

## Known technical findings

- `SceneBootstrap.cs` now subscribes to scene loads and rebuilds only the active prototype screen, while deduplicating runtime UI and using `InputSystemUIInputModule`; Unity Editor validation remains required.
- `manifest.json` declares `com.unity.inputsystem`; Package Manager import and package-lock restoration remain to be confirmed by Unity.
- `NetworkManager.Connect` explicitly reports no real network transport; `LobbyHub` is lobby-only.
- `MainMenu` Create/Join/Settings are placeholder status actions.
- `GameTableScreen` draws four placeholders and starts/stops its timer immediately; it has no actual gameplay mechanics.
- No dedicated agent instructions or GitHub CI were present at audit baseline.

## Update policy

When completing a milestone, link its commit/PR, add the validation command/results and keep unchecked items unchecked until verified. Rules remain authoritative in `docs/Alzheimer_Card_Game_Rules_v1.1.md` unless the owner explicitly approves a new version.

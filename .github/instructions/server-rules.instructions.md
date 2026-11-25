---
applyTo: "core/**,server/**,tests/**"
---
# Server and rules engine instructions

Authoritative source: `docs/Alzheimer_Card_Game_Rules_v1.1.md`. `DeckFactory`, `PlayingCard`, `ReactionRules`, `RoundScorer`, and `RoomSettings` are rules utilities. `AlzheimerRoundEngine` provides deterministic round setup, P2A abilities, P2B reactions, P2C executable final turns, exhaustion-aware scoring, and automatic match progression/reset. `LobbyHub` and `LobbyStore` implement rooms only.

## Design the next vertical slice as pure, testable C#

- In `core`, define immutable/controlled state for deck/discard, per-player ordered hand and knowledge, turn order, phases, active player, one caller, per-round penalties, cumulative scores and deadlines.
- Explicit transitions: `Deal -> InitialPeek -> MandatoryDraw -> ResolveDraw (Keep/Discard/Ability) -> PostResolutionCallOrEnd -> ReactionWindow -> NextTurn`; a caller enters `FinalPhase` for the next eligible mandatory draw, then reuses normal resolution/ability phases with calls/reactions disabled, before `Score -> NewRound/MatchEnd`.
- Ability windows (Jack, Queen, King) and reaction windows are distinct 30-second deadlines. `ProcessTimeouts()` uses the injected UTC clock to expire due abilities and reactions; never accept a client-supplied deadline. `ThrowReactionCard` and `MarkReactionDone` carry the active reaction-window ID and a unique command ID.
- Host always leads; clockwise turns. No player-count-4 assumption; hands vary in size. Deck count 1–4; stable unique physical-card identifiers.
- Preserve rules for knowledge: newly kept card is face down; owner saw it; a King swap is blind and received card is unknown. Invalid throw makes attempted card known and adds one unknown penalty card, or +10 if pile exhausted.
- P2B validates reactions using server-owned ranks, retains private knowledge, keeps Queen's two ranks valid throughout its window, and records the required subsequent-turn order when a reaction empties a hand.
- P2C consumes `RequiredFinalTurnPlayerIds` once through `PendingFinalTurnPlayerIds`; the caller is skipped, reaction-time active turns do not count as subsequent turns, and final-turn abilities still use the injected clock.
- Round scoring includes hand-card points and +10 undrawn penalties before doubling. `LastCompletedRound` exposes structured score/reason/cumulative data and the authorized round-end hand reveal; current hidden hands remain private.
- A nonterminal round score creates a fresh shuffled deck, new four-card hands, reset private knowledge and penalties, and host-first seating while preserving cumulative scores. Reaction-window IDs remain unique across rounds.
- Ordinary reactions match current **top rank only**. Queen two-rank exception lasts for whole window; the Queen ability's discarded card is the top after window ends. Done players cannot resume. A successful reaction can empty a hand and auto-call.
- Voluntary Alzheimer only at post-resolution prompt before reaction; one caller only. Empty-hand automatic calls may occur outside a player's turn and require full remaining-turn bookkeeping. Last-card and penalty depletion need precise end timing.
- Round-score doubling and cumulative score >=100 rules must be tested, including penalties before doubling and ties.
- Use injectable randomness and a controllable/virtual clock for deterministic tests. Model actions as validated commands producing state transitions/events; avoid implicit wall-clock sleeps in core tests.

## Server integration and security

- Keep connection IDs separate from stable player/seat identities. Current lobby is in memory and not reconnect-safe; mark this limitation until fixed.
- Serialize actions per room to avoid races across multiple simultaneous reaction throws. No gameplay rule favors a particular player, but server must settle each attempted action consistently.
- The engine is synchronous and not thread-safe. `CompleteReactionWindow()` must not bypass the reaction deadline; all players marking Done closes the window immediately.
- Validate phase, identity, ownership, card index/ID, deadline and game constraints server-side before committing an action.
- Never broadcast the full game state, private card ranks or private knowledge. Project public state per room and private views to each player.
- Use structured events/snapshots rather than exposing internal mutable object references. Guard against duplicate submissions and stale-turn actions.
- Scope network work to one end-to-end feature at a time; verify at least two concurrent clients before claiming online gameplay works.

## Verification commands

From repo root:

```powershell
dotnet run --project tests/Alzheimer.SmokeTests
dotnet build server/Alzheimer.Server
```

Add scenario tests for each implemented state transition; do not rely only on the existing 17 utility smoke checks. If test tools or NuGet restore fail, report the limitation exactly.

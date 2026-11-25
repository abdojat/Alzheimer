# Alzheimer — instructions for coding agents

This file applies to the **entire repository**. Read it before changing code. Do not treat this project as generic Cabo/Cambio.

## Source of truth

1. **Canonical gameplay rules:** `docs/Alzheimer_Card_Game_Rules_v1.1.md` (read in full before gameplay changes).
2. Explicit later decisions from the project owner supersede that document; update the rulebook and tests in the same change if a rule changes.
3. Existing implementation and tests describe **what exists**, not necessarily what the rules require.
4. If there is a genuinely unspecified behavior, mark it as a question or TODO; do not silently invent game rules.

## Repository map

- `core/Alzheimer.Core/`: pure .NET 10 utilities, the P1 deterministic round foundation, and P2A Jack/Queen/King abilities with authoritative ability deadlines. Reactions and full round/match completion are not implemented.
- `server/Alzheimer.Server/`: ASP.NET Core 10 / SignalR **lobby only**; in-memory rooms; no live gameplay, private card delivery or turn management yet.
- `tests/Alzheimer.SmokeTests/`: executable basic checks. Existing tests do **not** prove gameplay is finished.
- `client/Alzheimer.Client/`: Unity 6 editor project for an Android portrait client, uGUI prototype and Android build script.
- `unity/StarterCardTable.cs`: older reference demo; do not duplicate it into the client unless justified.
- `docs/`: authoritative rulebook and implementation roadmap.

## Working procedure

1. Inspect the relevant code, exact Unity `ProjectVersion.txt`, package manifest, README and applicable instructions before editing.
2. State the smallest coherent milestone and acceptance criteria; implement **one milestone at a time**. Prefer a working vertical slice over a broad scaffold.
3. Preserve existing code, scenes, Unity `.meta` files and stable GUID references. Use Unity Editor APIs for scene/asset generation, not handwritten serialized scene YAML.
4. Separate deterministic pure game rules from server authority and from Unity presentation. Avoid putting authoritative game decisions in MonoBehaviours.
5. Add or update automated tests for each rule or regression fixed. Run `dotnet run --project tests/Alzheimer.SmokeTests` from repository root; run Unity validation/builds where the Editor is available.
6. Do not claim any test, Android build, physical-device check or multiplayer scenario passed unless it was actually executed successfully. Report exact commands and remaining blockers.
7. Do not add a large dependency, change Unity versions, reset project settings, rewrite the app, push to main, or delete existing assets without a clear reason.
8. Keep README and `docs/DEVELOPMENT_ROADMAP.md` current when an implementation milestone is completed.

## Essential game invariants (summary only)

- 2–10 players; host chooses 1–4 combined 54-card decks (52 + 2 Jokers per deck); host starts every round and play is clockwise.
- Four face-down cards per player at deal; owners initially peek at two. A player's knowledge is private and **separate from the card's physical face-down state**.
- Every normal turn requires drawing and viewing a card. Keep/replace, discard, or (for a drawn J/Q/K) activate a 30-second ability. Newly placed hand cards stay face down.
- Jack peeks own card; Queen discards one own card; King blind-swaps any own card with any opponent card. Received card becomes unknown. Ability cards discarded on resolution; failed/expired ability has no effect.
- **Voluntary Alzheimer call only after fully resolving the drawn card and before the reaction window.** Not before draw, not during ability or reaction windows; one caller only.
- Reaction phase lasts up to 30 seconds; all players can throw any number of cards matching **rank only**, not suit. Done is irreversible within that window. Queen exception: both Queen rank and ability-discarded card rank stay valid for the entire window, and the Queen-discarded card becomes the final pile top.
- Incorrect throw returns the card and reveals it to the thrower, then requires an immediate unknown penalty card; if draw pile empty, add +10 score points. Window continues if depleted by penalty.
- Zero-card hand auto-calls; suppress reaction throwing immediately; when triggered outside that player's turn, each other player must get a subsequent turn. The caller takes no further turns.
- Drawing the last card: resolve it; offer voluntary call if eligible; if no call, reaction window still occurs before depletion ends round. When no caller and draw pile exhausted, double **everyone's** base round score.
- Caller uniquely lowest => double all other players; otherwise (including tie) double caller. Penalties count before doubling. Accumulate scores until anyone reaches >=100; lowest cumulative score wins; tied lowest players share the win.
- Full edge cases and exact timing: read the canonical rulebook.

## Server and privacy constraints

- The **server** owns the deck, actual card identities, authority for timers/actions, turn order, caller identity, scoring and round lifecycle.
- Do not send hidden identities to a client who is not entitled to know them. Prefer per-recipient state projections and explicit, temporary reveal messages.
- Assign unique physical card IDs across multiple decks; store hand positions and each player's knowledge explicitly. King swaps invalidate received-card knowledge.
- Use serialized per-room action processing / locking; define server-authoritative UTC deadlines and atomic invalid-throw penalties. Never trust client-supplied card values, scores or clocks.
- Treat room passwords, player names, reconnect tokens and eventual authentication as untrusted inputs. Keep guest lobby features clearly distinguished from production security.

## Unity P0 verification status

The owner reports that an Android build was successfully installed and launched on a physical phone. This confirmation is distinct from Unity automated test results, which remain unverified; do not claim automated scene-navigation, touch-input, or Unity test validation passed unless it is run. See `docs/DEVELOPMENT_ROADMAP.md` for the remaining P0 checks and evidence.

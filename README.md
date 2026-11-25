# Alzheimer — Development Starter

A starter implementation for the customized Alzheimer multiplayer card game. Includes:

- The confirmed v1.1 rulebook under `docs/`.
- A pure C# rules library with deck generation, card scores, reaction-rank validation, scoring utilities, and a deterministic P1/P2A round engine.
- A no-NuGet-dependency smoke test console app covering utilities, P1 transitions, and P2A special abilities.
- An ASP.NET Core 10 + SignalR **lobby-only** server (create/join rooms and player lists). The server does **not** yet implement game turns, private card delivery, timers, or online card actions.
- A small **local UI smoke test** Unity MonoBehaviour. This is *not* a functional game.

## Requirements

- Unity Hub + Unity **6000.6.10f1** (Unity 6.6), with Android Build Support, Android SDK & NDK Tools, and OpenJDK selected.
- .NET **10 SDK** and a C# editor (VS Code with C# Dev Kit, Visual Studio, or Rider).
- Git.

## Run the .NET tests (macOS/Windows/Linux)

From the repository root:

```bash
dotnet run --project tests/Alzheimer.SmokeTests
```

You should see `All smoke tests passed.` The tests cover deck and player configurations, deterministic setup and IDs, private peeks, mandatory draw, discard/replace, call timing, special-ability resolution and timeouts, rank-only reaction chains, persistent Queen matching, Done/deadlines, wrong-throw penalties, automatic calls, rejection atomicity, privacy-safe projections, turn order, card conservation, and existing card/scoring utilities.

## P1/P2B round engine API

`AlzheimerRoundEngine.StartNewRound(...)` creates a round in `WaitingForInitialPeeks`. Supply the clockwise player list, host ID, `RoomSettings`, an optional seeded `Random`, optional physical-card-ID factory, and optional `IGameClock`. The engine rotates the supplied clockwise order so the host is first, deals four unknown cards to each player, and keeps the remaining cards in its private draw pile.

Submit validated actions with `ProcessAction(playerId, action)`. Initial peeks are submitted as `PeekInitialCards(firstIndex, secondIndex)` by each player. Once all have peeked, the active player must `DrawCard`, then resolve the private draw by:

- `DiscardDrawnCard`, or
- `ReplaceHandCard(handIndex)` to keep the drawn card, or
- for a drawn Jack, Queen, or King only, `ActivateDrawnAbility` followed by the matching completion command: `ResolveJackAbility(handIndex)`, `ResolveQueenAbility(handIndex)`, or `ResolveKingAbility(targetPlayerId, ownHandIndex, targetHandIndex)`.

The active player's `PlayerGameView.AvailableDrawResolutions` lists only currently valid discard/replace/ability choices; `DrawResolutionRules.GetAvailableOptions(...)` omits activation when no legal target exists. An ability activation opens `AwaitingAbilityResolution` with a server-clock deadline exactly 30 seconds from activation. `AbilityDeadlineUtc` is visible only in the active player's projection; no action accepts a client-supplied time or deadline. The server must call `ProcessTimeouts()` when due, and every `ProcessAction` also processes an elapsed ability deadline before validating the submitted action. At or after the deadline, an ability completion is rejected; the drawn ability card is discarded without effect and the engine advances to `AwaitingPostResolutionDecision`.

After Jack resolution, the selected own card is known only to its owner. Queen removes the selected own card and discards it with the Queen; `PublicGameView.QueenReactionTargets` preserves the Queen rank, selected-card rank, and selected face-up card for P2B. The Queen-selected card is restored as the discard top when `CompleteReactionWindow()` closes that phase. If Queen empties its user's hand, the engine records the automatic Alzheimer call and skips the reaction phase. King swaps the selected slots without exposing either identity and makes each received card unknown to its new owner. Ability events contain no card identity; public and private projections remain separate.

All three used or expired ability cards are discarded face up. J/Q/K cards discarded without activation or kept with `ReplaceHandCard` have no ability effect. `CallAlzheimer` and `EndTurn` remain valid only after resolution. Results carry structured event types and error codes, not private card values. Obtain client-safe state from `GetPublicView()` and `GetPlayerView(playerId)`; only the latter includes that player's known cards and their pending draw.

The following executable-style example runs ordinary turns for two players:

```csharp
using Alzheimer.Core;

var nextId = 0;
var engine = AlzheimerRoundEngine.StartNewRound(
    new[]
    {
        new PlayerDefinition("host", "Host"),
        new PlayerDefinition("guest", "Guest")
    },
    "host",
    new RoomSettings(maxPlayers: 2, deckCount: 1),
    new Random(7),
    () => new Guid(++nextId, 0, 0, new byte[8]));

void Apply(string playerId, GameAction action)
{
    var result = engine.ProcessAction(playerId, action);
    if (!result.Accepted)
        throw new InvalidOperationException($"Action rejected: {result.ErrorCode}");
}

Apply("host", new PeekInitialCards(0, 1));
Apply("guest", new PeekInitialCards(0, 1));
Apply("host", new DrawCard());
var hostDraw = engine.GetPlayerView("host").PendingDrawnCard;
if (hostDraw is null)
    throw new InvalidOperationException("The host did not receive a private draw.");
Apply("host", new ReplaceHandCard(0));
Apply("host", new EndTurn());
var hostReactionWindow = engine.GetPublicView().ReactionWindowId!.Value;
Apply("host", new MarkReactionDone(hostReactionWindow, Guid.NewGuid()));
Apply("guest", new MarkReactionDone(hostReactionWindow, Guid.NewGuid()));

Apply("guest", new DrawCard());
var guestDraw = engine.GetPlayerView("guest").PendingDrawnCard;
if (guestDraw is null)
    throw new InvalidOperationException("The guest did not receive a private draw.");
Apply("guest", new DiscardDrawnCard());
Apply("guest", new EndTurn());
var guestReactionWindow = engine.GetPublicView().ReactionWindowId!.Value;
Apply("host", new MarkReactionDone(guestReactionWindow, Guid.NewGuid()));
Apply("guest", new MarkReactionDone(guestReactionWindow, Guid.NewGuid()));
```

`EndTurn` opens a 30-second reaction window using the injected authoritative UTC clock. `PublicGameView` exposes the window ID, deadline, and Done players; all players may submit `ThrowReactionCard(handIndex, reactionWindowId, commandId)` repeatedly, or mark themselves Done once with `MarkReactionDone(reactionWindowId, commandId)`. A unique command ID prevents replay, and the window ID rejects stale submissions. Throws use server-owned card ranks, match rank only against the ordinary current top, and never activate abilities. A wrong attempt leaves that card in place, privately reveals it to its owner, and immediately adds an unknown penalty card, or records +10 round-penalty points when the draw pile is empty.

Queen's two matching ranks persist for the full reaction window, and its selected card is restored to the pile top when the window closes. A successful throw that empties a hand records the automatic caller, closes reactions immediately, and preserves the active seat plus the required subsequent-turn order for P2C; Queen-empty-hand calls still cancel the upcoming window. `CompleteReactionWindow()` is a trusted progression hook that refuses to close an early window; the engine also processes due deadlines through `ProcessTimeouts()` and before accepting actions. The engine is synchronous, not thread-safe: production server code must serialize per-room commands and call timeouts from server-owned scheduling. These APIs are not wired to SignalR, and no transport, persistence, or Unity gameplay is included.

P2B implements J/Q/K abilities and reactions. P2C final-turn execution, draw-exhaustion round accounting, scoring/doubling, cumulative match completion, and round reset remain unimplemented; `RequiredFinalTurnPlayerIds` and per-player `RoundPenaltyPoints` preserve state needed for that work. The server remains lobby-only.

## GitHub Actions CI

`.github/workflows/dotnet.yml` restores the .NET projects, builds `Alzheimer.Core` and `Alzheimer.Server`, and runs the smoke/game-engine tests on pushes and pull requests targeting `main`. Unity Editor and Android builds are intentionally excluded to keep CI lightweight.

## Run the starter lobby server

```bash
dotnet run --project server/Alzheimer.Server -- --urls http://localhost:5050
```

Open `http://localhost:5050/health` and expect a JSON response with `status` set to `ok`.

SignalR hub route: `http://localhost:5050/hubs/lobby`.

Hub methods (intended for an appropriate compatible client):

- `CreateRoom(int deckCount, int maxPlayers, string playerName)` -> lobby snapshot and joins its group; 1–4 decks, 2–10 player capacity.
- `JoinRoom(string code, string playerName)` -> joins a room, returns a lobby snapshot.
- `LeaveRoom()` -> leaves current room.

Hub events: `RoomUpdated` and `RoomClosed`. The server uses in-memory state and connection IDs. This is not production-ready: no authentication, persistent rooms, reconnect handling, rate limiting or durable game state.

> The Unity client is not yet wired to SignalR. Establish an Android-compatible client transport and verify its AOT/IL2CPP behavior before relying on a particular SignalR client package in Unity. A native WebSocket message protocol is an alternative.

## Create the Unity application

The generated Unity project is at `client/Alzheimer.Client`.

1. Open Unity Hub and choose **Open** -> `D:\Users\abdal\Desktop\Alzheimer\client\Alzheimer.Client`.
2. Use Unity **6000.6.10f1**. Let Package Manager restore packages.
3. Open `Assets/Scenes/MainMenu.unity` and press **Play**.
4. Use `PLAY` to open the local `Assets/Scenes/GameTable.unity` preview.
5. The card table displays four face-down cards; tapping a card selects it and updates the status text.

The generated UI uses uGUI, a 1080×1920 Scale With Screen Size canvas, safe-area handling, and portrait layout. The starter script remains outside the Unity project for reference; the runtime UI is implemented in `Assets/Scripts/UI/`.

## Android build

1. In Unity Hub, open the project and install Android Build Support with SDK/NDK and JDK if prompted.
2. Install Android 8.0/API 26 or newer SDK support.
3. Enable USB debugging on the phone and accept the computer authorization prompt.
4. In Unity use **File -> Build Profiles**, select Android, switch platform, then build.
5. Automated build from Windows PowerShell:

```powershell
cd D:\Users\abdal\Desktop\Alzheimer\client\Alzheimer.Client
.\build-android.ps1
```

If the Unity executable is not auto-detected:

```powershell
.\build-android.ps1 -UnityExe "C:\Program Files\Unity\Hub\Editor\6000.6.10f1\Editor\Unity.exe"
```

The development IL2CPP/ARM64 APK is written to `D:\Users\abdal\Desktop\Alzheimer\builds\android\Alzheimer.apk`.

To install over USB:

```powershell
adb install -r D:\Users\abdal\Desktop\Alzheimer\builds\android\Alzheimer.apk
```

Generated `Library`, `Temp`, `Logs`, build folders, APK/AAB files, and caches are excluded by `.gitignore`.

## Suggested repository layout after importing Unity

```text
Alzheimer/
├── client/Alzheimer.Client/          # actual Unity project, created with Unity Hub
├── core/Alzheimer.Core/             # pure rules library
├── server/Alzheimer.Server/         # multiplayer server
├── tests/Alzheimer.SmokeTests/      # initial test runner
├── docs/Alzheimer_Card_Game_Rules_v1.1.md
└── README.md
```

## Roadmap

The current implementation status and P1/P2A/P2B/P2C acceptance boundaries are tracked in [docs/DEVELOPMENT_ROADMAP.md](docs/DEVELOPMENT_ROADMAP.md). P1 provides deterministic ordinary turns; P2A adds abilities; P2B adds deterministic reactions. P2C final-turn execution, scoring, and match completion remain outstanding. Unity connectivity and production concerns follow in P3/P4.

## Key implementation rules

- Only server holds actual hidden card identities. Never send other players' card identities to the clients.
- Track **knowledge per player/card-position**, not just a `faceUp` Boolean. A card replacing a hand card is face down even when its owner knows what it is. Receiving a card via King makes it unknown.
- Assign a stable ID to every physical card so 4 decks can have identical rank/suit copies.
- Use a per-room lock/serialized action queue for simultaneous throws. No player has priority by game rule, but the server must decide an order deterministically.
- Server-side absolute UTC deadlines govern ability/reaction timers; clients only display them. The core engine enforces both P2A ability and P2B reaction deadlines.
- Persist a match state transition/event log before adding real-money or ranked play. Do not rely on Unity physics/network sync for this card game.
- Confirm all special edge cases against `docs/Alzheimer_Card_Game_Rules_v1.1.md`. The repository includes P1 round foundations, P2A abilities and P2B reactions, but the server is still lobby-only and P2C scoring remains unimplemented.

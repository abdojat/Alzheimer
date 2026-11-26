# Alzheimer — Development Starter

A starter implementation for the customized Alzheimer multiplayer card game. Includes:

- The confirmed v1.1 rulebook under `docs/`.
- A pure C# rules library with deterministic P1/P2A/P2B/P2C gameplay, final turns, exhaustion-aware scoring, and match progression.
- A no-NuGet-dependency smoke test console app covering utility, rules, regression, privacy, and deterministic full-match scenarios.
- An ASP.NET Core 10 + SignalR authoritative multiplayer server with per-room game engines, versioned gameplay commands, private/public projections, serialized actions, and server-controlled deadlines.
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

The runner reports the exact number of passing assertions. Coverage includes deck/player configurations, deterministic setup and IDs, private peeks, mandatory draw, discard/replace, call timing, final-turn sequencing for 2–10 players, final-turn abilities/timeouts, rank-only reaction chains, persistent Queen matching, Done/deadlines, wrong-throw penalties and depletion, scoring/doubling, privacy-safe projections, round reset, and a complete deterministic match simulation.

## P1–P2C round engine API

`AlzheimerRoundEngine.StartNewRound(...)` creates a match in round 1, in `WaitingForInitialPeeks`. Supply the clockwise player list, host ID, `RoomSettings`, an optional seeded `Random`, optional physical-card-ID factory, and optional `IGameClock`. The engine rotates the supplied clockwise order so the host is first, deals four unknown cards to each player, and keeps the remaining cards in its private draw pile. The same engine retains cumulative scores and starts later rounds automatically.

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

Queen's two matching ranks persist for the full reaction window, and its selected card is restored to the pile top when the window closes. A successful throw that empties a hand records the automatic caller, closes reactions immediately, and preserves the active seat plus the required subsequent-turn order; Queen-empty-hand calls still cancel the upcoming window. `CompleteReactionWindow()` is a trusted progression hook that refuses to close an early window; the engine also processes due deadlines through `ProcessTimeouts()` and before accepting actions. The engine is synchronous and not thread-safe; the server serializes actions and timeout transitions through a per-room gate.

After a voluntary or automatic call, `FinalPhase` identifies the next eligible player who must draw. `RequiredFinalTurnPlayerIds` records the complete clockwise schedule and `PendingFinalTurnPlayerIds` advances as turns are completed. Final turns reuse mandatory draw, keep/discard/replace and J/Q/K ability processing; `CallAlzheimer` is disabled and `EndTurn` advances directly without a reaction window. The caller is skipped. When a reaction throw causes the call, the active player's in-progress turn is not counted as their subsequent turn.

The last normal draw resolves before the call/end decision; an uncalled last-card turn still opens its reaction window. A final-card penalty does not close an active reaction window, and each later undrawn penalty adds 10 points. On completion, `RoundScorer` sums hand values and penalties before applying the caller-specific or no-caller depletion doubling. `PublicGameView.LastCompletedRound` contains base/adjusted/cumulative scores, reason, caller, required/pending final-turn IDs, and the end-of-round hand reveal; hand identities remain private before scoring. If no cumulative score reaches 100, a fresh shuffled deck and private-knowledge reset start the next round with the same host/seating. At or above 100, `MatchCompleted` and `WinnerPlayerIds` expose the lowest cumulative-score winner(s).

The core remains the only rules engine; SignalR invokes it through an authoritative per-room server coordinator. The Unity client is not yet connected.

## GitHub Actions CI

`.github/workflows/dotnet.yml` restores the .NET projects, builds `Alzheimer.Core` and `Alzheimer.Server`, and runs the smoke/game-engine and SignalR integration test executables on pushes and pull requests targeting `main`. Unity Editor and Android builds are intentionally excluded to keep CI lightweight.

## Run the authoritative multiplayer server

```bash
dotnet run --project server/Alzheimer.Server -- --urls http://localhost:5050
```

Open `http://localhost:5050/health` and expect a JSON response with `status` set to `ok`.

SignalR hub route: `http://localhost:5050/hubs/lobby`.

Lobby methods:

- `CreateRoom(int deckCount, int maxPlayers, string playerName)` -> lobby snapshot and joins its group; 1–4 decks, 2–10 player capacity.
- `JoinRoom(string code, string playerName)` -> joins a room, returns a lobby snapshot.
- `LeaveRoom()` -> leaves current room.

Gameplay methods:

- `StartMatchV1()` -> host-only start response. Requires at least two connected players; duplicate starts and joins during an active match are rejected.
- `SubmitGameplayCommandV1(GameplayCommandV1)` -> version-1, explicitly allowlisted core action. Actor identity comes from the current SignalR connection's room membership, not the payload.

`GameplayCommandV1` contains `ProtocolVersion`, unique `CommandId`, `Action`, and only the action-specific fields (`FirstHandIndex`, `SecondHandIndex`, `HandIndex`, `TargetPlayerId`, `TargetHandIndex`, `ReactionWindowId`). Supported actions are initial peek, draw, discard, replace, ability activate/resolve (Jack/Queen/King), call Alzheimer, end turn, reaction throw, and reaction Done. It has no client-supplied card rank, score, deck state, deadline, or acting player ID. Responses carry accepted/error status, events, public view, and only the caller's private view.

Server-to-client events:

- `RoomUpdated` and `RoomClosed` retain lobby behavior.
- `MatchStartedV1` and `GameStateUpdatedV1` deliver `GamePublicUpdateV1` to the room group.
- `GamePrivateStateUpdatedV1` delivers `GamePrivateUpdateV1` directly to its entitled connection only.
- Public and private updates carry a monotonically increasing `Sequence`; clients should ignore stale lower-sequence updates. The public projection reveals hands only when the core's round-completion projection permits it.

Each active room owns one `AlzheimerRoundEngine`; its `SemaphoreSlim` serializes start, commands, timeout processing, and room lifecycle mutations. The deadline service checks engine-owned UTC deadlines every 100 ms and runs due transitions through that same gate. There are no independent per-room timer callbacks to retain after room cleanup. Network sends happen after the room gate is released.

Player IDs are random stable seat identities for the lifetime of a room and are distinct from SignalR connection IDs. This MVP has no authentication or reconnection: a disconnect during an active match abandons/removes the whole room, sends `RoomClosed` to remaining members, and revokes their ability to issue commands. Joining by the old room code does not restore access. Lobby rooms remain in-memory; no persistence or production identity security is provided.

Typical two-client session: host calls `CreateRoom`, guest calls `JoinRoom`, host calls `StartMatchV1`, both submit `PeekInitialCards`, and the active player submits `DrawCard` followed by a supported resolution and `EndTurn`. Each room member can then submit `ThrowReactionCard` or `MarkReactionDone` using the active reaction-window ID. The server drives the next turn or timeout.

CreateRoom and JoinRoom return the caller's stable `LocalPlayerId` in `LobbySnapshot` for P3B identity mapping. RoomUpdated broadcasts omit this caller-specific field. Player names are not used to identify the local seat.

## P3B local network setup

For a phone and Windows PC on the same Wi-Fi network:

```powershell
dotnet run --project server/Alzheimer.Server -- --urls http://0.0.0.0:5050
```

Find the PC IPv4 address with `ipconfig`, configure that address in the Unity server URL field, and enter `http://<PC-IP>:5050`. Do not use `localhost` on Android. Development cleartext HTTP can be blocked by Android platform settings; release builds should use HTTPS/WSS. Allow the port through Windows Firewall for local-network testing if it is blocked.

Run the reproducible two-game-client SignalR full-match scenario (the runner also creates helper clients for lifecycle and multi-room checks):

```bash
dotnet run --project tests/Alzheimer.Server.IntegrationTests -c Release
```

It starts an in-process Kestrel server, connects independent .NET SignalR clients, and validates private serialized projections, gameplay actions, concurrency, deadlines, room isolation/disconnection, a complete scored round, and match completion without real-time timer waits. The same runner is included in `.github/workflows/dotnet.yml`; GitHub CI must run after push before its status can be claimed.

> P3B-1 transport implementation is in progress in Unity: the client uses the standard SignalR JSON hub WebSocket framing over `System.Net.WebSockets.ClientWebSocket`, posts to the hub negotiate endpoint, marshals callbacks to the Unity main thread, and uses Newtonsoft.Json for protocol DTOs. Unity/Android transport and two-device behavior have not yet been executed.

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

The current implementation status and P1/P2A/P2B/P2C acceptance boundaries are tracked in [docs/DEVELOPMENT_ROADMAP.md](docs/DEVELOPMENT_ROADMAP.md). P1 provides deterministic ordinary turns; P2A adds abilities; P2B adds deterministic reactions; P2C completes final turns, exhaustion/scoring, match progression, and round reset. Unity connectivity and production concerns follow in P3/P4.

## Key implementation rules

- Only server holds actual hidden card identities. Never send other players' card identities to the clients.
- Track **knowledge per player/card-position**, not just a `faceUp` Boolean. A card replacing a hand card is face down even when its owner knows what it is. Receiving a card via King makes it unknown.
- Assign a stable ID to every physical card so 4 decks can have identical rank/suit copies.
- Use a per-room lock/serialized action queue for simultaneous throws. No player has priority by game rule, but the server must decide an order deterministically.
- Server-side absolute UTC deadlines govern ability/reaction timers; clients only display them. The core engine enforces both P2A ability and P2B reaction deadlines.
- Persist a match state transition/event log before adding real-money or ranked play. Do not rely on Unity physics/network sync for this card game.
- Confirm all special edge cases against `docs/Alzheimer_Card_Game_Rules_v1.1.md`. P1–P2C are implemented in the pure C# engine; server gameplay/networking and Unity integration remain unimplemented.

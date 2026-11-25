# Alzheimer — Development Starter

A starter implementation for the customized Alzheimer multiplayer card game. Includes:

- The confirmed v1.1 rulebook under `docs/`.
- A pure C# rules library with deck generation, card scores, reaction-rank validation, scoring utilities, and a deterministic P1 round engine.
- A no-NuGet-dependency smoke test console app covering utilities and P1 round transitions.
- An ASP.NET Core 10 + SignalR **lobby-only** server (create/join rooms and player lists). The server does **not** yet implement game turns, private card delivery, timers, or online card actions.
- A small **local UI smoke test** Unity MonoBehaviour. This is *not* a functional game.

## Requirements

- Unity Hub + Unity **6000.6.10f1** (Unity 6.6), with Android Build Support, Android SDK & NDK Tools, and OpenJDK selected.
- .NET **10 SDK** and a C# editor (VS Code with C# Dev Kit, Visual Studio, or Rider).
- Git.

## Run the core tests (macOS/Windows/Linux)

From the repository root:

```bash
dotnet run --project tests/Alzheimer.SmokeTests
```

You should see `All smoke tests passed.` The tests cover deck and player configurations, deterministic setup and IDs, private peeks, mandatory draw, discard/replace, call timing, rejection atomicity, public/private projections, turn order, card conservation, and existing card/scoring utilities.

## P1 round engine API

`AlzheimerRoundEngine.StartNewRound(...)` creates a round in `WaitingForInitialPeeks`. Supply the clockwise player list, host ID, `RoomSettings`, an optional seeded `Random`, optional physical-card-ID factory, and optional `IGameClock`. The engine rotates the supplied clockwise order so the host is first, deals four unknown cards to each player, and keeps the remaining cards in its private draw pile.

Submit validated actions with `ProcessAction(playerId, action)`. Initial peeks are submitted as `PeekInitialCards(firstIndex, secondIndex)` by each player. When all have peeked, the host may `DrawCard`, then resolve it with `DiscardDrawnCard` or `ReplaceHandCard(handIndex)`. `CallAlzheimer` and `EndTurn` are accepted only after resolution. Results carry privacy-safe event types and error codes, not card identities. Obtain client-safe state from `GetPublicView()` and `GetPlayerView(playerId)`; the latter includes only that player's known cards and their pending draw.

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
engine.CompleteReactionWindow();

Apply("guest", new DrawCard());
var guestDraw = engine.GetPlayerView("guest").PendingDrawnCard;
if (guestDraw is null)
    throw new InvalidOperationException("The guest did not receive a private draw.");
Apply("guest", new DiscardDrawnCard());
Apply("guest", new EndTurn());
engine.CompleteReactionWindow();
```

`CompleteReactionWindow()` is an engine-authoritative P1 progression hook for deterministic simulation; it does not implement reactions or a timer. In production, the server must call it only after its serialized reaction-phase processing. The engine is not thread-safe: a future per-room server actor/queue or lock must serialize commands. The engine does not expose mutable state, and the server should send only the appropriate public or per-player projection.

P1 deliberately does not implement J/Q/K abilities, reaction throws or 30-second deadline enforcement, automatic empty-hand calls, final-turn accounting, round scoring, match completion, or round reset. Calling Alzheimer enters the prepared `FinalPhase`; those game-completion mechanics remain P2.

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

The current implementation status and P1/P2 acceptance boundaries are tracked in [docs/DEVELOPMENT_ROADMAP.md](docs/DEVELOPMENT_ROADMAP.md). P1 provides deterministic ordinary turns; P2 completes abilities, reactions, final-turn accounting and scoring. Unity connectivity and production concerns follow in P3/P4.

## Key implementation rules

- Only server holds actual hidden card identities. Never send other players' card identities to the clients.
- Track **knowledge per player/card-position**, not just a `faceUp` Boolean. A card replacing a hand card is face down even when its owner knows what it is. Receiving a card via King makes it unknown.
- Assign a stable ID to every physical card so 4 decks can have identical rank/suit copies.
- Use a per-room lock/serialized action queue for simultaneous throws. No player has priority by game rule, but the server must decide an order deterministically.
- Server-side absolute UTC deadlines govern ability/reaction timers; clients only display them.
- Persist a match state transition/event log before adding real-money or ranked play. Do not rely on Unity physics/network sync for this card game.
- Confirm all special edge cases against `docs/Alzheimer_Card_Game_Rules_v1.1.md`. The repository includes the P1 deterministic round foundation, but the server is still lobby-only and P2 gameplay mechanics remain unimplemented.

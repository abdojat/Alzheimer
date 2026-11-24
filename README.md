# Alzheimer — Development Starter

A **starter scaffold** for the customized Alzheimer multiplayer card game. Includes:

- The confirmed v1.1 rulebook under `docs/`.
- A pure C# rules library with deck generation, card scores, reaction-rank validation, round doubling and match-stop logic.
- A no-NuGet-dependency smoke test console app.
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

You should see `All smoke tests passed.` The tests cover the deck configurations, card scoring, rank-based matching, Queen reaction-rank behavior, round doubling, and score-100 ending.

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

## Roadmap — implement in this order

1. **Server rule state machine**: deal 4, privately reveal 2, mandatory draw, replace/discard/keep abilities, Call/End turn.
2. **Server 30-second phases**: ability window, matching reaction window, done flags, Queen dual-rank exception, invalid throw and penalty +10 when deck is empty.
3. **Final phase and scoring**: empty-hand auto-call, single caller, caller's final-turn bookkeeping, last-card depletion, match score threshold and deck reset.
4. **Connect Unity**: create/join room, private card views, card index selection, animation and timer UI.
5. **Production**: auth/guest identities, reconnection, authoritative timers, test coverage and persistence.

## Key implementation rules

- Only server holds actual hidden card identities. Never send other players' card identities to the clients.
- Track **knowledge per player/card-position**, not just a `faceUp` Boolean. A card replacing a hand card is face down even when its owner knows what it is. Receiving a card via King makes it unknown.
- Assign a stable ID to every physical card so 4 decks can have identical rank/suit copies.
- Use a per-room lock/serialized action queue for simultaneous throws. No player has priority by game rule, but the server must decide an order deterministically.
- Server-side absolute UTC deadlines govern ability/reaction timers; clients only display them.
- Persist a match state transition/event log before adding real-money or ranked play. Do not rely on Unity physics/network sync for this card game.
- Confirm all special edge cases against `docs/Alzheimer_Card_Game_Rules_v1.1.md`; this starter deliberately implements only basic core utilities and lobby functions.

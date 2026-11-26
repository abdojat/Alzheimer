using Alzheimer.Core;
using System.Security.Cryptography;

namespace Alzheimer.Server;

public sealed record LobbyPlayer(string PlayerId, string DisplayName, bool IsHost);
public sealed record LobbySnapshot(string Code, int DeckCount, int MaxPlayers, IReadOnlyList<LobbyPlayer> Players);

internal sealed class Lobby
{
    public required string Code { get; init; }
    public required RoomSettings Settings { get; init; }
    public List<RoomPlayer> Players { get; } = new();
    public required string HostPlayerId { get; set; }
    public SemaphoreSlim ActionGate { get; } = new(1, 1);
    public AlzheimerRoundEngine? Engine { get; set; }
    public HashSet<(string PlayerId, Guid CommandId)> ProcessedCommandIds { get; } = new();
    public long UpdateSequence { get; set; }

    public LobbySnapshot Snapshot() => new(Code, Settings.DeckCount, Settings.MaxPlayers,
        Players.Select(p => new LobbyPlayer(p.PlayerId, p.DisplayName, p.PlayerId == HostPlayerId)).ToArray());
}

internal sealed record RoomPlayer(string ConnectionId, string PlayerId, string DisplayName);
internal sealed record LeaveRoomResult(
    string Code,
    LobbySnapshot? Snapshot,
    IReadOnlyList<string> ConnectionIdsToRemove);

public sealed class LobbyStore
{
    private readonly IGameClock _clock;
    private readonly Func<Random> _randomFactory;
    private readonly object _sync = new();
    private readonly Dictionary<string, Lobby> _rooms = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Lobby> _roomsByConnection = new(StringComparer.Ordinal);

    public LobbyStore(IGameClock clock, Func<Random>? randomFactory = null)
    {
        _clock = clock;
        _randomFactory = randomFactory ?? (() => new Random());
    }

    private static string NormalizeName(string name) =>
        !string.IsNullOrWhiteSpace(name) && name.Trim().Length <= 24
            ? name.Trim()
            : throw new ArgumentException("Player name must be 1–24 characters.");

    public LobbySnapshot Create(string connectionId, int deckCount, int maxPlayers, string playerName)
    {
        var settings = new RoomSettings(maxPlayers, deckCount);
        var name = NormalizeName(playerName);
        lock (_sync)
        {
            if (_roomsByConnection.ContainsKey(connectionId))
                throw new InvalidOperationException("Leave your current room first.");
            string code;
            do { code = Convert.ToHexString(RandomNumberGenerator.GetBytes(3)); }
            while (_rooms.ContainsKey(code));
            var playerId = Guid.NewGuid().ToString("N");
            var room = new Lobby { Code = code, Settings = settings, HostPlayerId = playerId };
            room.Players.Add(new RoomPlayer(connectionId, playerId, name));
            _rooms.Add(code, room);
            _roomsByConnection.Add(connectionId, room);
            return room.Snapshot();
        }
    }

    public async Task<LobbySnapshot> JoinAsync(string connectionId, string code, string playerName)
    {
        var name = NormalizeName(playerName);
        Lobby room;
        lock (_sync)
        {
            if (_roomsByConnection.ContainsKey(connectionId))
                throw new InvalidOperationException("Leave your current room first.");
            if (!_rooms.TryGetValue(code.Trim(), out var foundRoom))
                throw new KeyNotFoundException("Room not found.");
            room = foundRoom;
        }

        await room.ActionGate.WaitAsync();
        try
        {
            lock (_sync)
            {
                if (!_rooms.TryGetValue(room.Code, out var currentRoom) || !ReferenceEquals(room, currentRoom))
                    throw new KeyNotFoundException("Room not found.");
                if (_roomsByConnection.ContainsKey(connectionId))
                    throw new InvalidOperationException("Leave your current room first.");
                if (room.Engine is not null)
                    throw new InvalidOperationException("Joining an active match is not supported.");
                if (room.Players.Count >= room.Settings.MaxPlayers)
                    throw new InvalidOperationException("Room is full.");
                room.Players.Add(new RoomPlayer(connectionId, Guid.NewGuid().ToString("N"), name));
                _roomsByConnection.Add(connectionId, room);
                return room.Snapshot();
            }
        }
        finally
        {
            room.ActionGate.Release();
        }
    }

    internal async Task<LeaveRoomResult?> LeaveAsync(string connectionId)
    {
        Lobby? room;
        lock (_sync)
            _roomsByConnection.TryGetValue(connectionId, out room);
        if (room is null)
            return null;

        await room.ActionGate.WaitAsync();
        try
        {
            lock (_sync)
            {
                if (!_roomsByConnection.TryGetValue(connectionId, out var currentRoom)
                    || !ReferenceEquals(room, currentRoom))
                    return null;

                if (room.Engine is not null)
                {
                    var roomConnections = room.Players.Select(player => player.ConnectionId).ToArray();
                    foreach (var player in room.Players)
                        _roomsByConnection.Remove(player.ConnectionId);
                    _rooms.Remove(room.Code);
                    return new LeaveRoomResult(room.Code, null, roomConnections);
                }

                var leavingPlayer = room.Players.Single(player => player.ConnectionId == connectionId);
                room.Players.Remove(leavingPlayer);
                _roomsByConnection.Remove(connectionId);
                if (room.Players.Count == 0)
                {
                    _rooms.Remove(room.Code);
                    return new LeaveRoomResult(room.Code, null, new[] { connectionId });
                }
                if (room.HostPlayerId == leavingPlayer.PlayerId)
                    room.HostPlayerId = room.Players[0].PlayerId;
                return new LeaveRoomResult(room.Code, room.Snapshot(), new[] { connectionId });
            }
        }
        finally
        {
            room.ActionGate.Release();
        }
    }

    internal async Task<(StartMatchResponseV1 Response, GameStateUpdateBundle? Update)> StartMatchAsync(string connectionId)
    {
        var (room, player) = FindMembership(connectionId);
        if (room is null || player is null)
            return (new StartMatchResponseV1(1, null, false, "NotInRoom", null, null), null);

        await room.ActionGate.WaitAsync();
        try
        {
            if (!IsCurrentMembership(room, connectionId))
                return (new StartMatchResponseV1(1, null, false, "NotInRoom", null, null), null);
            if (room.Engine is not null)
                return (new StartMatchResponseV1(1, room.UpdateSequence, false, "MatchAlreadyStarted", null, null), null);
            if (player.PlayerId != room.HostPlayerId)
                return (new StartMatchResponseV1(1, null, false, "HostOnly", null, null), null);
            if (room.Players.Count < 2)
                return (new StartMatchResponseV1(1, null, false, "AtLeastTwoPlayersRequired", null, null), null);

            room.Engine = AlzheimerRoundEngine.StartNewRound(
                room.Players.Select(member => new PlayerDefinition(member.PlayerId, member.DisplayName)).ToArray(),
                room.HostPlayerId,
                room.Settings,
                random: _randomFactory(),
                clock: _clock);
            var update = CreateUpdate(room, Array.Empty<GameEvent>());
            var response = new StartMatchResponseV1(
                1,
                update.PublicUpdate.Sequence,
                true,
                null,
                update.PublicUpdate.View,
                room.Engine.GetPlayerView(player.PlayerId));
            return (response, update);
        }
        finally
        {
            room.ActionGate.Release();
        }
    }

    internal async Task<GameCommandOutcome> ExecuteCommandAsync(string connectionId, GameplayCommandV1? command)
    {
        var (room, player) = FindMembership(connectionId);
        if (room is null || player is null)
            return new GameCommandOutcome(
                new GameCommandResponseV1(1, null, command?.CommandId, false, "NotInRoom", Array.Empty<GameEvent>(), null, null),
                null);

        await room.ActionGate.WaitAsync();
        try
        {
            if (!IsCurrentMembership(room, connectionId))
                return new GameCommandOutcome(
                    new GameCommandResponseV1(1, null, command?.CommandId, false, "NotInRoom", Array.Empty<GameEvent>(), null, null),
                    null);
            if (room.Engine is null)
                return new GameCommandOutcome(
                    new GameCommandResponseV1(1, null, command?.CommandId, false, "MatchNotStarted", Array.Empty<GameEvent>(), null, null),
                    null);
            if (command is null)
                return CreateRejectedOutcome(room, player, null, "InvalidCommand");
            if (!GameplayCommandMapperV1.TryMap(command, out var action, out var validationError))
                return CreateRejectedOutcome(room, player, command.CommandId, validationError!);
            if (room.ProcessedCommandIds.Contains((player.PlayerId, command.CommandId)))
                return CreateRejectedOutcome(room, player, command.CommandId, "DuplicateCommand");

            var transition = room.Engine.ProcessAction(player.PlayerId, action);
            if (transition.Accepted || transition.Events.Count > 0)
                room.ProcessedCommandIds.Add((player.PlayerId, command.CommandId));
            var update = transition.Events.Count > 0
                ? CreateUpdate(room, transition.Events)
                : null;
            var response = new GameCommandResponseV1(
                1,
                room.UpdateSequence,
                command.CommandId,
                transition.Accepted,
                transition.ErrorCode?.ToString(),
                transition.Events,
                room.Engine.GetPublicView(),
                room.Engine.GetPlayerView(player.PlayerId));
            return new GameCommandOutcome(response, update);
        }
        finally
        {
            room.ActionGate.Release();
        }
    }

    internal async Task<GameStateUpdateBundle?> GetCurrentGameSnapshotAsync(string connectionId)
    {
        var (room, player) = FindMembership(connectionId);
        if (room is null || player is null)
            return null;

        await room.ActionGate.WaitAsync();
        try
        {
            if (!IsCurrentMembership(room, connectionId) || room.Engine is null)
                return null;

            var engine = room.Engine;
            var publicUpdate = new GamePublicUpdateV1(
                1,
                room.UpdateSequence,
                engine.GetPublicView(),
                Array.Empty<GameEvent>());
            var privateUpdate = new GamePrivateUpdateV1(
                1,
                room.UpdateSequence,
                engine.GetPlayerView(player.PlayerId));
            return new GameStateUpdateBundle(
                room.Code,
                publicUpdate,
                new[] { (connectionId, privateUpdate) });
        }
        finally
        {
            room.ActionGate.Release();
        }
    }

    internal async Task<IReadOnlyList<GameStateUpdateBundle>> ProcessDueTimeoutsAsync()
    {
        Lobby[] rooms;
        lock (_sync)
            rooms = _rooms.Values.ToArray();

        var updates = new List<GameStateUpdateBundle>();
        foreach (var room in rooms)
        {
            await room.ActionGate.WaitAsync();
            try
            {
                if (!IsRoomPresent(room) || room.Engine is null)
                    continue;

                var deadline = GetDeadline(room);
                if (deadline is null || _clock.UtcNow.ToUniversalTime() < deadline.Value)
                    continue;

                var transition = room.Engine.ProcessTimeouts();
                if (transition.Accepted && transition.Events.Count > 0)
                    updates.Add(CreateUpdate(room, transition.Events));
            }
            finally
            {
                room.ActionGate.Release();
            }
        }

        return updates;
    }

    private (Lobby? Room, RoomPlayer? Player) FindMembership(string connectionId)
    {
        lock (_sync)
        {
            if (!_roomsByConnection.TryGetValue(connectionId, out var room))
                return (null, null);
            return (room, room.Players.Single(player => player.ConnectionId == connectionId));
        }
    }

    private bool IsCurrentMembership(Lobby room, string connectionId)
    {
        lock (_sync)
            return _roomsByConnection.TryGetValue(connectionId, out var currentRoom)
                && ReferenceEquals(room, currentRoom);
    }

    private static DateTimeOffset? GetDeadline(Lobby room)
    {
        var engine = room.Engine;
        if (engine is null)
            return null;

        var publicView = engine.GetPublicView();
        return publicView.Phase == GamePhase.AwaitingReactionResolution
            ? publicView.ReactionDeadlineUtc
            : publicView.Phase == GamePhase.AwaitingAbilityResolution
                ? engine.GetPlayerView(publicView.ActivePlayerId).AbilityDeadlineUtc
                : null;
    }

    private static GameStateUpdateBundle CreateUpdate(Lobby room, IReadOnlyList<GameEvent> events)
    {
        var engine = room.Engine ?? throw new InvalidOperationException("The room has no active match.");
        var sequence = ++room.UpdateSequence;
        var publicUpdate = new GamePublicUpdateV1(1, sequence, engine.GetPublicView(), Array.AsReadOnly(events.ToArray()));
        var privateUpdates = room.Players.Select(player =>
            (player.ConnectionId, new GamePrivateUpdateV1(1, sequence, engine.GetPlayerView(player.PlayerId)))).ToArray();
        return new GameStateUpdateBundle(room.Code, publicUpdate, privateUpdates);
    }

    private static GameCommandOutcome CreateRejectedOutcome(
        Lobby room,
        RoomPlayer player,
        Guid? commandId,
        string errorCode)
    {
        var engine = room.Engine ?? throw new InvalidOperationException("The room has no active match.");
        return new GameCommandOutcome(
            new GameCommandResponseV1(
                1,
                room.UpdateSequence,
                commandId,
                false,
                errorCode,
                Array.Empty<GameEvent>(),
                engine.GetPublicView(),
                engine.GetPlayerView(player.PlayerId)),
            null);
    }

    private bool IsRoomPresent(Lobby room)
    {
        lock (_sync)
            return _rooms.TryGetValue(room.Code, out var currentRoom) && ReferenceEquals(room, currentRoom);
    }
}

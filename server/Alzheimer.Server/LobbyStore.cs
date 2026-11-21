using Alzheimer.Core;
using System.Security.Cryptography;

namespace Alzheimer.Server;

public sealed record LobbyPlayer(string PlayerId, string DisplayName, bool IsHost);
public sealed record LobbySnapshot(string Code, int DeckCount, int MaxPlayers, IReadOnlyList<LobbyPlayer> Players);

internal sealed class Lobby
{
    public required string Code { get; init; }
    public required RoomSettings Settings { get; init; }
    public List<(string ConnectionId, string PlayerId, string DisplayName)> Players { get; } = new();
    public required string HostConnectionId { get; set; }

    public LobbySnapshot Snapshot() => new(Code, Settings.DeckCount, Settings.MaxPlayers,
        Players.Select(p => new LobbyPlayer(p.PlayerId, p.DisplayName, p.ConnectionId == HostConnectionId)).ToArray());
}

public sealed class LobbyStore
{
    private readonly object _sync = new();
    private readonly Dictionary<string, Lobby> _rooms = new(StringComparer.OrdinalIgnoreCase);

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
            if (_rooms.Values.Any(r => r.Players.Any(p => p.ConnectionId == connectionId)))
                throw new InvalidOperationException("Leave your current room first.");
            string code;
            do { code = Convert.ToHexString(RandomNumberGenerator.GetBytes(3)); }
            while (_rooms.ContainsKey(code));
            var room = new Lobby { Code = code, Settings = settings, HostConnectionId = connectionId };
            room.Players.Add((connectionId, Guid.NewGuid().ToString("N"), name));
            _rooms.Add(code, room);
            return room.Snapshot();
        }
    }

    public LobbySnapshot Join(string connectionId, string code, string playerName)
    {
        var name = NormalizeName(playerName);
        lock (_sync)
        {
            if (_rooms.Values.Any(r => r.Players.Any(p => p.ConnectionId == connectionId)))
                throw new InvalidOperationException("Leave your current room first.");
            if (!_rooms.TryGetValue(code.Trim(), out var room))
                throw new KeyNotFoundException("Room not found.");
            if (room.Players.Count >= room.Settings.MaxPlayers)
                throw new InvalidOperationException("Room is full.");
            room.Players.Add((connectionId, Guid.NewGuid().ToString("N"), name));
            return room.Snapshot();
        }
    }

    public (string Code, LobbySnapshot? Snapshot)? Leave(string connectionId)
    {
        lock (_sync)
        {
            var room = _rooms.Values.FirstOrDefault(r => r.Players.Any(p => p.ConnectionId == connectionId));
            if (room is null) return null;
            room.Players.RemoveAll(p => p.ConnectionId == connectionId);
            if (room.Players.Count == 0)
            {
                _rooms.Remove(room.Code);
                return (room.Code, null);
            }
            if (room.HostConnectionId == connectionId)
                room.HostConnectionId = room.Players[0].ConnectionId;
            return (room.Code, room.Snapshot());
        }
    }
}

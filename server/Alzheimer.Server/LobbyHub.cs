using Microsoft.AspNetCore.SignalR;

namespace Alzheimer.Server;

// Minimal lobby demonstration only. No gameplay actions or card identities are transmitted here.
public sealed class LobbyHub(LobbyStore rooms) : Hub
{
    public async Task<LobbySnapshot> CreateRoom(int deckCount, int maxPlayers, string playerName)
    {
        var room = rooms.Create(Context.ConnectionId, deckCount, maxPlayers, playerName);
        await Groups.AddToGroupAsync(Context.ConnectionId, room.Code);
        await Clients.Group(room.Code).SendAsync("RoomUpdated", room);
        return room;
    }

    public async Task<LobbySnapshot> JoinRoom(string code, string playerName)
    {
        var room = rooms.Join(Context.ConnectionId, code, playerName);
        await Groups.AddToGroupAsync(Context.ConnectionId, room.Code);
        await Clients.Group(room.Code).SendAsync("RoomUpdated", room);
        return room;
    }

    public async Task LeaveRoom()
    {
        var result = rooms.Leave(Context.ConnectionId);
        if (result is null) return;
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, result.Value.Code);
        if (result.Value.Snapshot is null)
            await Clients.Group(result.Value.Code).SendAsync("RoomClosed");
        else
            await Clients.Group(result.Value.Code).SendAsync("RoomUpdated", result.Value.Snapshot);
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var result = rooms.Leave(Context.ConnectionId);
        if (result is not null)
        {
            if (result.Value.Snapshot is null)
                await Clients.Group(result.Value.Code).SendAsync("RoomClosed");
            else
                await Clients.Group(result.Value.Code).SendAsync("RoomUpdated", result.Value.Snapshot);
        }
        await base.OnDisconnectedAsync(exception);
    }
}

using Microsoft.AspNetCore.SignalR;

namespace Alzheimer.Server;

public sealed class LobbyHub(LobbyStore rooms) : Hub
{
    public async Task<LobbySnapshot> CreateRoom(int deckCount, int maxPlayers, string playerName)
    {
        var room = rooms.Create(Context.ConnectionId, deckCount, maxPlayers, playerName);
        await Groups.AddToGroupAsync(Context.ConnectionId, room.Code);
        await Clients.Group(room.Code).SendAsync("RoomUpdated", room with { LocalPlayerId = null });
        return room;
    }

    public async Task<LobbySnapshot> JoinRoom(string code, string playerName)
    {
        var room = await rooms.JoinAsync(Context.ConnectionId, code, playerName);
        await Groups.AddToGroupAsync(Context.ConnectionId, room.Code);
        await Clients.Group(room.Code).SendAsync("RoomUpdated", room with { LocalPlayerId = null });
        var currentGame = await rooms.GetCurrentGameSnapshotAsync(Context.ConnectionId);
        if (currentGame is not null)
        {
            await Clients.Client(Context.ConnectionId)
                .SendAsync("GameStateUpdatedV1", currentGame.PublicUpdate);
            foreach (var (connectionId, privateUpdate) in currentGame.PrivateUpdates)
                await Clients.Client(connectionId).SendAsync("GamePrivateStateUpdatedV1", privateUpdate);
        }
        return room;
    }

    public async Task LeaveRoom()
    {
        var result = await rooms.LeaveAsync(Context.ConnectionId);
        if (result is null) return;
        if (result.Snapshot is null)
        {
            await Clients.Group(result.Code).SendAsync("RoomClosed");
            foreach (var connectionId in result.ConnectionIdsToRemove)
                await Groups.RemoveFromGroupAsync(connectionId, result.Code);
        }
        else
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, result.Code);
            await Clients.Group(result.Code).SendAsync("RoomUpdated", result.Snapshot);
        }
    }

    public async Task<StartMatchResponseV1> StartMatchV1()
    {
        var (response, update) = await rooms.StartMatchAsync(Context.ConnectionId);
        if (update is not null)
            await PublishGameUpdate(update, "MatchStartedV1");
        return response;
    }

    public async Task<GameCommandResponseV1> SubmitGameplayCommandV1(GameplayCommandV1? command)
    {
        var outcome = await rooms.ExecuteCommandAsync(Context.ConnectionId, command);
        if (outcome.Update is not null)
            await PublishGameUpdate(outcome.Update, "GameStateUpdatedV1");
        return outcome.Response;
    }

    private async Task PublishGameUpdate(GameStateUpdateBundle update, string publicEvent)
    {
        await Clients.Group(update.RoomCode).SendAsync(publicEvent, update.PublicUpdate);
        foreach (var (connectionId, privateUpdate) in update.PrivateUpdates)
        {
            await Clients.Client(connectionId).SendAsync("GamePrivateStateUpdatedV1", privateUpdate);
        }
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var result = await rooms.LeaveAsync(Context.ConnectionId);
        if (result is not null)
        {
            if (result.Snapshot is null)
            {
                await Clients.Group(result.Code).SendAsync("RoomClosed");
                foreach (var connectionId in result.ConnectionIdsToRemove)
                    await Groups.RemoveFromGroupAsync(connectionId, result.Code);
            }
            else
                await Clients.Group(result.Code).SendAsync("RoomUpdated", result.Snapshot);
        }
        await base.OnDisconnectedAsync(exception);
    }
}

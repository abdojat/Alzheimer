using Microsoft.AspNetCore.SignalR;

namespace Alzheimer.Server;

public sealed class GameDeadlineService(
    LobbyStore rooms,
    IHubContext<LobbyHub> hub) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(100));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            var updates = await rooms.ProcessDueTimeoutsAsync();
            foreach (var update in updates)
            {
                await hub.Clients.Group(update.RoomCode)
                    .SendAsync("GameStateUpdatedV1", update.PublicUpdate, stoppingToken);
                foreach (var (connectionId, privateUpdate) in update.PrivateUpdates)
                {
                    await hub.Clients.Client(connectionId)
                        .SendAsync("GamePrivateStateUpdatedV1", privateUpdate, stoppingToken);
                }
            }
        }
    }
}

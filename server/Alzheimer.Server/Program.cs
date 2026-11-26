using Alzheimer.Core;
using Alzheimer.Server;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSignalR();
builder.Services.AddSingleton<IGameClock, SystemGameClock>();
builder.Services.AddSingleton<LobbyStore>();
builder.Services.AddHostedService<GameDeadlineService>();

var app = builder.Build();
app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "Alzheimer.Server", phase = "authoritative-multiplayer" }));
app.MapGet("/api/config", () => Results.Ok(new { minPlayers = 2, maxPlayers = 10, minDecks = 1, maxDecks = 4, cardsPerDeck = 54 }));
app.MapHub<LobbyHub>("/hubs/lobby");
app.Run();

public partial class Program { }

using System.Net;
using System.Text.Json;
using System.Threading.Channels;
using Alzheimer.Core;
using Alzheimer.Server;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

var assertions = 0;
var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
var seed = FindSeedWithJackAndKingInDrawPile();
var clock = new ManualGameClock(DateTimeOffset.UtcNow);
var builder = WebApplication.CreateBuilder();
builder.Logging.ClearProviders();
builder.WebHost.UseKestrel().UseUrls("http://127.0.0.1:0");
builder.Services.AddSignalR();
builder.Services.AddSingleton<IGameClock>(clock);
builder.Services.AddSingleton(_ => new LobbyStore(clock, () => new Random(seed)));
builder.Services.AddHostedService<GameDeadlineService>();

await using var app = builder.Build();
app.MapHub<LobbyHub>("/hubs/lobby");
await app.StartAsync();

var server = app.Services.GetRequiredService<IServer>();
var address = server.Features.Get<IServerAddressesFeature>()!.Addresses.Single();
var uri = new Uri(address.Replace("0.0.0.0", "127.0.0.1", StringComparison.Ordinal));
var host = CreateClient(uri);
var guest = CreateClient(uri);
var lateJoiner = CreateClient(uri);
var secondRoomHost = CreateClient(uri);
var secondRoomGuest = CreateClient(uri);

try
{
    await Task.WhenAll(
        host.Connection.StartAsync(),
        guest.Connection.StartAsync(),
        lateJoiner.Connection.StartAsync(),
        secondRoomHost.Connection.StartAsync(),
        secondRoomGuest.Connection.StartAsync());

    var room = await host.Connection.InvokeAsync<LobbySnapshot>("CreateRoom", 1, 3, "Host");
    Check(room.DeckCount == 1 && room.MaxPlayers == 3 && room.Players.Count == 1, "host room settings are retained");
    var insufficientPlayers = await host.Connection.InvokeAsync<StartMatchResponseV1>("StartMatchV1");
    Check(!insufficientPlayers.Accepted && insufficientPlayers.ErrorCode == "AtLeastTwoPlayersRequired", "host cannot start with one player");

    var joined = await guest.Connection.InvokeAsync<LobbySnapshot>("JoinRoom", room.Code, "Guest");
    Check(joined.Players.Count == 2 && joined.Players[0].IsHost, "guest joins in clockwise seating order");
    var nonHostStart = await guest.Connection.InvokeAsync<StartMatchResponseV1>("StartMatchV1");
    Check(!nonHostStart.Accepted && nonHostStart.ErrorCode == "HostOnly", "non-host start is rejected");

    var started = await host.Connection.InvokeAsync<StartMatchResponseV1>("StartMatchV1");
    Check(started.Accepted && started.PublicView?.DeckCount == 1, "host starts one authoritative match");
    Check(started.PublicView!.Players.Count == 2 && started.PublicView.ActivePlayerId == started.PublicView.HostPlayerId,
        "match starts with the host's clockwise turn");
    Check(started.StateSequence == 1, "public match updates expose their authoritative sequence number");
    var duplicateStart = await host.Connection.InvokeAsync<StartMatchResponseV1>("StartMatchV1");
    Check(!duplicateStart.Accepted && duplicateStart.ErrorCode == "MatchAlreadyStarted", "duplicate match start is rejected");
    _ = await ReadUntil(host.PublicUpdates.Reader, update => update.View.Phase == GamePhase.WaitingForInitialPeeks);
    _ = await ReadUntil(guest.PublicUpdates.Reader, update => update.View.Phase == GamePhase.WaitingForInitialPeeks);
    _ = await ReadUntil(guest.PrivateUpdates.Reader, update => update.View.OwnHand.Count == 4);
    var unsupportedProtocol = await Submit(host, GameplayActionTypeV1.DrawCard, protocolVersion: 2);
    Check(!unsupportedProtocol.Accepted && unsupportedProtocol.ErrorCode == "UnsupportedProtocolVersion",
        "unsupported gameplay protocol versions are explicitly rejected");
    var invalidCommandType = await Submit(host, (GameplayActionTypeV1)999);
    Check(!invalidCommandType.Accepted && invalidCommandType.ErrorCode == "InvalidCommandType",
        "command types outside the explicit allowlist are rejected");

    var activeJoinRejected = false;
    try
    {
        await lateJoiner.Connection.InvokeAsync<LobbySnapshot>("JoinRoom", room.Code, "Late");
    }
    catch (HubException)
    {
        activeJoinRejected = true;
    }
    Check(activeJoinRejected, "new sockets cannot join an active match");

    var hostPlayerId = started.PublicView.HostPlayerId;
    var guestPlayerId = started.PublicView.Players.Single(player => player.PlayerId != hostPlayerId).PlayerId;
    var hostPeek = await Submit(host, GameplayActionTypeV1.PeekInitialCards, first: 0, second: 1);
    var hostPrivateAfterPeek = await ReadUntil(
        host.PrivateUpdates.Reader,
        update => update.View.OwnHand.Count(slot => slot.KnownCard is not null) == 2);
    var hostKnownIds = hostPeek.PrivateView!.OwnHand
        .Where(slot => slot.KnownCard is not null)
        .Select(slot => slot.KnownCard!.PhysicalCardId)
        .ToArray();
    var hostPrivateJson = JsonSerializer.Serialize(hostPrivateAfterPeek, jsonOptions);
    Check(hostKnownIds.All(id => hostPrivateJson.Contains(id.ToString(), StringComparison.Ordinal)),
        "serialized private peek contains only the owner's known cards");
    Check(hostKnownIds.All(id => !JsonSerializer.Serialize(hostPeek.PublicView, jsonOptions)
            .Contains(id.ToString(), StringComparison.Ordinal)),
        "public projection omits the host's hidden card identities");

    var guestPeek = await Submit(guest, GameplayActionTypeV1.PeekInitialCards, first: 0, second: 1);
    var guestPrivateAfterPeek = await ReadUntil(
        guest.PrivateUpdates.Reader,
        update => update.View.OwnHand.Count(slot => slot.KnownCard is not null) == 2);
    var guestKnownIds = guestPeek.PrivateView!.OwnHand
        .Where(slot => slot.KnownCard is not null)
        .Select(slot => slot.KnownCard!.PhysicalCardId)
        .ToArray();
    Check(guestKnownIds.All(id => !JsonSerializer.Serialize(hostPrivateAfterPeek, jsonOptions)
            .Contains(id.ToString(), StringComparison.Ordinal)),
        "host's serialized private message omits the guest's known cards");
    Check(guestKnownIds.All(id => JsonSerializer.Serialize(guestPrivateAfterPeek, jsonOptions)
            .Contains(id.ToString(), StringComparison.Ordinal)),
        "guest's serialized private message contains the guest's own known cards");

    var firstDrawCommandId = Guid.NewGuid();
    var firstDraw = await Submit(host, GameplayActionTypeV1.DrawCard, commandId: firstDrawCommandId);
    var pendingDraw = firstDraw.PrivateView!.PendingDrawnCard!;
    Check(pendingDraw.Rank == CardRank.Jack, "deterministic server test draws the Jack ability card");
    var duplicateDraw = await Submit(host, GameplayActionTypeV1.DrawCard, commandId: firstDrawCommandId);
    Check(!duplicateDraw.Accepted && duplicateDraw.ErrorCode == "DuplicateCommand",
        "duplicate gameplay command IDs are rejected");
    Check(duplicateDraw.StateSequence == firstDraw.StateSequence,
        "responses retain the state sequence for stale-update filtering");
    var activation = await Submit(host, GameplayActionTypeV1.ActivateDrawnAbility);
    Check(activation.Accepted, "active player activates the drawn Jack");
    var jackPeek = await Submit(host, GameplayActionTypeV1.ResolveJackAbility, handIndex: 2);
    var jackPrivateView = jackPeek.PrivateView
        ?? throw new InvalidOperationException("Jack resolution omitted the owner's private view.");
    Check(jackPeek.Accepted && jackPrivateView.OwnHand[2].KnownCard is not null,
        "Jack resolves a private peek through the server command");
    var jackKnownId = jackPrivateView.OwnHand[2].KnownCard!.PhysicalCardId;
    var jackPublicUpdate = await ReadUntil(
        host.PublicUpdates.Reader,
        update => update.Sequence == jackPeek.StateSequence);
    var hostAfterJack = await ReadUntil(
        host.PrivateUpdates.Reader,
        update => update.Sequence == jackPeek.StateSequence);
    var guestAfterJack = await ReadUntil(
        guest.PrivateUpdates.Reader,
        update => update.Sequence == jackPeek.StateSequence);
    var jackPublicJson = JsonSerializer.Serialize(jackPublicUpdate, jsonOptions);
    var jackHostJson = JsonSerializer.Serialize(hostAfterJack, jsonOptions);
    var jackGuestJson = JsonSerializer.Serialize(guestAfterJack, jsonOptions);
    Check(jackHostJson.Contains(jackKnownId.ToString(), StringComparison.Ordinal),
        "Jack's revealed card is present in the owner's serialized private SignalR message");
    Check(!jackPublicJson.Contains(jackKnownId.ToString(), StringComparison.Ordinal)
        && !jackGuestJson.Contains(jackKnownId.ToString(), StringComparison.Ordinal),
        "Jack's revealed card is absent from public and opponent SignalR projections");

    var resolvedJackEnd = await Submit(host, GameplayActionTypeV1.EndTurn);
    var firstWindow = resolvedJackEnd.PublicView!.ReactionWindowId!.Value;
    var staleWindow = await Submit(host, GameplayActionTypeV1.ThrowReactionCard, handIndex: 0, windowId: firstWindow + 1);
    Check(!staleWindow.Accepted && staleWindow.ErrorCode == GameErrorCode.StaleReactionWindow.ToString(),
        "stale reaction-window IDs are rejected");
    clock.Advance(TimeSpan.FromSeconds(30));
    var reactionTimeout = await ReadUntil(
        host.PublicUpdates.Reader,
        update => update.Events.Any(gameEvent => gameEvent.Type == GameEventType.ReactionWindowCompleted)
            && update.View.Phase == GamePhase.AwaitingDraw);
    Check(reactionTimeout.View.ActivePlayerId == guestPlayerId,
        "reaction deadline advances the turn without any client action");

    var nonActiveDraw = await Submit(host, GameplayActionTypeV1.DrawCard);
    Check(!nonActiveDraw.Accepted && nonActiveDraw.ErrorCode == GameErrorCode.NotActivePlayer.ToString(),
        "non-active player cannot draw during another player's turn");

    var secondRoom = await secondRoomHost.Connection.InvokeAsync<LobbySnapshot>("CreateRoom", 1, 2, "Second host");
    _ = await secondRoomGuest.Connection.InvokeAsync<LobbySnapshot>("JoinRoom", secondRoom.Code, "Second guest");
    var secondMatch = await secondRoomHost.Connection.InvokeAsync<StartMatchResponseV1>("StartMatchV1");
    Check(secondMatch.Accepted && secondMatch.PublicView!.Phase == GamePhase.WaitingForInitialPeeks,
        "a second room owns an independent engine");
    var secondPeek = await Submit(secondRoomHost, GameplayActionTypeV1.PeekInitialCards, first: 0, second: 1);
    Check(secondPeek.Accepted && secondPeek.PublicView!.Phase == GamePhase.WaitingForInitialPeeks,
        "one room's initial peek does not advance another room");
    _ = await Submit(secondRoomGuest, GameplayActionTypeV1.PeekInitialCards, first: 0, second: 1);
    _ = await Submit(secondRoomHost, GameplayActionTypeV1.DrawCard);
    _ = await Submit(secondRoomHost, GameplayActionTypeV1.DiscardDrawnCard);
    var secondRoomReaction = await Submit(secondRoomHost, GameplayActionTypeV1.EndTurn);
    Check(secondRoomReaction.PublicView?.ReactionDeadlineUtc is not null,
        "active room has a server-owned deadline before disconnect cleanup");

    var roomClosed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    secondRoomHost.Connection.On("RoomClosed", () => roomClosed.TrySetResult());
    await secondRoomGuest.Connection.StopAsync();
    await roomClosed.Task.WaitAsync(TimeSpan.FromSeconds(5));
    var disconnectedAction = await Submit(secondRoomHost, GameplayActionTypeV1.DrawCard);
    Check(!disconnectedAction.Accepted && disconnectedAction.ErrorCode == "NotInRoom",
        "disconnect abandons the active room and revokes remaining socket membership");
    var closedRoomJoinRejected = false;
    try
    {
        await lateJoiner.Connection.InvokeAsync<LobbySnapshot>("JoinRoom", secondRoom.Code, "Late");
    }
    catch (HubException)
    {
        closedRoomJoinRejected = true;
    }
    Check(closedRoomJoinRejected, "a closed match cannot be rejoined by knowing its former room code");
    while (secondRoomHost.PublicUpdates.Reader.TryRead(out _)) { }
    clock.Advance(TimeSpan.FromSeconds(30));
    using (var noStaleTimeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(500)))
    {
        var staleTimeoutSent = false;
        try
        {
            staleTimeoutSent = await secondRoomHost.PublicUpdates.Reader.WaitToReadAsync(noStaleTimeout.Token);
        }
        catch (OperationCanceledException)
        {
        }
        Check(!staleTimeoutSent, "removed room deadlines produce no later game update");
    }

    var view = nonActiveDraw.PublicView!;
    var kingSwapVerified = false;
    var abilityTimeoutVerified = false;
    var concurrentReactionThrowsVerified = false;
    var completedRounds = 0;
    while (!view.MatchCompleted)
    {
        if (view.LastCompletedRound is { } lastCompletedRound
            && lastCompletedRound.RoundNumber > completedRounds)
        {
            completedRounds = lastCompletedRound.RoundNumber;
            if (view.MatchCompleted)
                break;

            var nextHostPeek = await Submit(host, GameplayActionTypeV1.PeekInitialCards, first: 0, second: 1);
            var nextGuestPeek = await Submit(guest, GameplayActionTypeV1.PeekInitialCards, first: 0, second: 1);
            Check(nextHostPeek.Accepted && nextGuestPeek.Accepted,
                "both players complete private peeks after the server starts a fresh round");
            view = nextGuestPeek.PublicView!;
            continue;
        }

        if (view.Phase != GamePhase.AwaitingDraw || view.DrawPileCount == 0)
            throw new InvalidOperationException($"Unexpected phase while driving the round: {view.Phase}.");

        var activeClient = view.ActivePlayerId == hostPlayerId ? host : guest;
        var draw = await Submit(activeClient, GameplayActionTypeV1.DrawCard);
        var drawPrivateView = draw.PrivateView
            ?? throw new InvalidOperationException("Draw response omitted the active player's private view.");
        Check(draw.Accepted && drawPrivateView.PendingDrawnCard is not null, "active player receives a private drawn card");
        var rank = drawPrivateView.PendingDrawnCard!.Rank;
        view = draw.PublicView!;

        if (rank == CardRank.King && !kingSwapVerified)
        {
            var otherPlayerId = view.ActivePlayerId == hostPlayerId ? guestPlayerId : hostPlayerId;
            var targetKnownId = otherPlayerId == hostPlayerId ? hostKnownIds[0] : guestKnownIds[0];
            var activeKnownId = drawPrivateView.OwnHand[2].KnownCard?.PhysicalCardId;
            var kingActivation = await Submit(activeClient, GameplayActionTypeV1.ActivateDrawnAbility);
            Check(kingActivation.Accepted, "King ability activation is accepted for the active player");
            var swap = await Submit(
                activeClient,
                GameplayActionTypeV1.ResolveKingAbility,
                handIndex: 2,
                targetPlayerId: otherPlayerId,
                targetHandIndex: 0);
            Check(swap.Accepted, "King resolves a blind swap through the server command");
            var publicSwapUpdate = await ReadUntil(
                host.PublicUpdates.Reader,
                update => update.Sequence == swap.StateSequence);
            var hostAfterSwap = await ReadUntil(host.PrivateUpdates.Reader, update => update.Sequence == swap.StateSequence);
            var guestAfterSwap = await ReadUntil(guest.PrivateUpdates.Reader, update => update.Sequence == swap.StateSequence);
            var publicSwapJson = JsonSerializer.Serialize(publicSwapUpdate, jsonOptions);
            var hostSwapJson = JsonSerializer.Serialize(hostAfterSwap, jsonOptions);
            var guestSwapJson = JsonSerializer.Serialize(guestAfterSwap, jsonOptions);
            var swappedKnownIds = new[] { targetKnownId, activeKnownId }.Where(id => id.HasValue).Select(id => id!.Value);
            Check(swappedKnownIds.All(id => !publicSwapJson.Contains(id.ToString(), StringComparison.Ordinal)
                    && !hostSwapJson.Contains(id.ToString(), StringComparison.Ordinal)
                    && !guestSwapJson.Contains(id.ToString(), StringComparison.Ordinal)),
                "King swap keeps both exchanged identities absent from public and private messages");
            var actorPrivate = view.ActivePlayerId == hostPlayerId ? hostAfterSwap.View : guestAfterSwap.View;
            var targetPrivate = otherPlayerId == hostPlayerId ? hostAfterSwap.View : guestAfterSwap.View;
            Check(actorPrivate.OwnHand[2].Knowledge == CardKnowledge.Unknown
                && targetPrivate.OwnHand[0].Knowledge == CardKnowledge.Unknown,
                "King swap invalidates knowledge at both received hand positions");
            kingSwapVerified = true;
            view = swap.PublicView!;
        }
        else if (rank is CardRank.Jack or CardRank.Queen or CardRank.King
            && !abilityTimeoutVerified)
        {
            var abilityActivation = await Submit(activeClient, GameplayActionTypeV1.ActivateDrawnAbility);
            Check(abilityActivation.Accepted, "special ability opens a server-controlled deadline");
            clock.Advance(TimeSpan.FromSeconds(30));
            var abilityTimeout = await ReadUntil(
                host.PublicUpdates.Reader,
                update => update.Events.Any(gameEvent => gameEvent.Type == GameEventType.AbilityExpired));
            Check(abilityTimeout.View.Phase == GamePhase.AwaitingPostResolutionDecision,
                "ability deadline expires without a client submission");
            abilityTimeoutVerified = true;
            view = abilityTimeout.View;
        }
        else
        {
            var discard = await Submit(activeClient, GameplayActionTypeV1.DiscardDrawnCard);
            Check(discard.Accepted, "drawn card resolves through the core discard action");
            view = discard.PublicView!;
        }

        var endTurn = await Submit(activeClient, GameplayActionTypeV1.EndTurn);
        Check(endTurn.Accepted && endTurn.PublicView?.Phase == GamePhase.AwaitingReactionResolution,
            "resolved turn opens the authoritative reaction phase");
        view = endTurn.PublicView!;
        var windowId = view.ReactionWindowId!.Value;

        if (kingSwapVerified && !concurrentReactionThrowsVerified)
        {
            var beforeThrows = view.DrawPileCount;
            var hostThrow = Submit(host, GameplayActionTypeV1.ThrowReactionCard, handIndex: 0, windowId: windowId);
            var guestThrow = Submit(guest, GameplayActionTypeV1.ThrowReactionCard, handIndex: 0, windowId: windowId);
            var throwResults = await Task.WhenAll(hostThrow, guestThrow);
            Check(throwResults.All(result => result.Accepted),
                "simultaneous reaction throws are serialized and accepted safely");
            var invalidThrows = throwResults.Count(result =>
                result.Events.Any(gameEvent => gameEvent.Type == GameEventType.IncorrectReactionThrow));
            var resultingDrawPileCount = throwResults.Min(result => result.PublicView!.DrawPileCount);
            Check(resultingDrawPileCount == beforeThrows - invalidThrows,
                "concurrent reaction penalties consume exactly one authoritative card each");
            concurrentReactionThrowsVerified = true;
        }

        var hostDone = Submit(host, GameplayActionTypeV1.MarkReactionDone, windowId: windowId);
        var guestDone = Submit(guest, GameplayActionTypeV1.MarkReactionDone, windowId: windowId);
        var doneResults = await Task.WhenAll(hostDone, guestDone);
        Check(doneResults.All(result => result.Accepted), "both players can irreversibly mark reaction Done");
        view = doneResults.Select(result => result.PublicView)
            .FirstOrDefault(publicView => publicView?.Phase != GamePhase.AwaitingReactionResolution)
            ?? throw new InvalidOperationException("Closing the reaction window did not return the next phase.");
    }

    Check(kingSwapVerified, "deterministic match exercised King swap privacy");
    Check(abilityTimeoutVerified, "deterministic match exercised ability timeout");
    Check(concurrentReactionThrowsVerified, "deterministic match exercised concurrent reaction throws");
    Check(view.LastCompletedRound!.Reason == RoundCompletionReason.DrawPileExhausted
        && view.LastCompletedRound.RevealedHands.Count == 2
        && view.LastCompletedRound.Scores.Count == 2,
        "two SignalR clients complete a full round and receive its scoring projection");
    Check(view.MatchCompleted && view.WinnerPlayerIds.Count > 0,
        "two SignalR clients complete a full match without real-time deadline waits");

    Console.WriteLine($"Passed {assertions} SignalR integration assertions.");
}
finally
{
    await Task.WhenAll(
        host.Connection.DisposeAsync().AsTask(),
        guest.Connection.DisposeAsync().AsTask(),
        lateJoiner.Connection.DisposeAsync().AsTask(),
        secondRoomHost.Connection.DisposeAsync().AsTask(),
        secondRoomGuest.Connection.DisposeAsync().AsTask());
    await app.StopAsync();
}

async Task<GameCommandResponseV1> Submit(
    ClientHarness client,
    GameplayActionTypeV1 action,
    int? first = null,
    int? second = null,
    int? handIndex = null,
    string? targetPlayerId = null,
    int? targetHandIndex = null,
    long? windowId = null,
    int protocolVersion = 1,
    Guid? commandId = null)
{
    var command = new GameplayCommandV1(
        protocolVersion,
        commandId ?? Guid.NewGuid(),
        action,
        first,
        second,
        handIndex,
        targetPlayerId,
        targetHandIndex,
        windowId);
    return await client.Connection.InvokeAsync<GameCommandResponseV1>("SubmitGameplayCommandV1", command);
}

static ClientHarness CreateClient(Uri serverUri)
{
    var connection = new HubConnectionBuilder().WithUrl(new Uri(serverUri, "/hubs/lobby")).Build();
    var publicUpdates = Channel.CreateUnbounded<GamePublicUpdateV1>();
    var privateUpdates = Channel.CreateUnbounded<GamePrivateUpdateV1>();
    connection.On<GamePublicUpdateV1>("MatchStartedV1", update => publicUpdates.Writer.TryWrite(update));
    connection.On<GamePublicUpdateV1>("GameStateUpdatedV1", update => publicUpdates.Writer.TryWrite(update));
    connection.On<GamePrivateUpdateV1>("GamePrivateStateUpdatedV1", update => privateUpdates.Writer.TryWrite(update));
    return new ClientHarness(connection, publicUpdates, privateUpdates);
}

static async Task<T> ReadUntil<T>(ChannelReader<T> reader, Func<T, bool> predicate)
{
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
    while (true)
    {
        var item = await reader.ReadAsync(timeout.Token);
        if (predicate(item))
            return item;
    }
}

static int FindSeedWithJackAndKingInDrawPile()
{
    for (var candidate = 1; candidate < 100_000; candidate++)
    {
        var nextId = 0;
        var cards = DeckFactory.CreateShuffled(
            1,
            new Random(candidate),
            () => new Guid(++nextId, 0, 0, new byte[8]));
        if (cards[^1].Rank == CardRank.Jack && cards.Skip(8).Any(card => card.Rank == CardRank.King))
            return candidate;
    }

    throw new InvalidOperationException("Could not find a deterministic deck seed.");
}

void Check(bool condition, string description)
{
    if (!condition)
        throw new InvalidOperationException("FAILED: " + description);
    assertions++;
    Console.WriteLine("PASS: " + description);
}

sealed record ClientHarness(
    HubConnection Connection,
    Channel<GamePublicUpdateV1> PublicUpdates,
    Channel<GamePrivateUpdateV1> PrivateUpdates);

sealed class ManualGameClock(DateTimeOffset initialUtc) : IGameClock
{
    private readonly object _sync = new();
    private DateTimeOffset _utcNow = initialUtc.ToUniversalTime();

    public DateTimeOffset UtcNow
    {
        get
        {
            lock (_sync)
                return _utcNow;
        }
    }

    public void Advance(TimeSpan duration)
    {
        lock (_sync)
            _utcNow = _utcNow.Add(duration);
    }
}

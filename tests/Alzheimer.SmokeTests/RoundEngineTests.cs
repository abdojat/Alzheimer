using Alzheimer.Core;
using System.Text.Json;

internal static class RoundEngineTests
{
    public static void Run(Action<bool, string> check)
    {
        TestDeckAndPlayerConfigurations(check);
        TestInitialPeeksAndPrivacy(check);
        TestRejectedActionsAreAtomic(check);
        TestDrawDiscardAndClockwiseTurns(check);
        TestReplacementAndCallerTiming(check);
        TestUnknownCardReplacementAndLastDraw(check);
        TestSpecialCardsCanBeKeptWithoutAbilities(check);
        TestTwoPlayerExample(check);
        TestDeterministicMultiTurnSimulation(check);
    }

    private static void TestDeckAndPlayerConfigurations(Action<bool, string> check)
    {
        for (var deckCount = 1; deckCount <= 4; deckCount++)
        for (var playerCount = 2; playerCount <= 10; playerCount++)
        {
            var engine = CreateEngine(playerCount, deckCount, seed: deckCount * 100 + playerCount);
            var view = engine.GetPublicView();
            check(view.DeckCount == deckCount, $"{deckCount} deck(s), {playerCount} players: deck count retained");
            check(view.Players.Count == playerCount, $"{deckCount} deck(s), {playerCount} players: player count retained");
            check(view.Phase == GamePhase.WaitingForInitialPeeks, $"{deckCount} deck(s), {playerCount} players: wait for peeks");
            check(view.ActivePlayerId == "P1", $"{deckCount} deck(s), {playerCount} players: host starts");
            check(view.DrawPileCount == deckCount * 54 - playerCount * 4,
                $"{deckCount} deck(s), {playerCount} players: exact initial deal and pile count");
            check(view.Players.All(player => player.HandCount == 4),
                $"{deckCount} deck(s), {playerCount} players: four initial hand cards each");

            var deterministicA = DeckFactory.CreateShuffled(deckCount, new Random(17), CreateIdFactory());
            var deterministicB = DeckFactory.CreateShuffled(deckCount, new Random(17), CreateIdFactory());
            check(deterministicA.SequenceEqual(deterministicB),
                $"{deckCount} deck(s): seeded shuffle and injected IDs are deterministic");
            check(deterministicA.Select(card => card.PhysicalCardId).Distinct().Count() == deckCount * 54,
                $"{deckCount} deck(s): physical card IDs are unique");
        }

        var duplicateId = new Guid(1, 0, 0, new byte[8]);
        var duplicateRejected = false;
        try
        {
            DeckFactory.CreateShuffled(1, new Random(1), () => duplicateId);
        }
        catch (InvalidOperationException)
        {
            duplicateRejected = true;
        }
        check(duplicateRejected, "duplicate physical card IDs are rejected");
    }

    private static void TestInitialPeeksAndPrivacy(Action<bool, string> check)
    {
        var engine = CreateEngine(3, 1, seed: 201);
        var initial = engine.GetPublicView();
        var initialJson = JsonSerializer.Serialize(initial);
        check(initial.Phase == GamePhase.WaitingForInitialPeeks, "round waits for every initial peek");
        check(!initialJson.Contains("OwnHand", StringComparison.Ordinal), "public projection has no private hand property");

        var duplicateIndices = engine.ProcessAction("P1", new PeekInitialCards(1, 1));
        check(!duplicateIndices.Accepted && duplicateIndices.ErrorCode == GameErrorCode.InvalidPeekSelection,
            "initial peek rejects duplicate positions");
        var invalidIndex = engine.ProcessAction("P1", new PeekInitialCards(-1, 2));
        check(!invalidIndex.Accepted && invalidIndex.ErrorCode == GameErrorCode.InvalidPeekSelection,
            "initial peek rejects invalid positions");

        var firstPeek = engine.ProcessAction("P2", new PeekInitialCards(0, 2));
        check(firstPeek.Accepted, "a player can privately peek at two of their own cards");
        var duplicatePeek = engine.ProcessAction("P2", new PeekInitialCards(1, 3));
        check(!duplicatePeek.Accepted && duplicatePeek.ErrorCode == GameErrorCode.InitialPeekAlreadyCompleted,
            "a player cannot take extra initial peeks");
        check(engine.GetPublicView().Phase == GamePhase.WaitingForInitialPeeks,
            "round remains blocked until all players peek");

        engine.ProcessAction("P1", new PeekInitialCards(1, 3));
        engine.ProcessAction("P3", new PeekInitialCards(0, 1));
        var view = engine.GetPublicView();
        check(view.Phase == GamePhase.AwaitingDraw && view.ActivePlayerId == "P1",
            "host turn begins after all initial peeks");
        check(view.Players.All(player => player.HandCount == 4), "peeking does not remove cards");

        var p1 = engine.GetPlayerView("P1");
        var p2 = engine.GetPlayerView("P2");
        var p3 = engine.GetPlayerView("P3");
        check(p1.OwnHand.Count(slot => slot.KnownCard is not null) == 2, "player one knows exactly two cards");
        check(p2.OwnHand.Count(slot => slot.KnownCard is not null) == 2, "player two knows exactly two cards");
        check(p3.OwnHand.Count(slot => slot.KnownCard is not null) == 2, "player three knows exactly two cards");
        check(p1.OwnHand.Count(slot => slot.Knowledge == CardKnowledge.Unknown) == 2,
            "each player's other two cards remain unknown");
        var allPeekedIds = new[] { p1, p2, p3 }
            .SelectMany(player => player.OwnHand)
            .Where(slot => slot.KnownCard is not null)
            .Select(slot => slot.KnownCard!.PhysicalCardId)
            .ToArray();
        check(allPeekedIds.Distinct().Count() == 6, "initially revealed physical cards are distinct");

        var publicJson = JsonSerializer.Serialize(view);
        check(allPeekedIds.All(id => !publicJson.Contains(id.ToString(), StringComparison.Ordinal)),
            "public projection does not disclose known hand identities");
        check(!JsonSerializer.Serialize(p1).Contains(p2.OwnHand[0].KnownCard!.PhysicalCardId.ToString(),
            StringComparison.Ordinal), "a player's projection does not expose another player's known cards");
        check(p1.OwnHand.All(slot => slot.KnownCard is null || slot.Knowledge == CardKnowledge.KnownToOwner),
            "private hand exposes only owner-known card identities");
    }

    private static void TestRejectedActionsAreAtomic(Action<bool, string> check)
    {
        var engine = CreateEngine(2, 1, seed: 301);
        var before = Snapshot(engine, "P1", "P2");
        var unknown = engine.ProcessAction("missing", new DrawCard());
        check(!unknown.Accepted && unknown.ErrorCode == GameErrorCode.InvalidPlayer, "unknown player ID is rejected");
        check(Snapshot(engine, "P1", "P2") == before, "unknown player action changes no state");

        var callTooEarly = engine.ProcessAction("P1", new CallAlzheimer());
        check(!callTooEarly.Accepted && callTooEarly.ErrorCode == GameErrorCode.InvalidPhase,
            "Alzheimer cannot be called before drawing");
        check(Snapshot(engine, "P1", "P2") == before, "out-of-phase call changes no state");

        var drawBeforePeeks = engine.ProcessAction("P1", new DrawCard());
        check(!drawBeforePeeks.Accepted && drawBeforePeeks.ErrorCode == GameErrorCode.InvalidPhase,
            "normal draw is unavailable before all initial peeks");
        check(Snapshot(engine, "P1", "P2") == before, "out-of-phase draw changes no state");

        engine.ProcessAction("P1", new PeekInitialCards(0, 1));
        engine.ProcessAction("P2", new PeekInitialCards(0, 1));
        before = Snapshot(engine, "P1", "P2");
        var otherPlayerDraw = engine.ProcessAction("P2", new DrawCard());
        check(!otherPlayerDraw.Accepted && otherPlayerDraw.ErrorCode == GameErrorCode.NotActivePlayer,
            "only the active player can draw");
        check(Snapshot(engine, "P1", "P2") == before, "unauthorized draw changes no state");

        var hostDraw = engine.ProcessAction("P1", new DrawCard());
        check(hostDraw.Accepted, "active host can draw");
        before = Snapshot(engine, "P1", "P2");
        var doubleDraw = engine.ProcessAction("P1", new DrawCard());
        check(!doubleDraw.Accepted && doubleDraw.ErrorCode == GameErrorCode.InvalidPhase,
            "double draw is rejected");
        check(Snapshot(engine, "P1", "P2") == before, "double draw rejection changes no state");

        var otherPlayerSelection = engine.ProcessAction("P2", new ReplaceHandCard(0));
        check(!otherPlayerSelection.Accepted && otherPlayerSelection.ErrorCode == GameErrorCode.NotActivePlayer,
            "another player cannot select a hand position");
        check(Snapshot(engine, "P1", "P2") == before, "unauthorized hand selection changes no state");

        var invalidPosition = engine.ProcessAction("P1", new ReplaceHandCard(4));
        check(!invalidPosition.Accepted && invalidPosition.ErrorCode == GameErrorCode.InvalidHandIndex,
            "hand replacement rejects positions outside the active player's hand");
        check(Snapshot(engine, "P1", "P2") == before, "invalid hand selection changes no state");
    }

    private static void TestDrawDiscardAndClockwiseTurns(Action<bool, string> check)
    {
        var clock = new FixedClock(new DateTimeOffset(2026, 11, 25, 0, 0, 0, TimeSpan.Zero));
        var engine = CreateEngine(3, 1, seed: 401, hostPlayerId: "P2", clock: clock);
        CompletePeeks(engine, "P2", "P3", "P1");
        check(engine.GetPublicView().ActivePlayerId == "P2", "host starts even when not first in input order");
        check(engine.GetPublicView().Players.Select(player => player.PlayerId)
            .SequenceEqual(new[] { "P2", "P3", "P1" }), "clockwise order rotates from the host seat");

        var drawBefore = engine.GetPublicView().DrawPileCount;
        var drawn = engine.ProcessAction("P2", new DrawCard());
        check(drawn.Accepted && drawn.OccurredAtUtc == clock.UtcNow, "draw result uses the injected UTC clock");
        var pendingId = engine.GetPlayerView("P2").PendingDrawnCard!.PhysicalCardId;
        check(engine.GetPlayerView("P3").PendingDrawnCard is null, "pending draw is private to the active player");
        check(engine.GetPublicView().DrawPileCount == drawBefore - 1, "draw consumes exactly one card");
        var endBeforeResolution = engine.ProcessAction("P2", new EndTurn());
        check(!endBeforeResolution.Accepted && endBeforeResolution.ErrorCode == GameErrorCode.InvalidPhase,
            "turn cannot end before the drawn card is resolved");

        var discarded = engine.ProcessAction("P2", new DiscardDrawnCard());
        check(discarded.Accepted && engine.GetPublicView().Phase == GamePhase.AwaitingPostResolutionDecision,
            "active player can discard the drawn card");
        var publicAfterDiscard = engine.GetPublicView();
        check(publicAfterDiscard.Players.Single(player => player.PlayerId == "P2").HandCount == 4,
            "discarding the drawn card leaves hand size unchanged");
        check(publicAfterDiscard.DiscardPile.Last().PhysicalCardId == pendingId,
            "drawn card becomes face up in discard history");
        check(engine.GetPlayerView("P2").PendingDrawnCard is null, "resolved card is no longer pending");
        var duplicateDiscard = engine.ProcessAction("P2", new DiscardDrawnCard());
        check(!duplicateDiscard.Accepted && duplicateDiscard.ErrorCode == GameErrorCode.InvalidPhase,
            "duplicate drawn-card resolution is rejected");

        var ended = engine.ProcessAction("P2", new EndTurn());
        check(ended.Accepted && engine.GetPublicView().Phase == GamePhase.AwaitingReactionResolution,
            "end turn opens the reaction-resolution phase");
        var callDuringReaction = engine.ProcessAction("P2", new CallAlzheimer());
        check(!callDuringReaction.Accepted && callDuringReaction.ErrorCode == GameErrorCode.InvalidPhase,
            "Alzheimer cannot be called during reaction resolution");
        var advanced = CompleteByMarkingDone(engine);
        check(advanced.Accepted && engine.GetPublicView().ActivePlayerId == "P3",
            "authoritative reaction completion advances clockwise");
        check(engine.GetPublicView().Phase == GamePhase.AwaitingDraw, "next turn requires a new draw");
        var outOfPhaseCompletion = engine.CompleteReactionWindow();
        check(!outOfPhaseCompletion.Accepted && outOfPhaseCompletion.ErrorCode == GameErrorCode.InvalidPhase,
            "reaction completion hook rejects calls outside its phase");
    }

    private static void TestReplacementAndCallerTiming(Action<bool, string> check)
    {
        var engine = CreateEngine(2, 1, seed: 501);
        CompletePeeks(engine, "P1", "P2");
        var beforeHand = engine.GetPlayerView("P1").OwnHand;
        var knownOldId = beforeHand[0].KnownCard!.PhysicalCardId;
        check(beforeHand[2].Knowledge == CardKnowledge.Unknown, "replacement case begins with unknown hand knowledge");

        engine.ProcessAction("P1", new DrawCard());
        var drawn = engine.GetPlayerView("P1").PendingDrawnCard!;
        var earlyCall = engine.ProcessAction("P1", new CallAlzheimer());
        check(!earlyCall.Accepted && earlyCall.ErrorCode == GameErrorCode.InvalidPhase,
            "Alzheimer cannot be called before drawn-card resolution");
        var replacement = engine.ProcessAction("P1", new ReplaceHandCard(0));
        check(replacement.Accepted, "active player can replace a previously known hand card");
        var replacedView = engine.GetPlayerView("P1");
        check(replacedView.OwnHand.Count == 4, "replacement preserves hand size");
        check(replacedView.OwnHand[0].KnownCard == drawn, "newly placed face-down card remains known to its owner");
        check(replacedView.OwnHand[0].Knowledge == CardKnowledge.KnownToOwner,
            "replacement sets owner knowledge independently of face-down status");
        check(replacedView.Public.DiscardPile.Last().PhysicalCardId == knownOldId,
            "replaced known card is discarded face up");
        check(replacedView.OwnHand[2].Knowledge == CardKnowledge.Unknown && replacedView.OwnHand[2].KnownCard is null,
            "other unknown hand positions stay unknown");

        var called = engine.ProcessAction("P1", new CallAlzheimer());
        check(called.Accepted && engine.GetPublicView().CallerId == "P1", "caller is recorded after card resolution");
        check(engine.GetPublicView().Phase == GamePhase.FinalPhase
            && engine.GetPublicView().IsFinalTurnMode
            && engine.GetPublicView().ActivePlayerId == "P2",
            "a call immediately starts the next player's executable final turn");
        var secondCall = engine.ProcessAction("P2", new CallAlzheimer());
        check(!secondCall.Accepted && secondCall.ErrorCode == GameErrorCode.CallerAlreadyRecorded
            && engine.GetPublicView().CallerId == "P1",
            "a later player cannot replace the round's caller");
        var callerDraw = engine.ProcessAction("P1", new DrawCard());
        check(!callerDraw.Accepted && callerDraw.ErrorCode == GameErrorCode.NotActivePlayer,
            "caller is skipped while the other player takes a final turn");
        check(!engine.GetPlayerView("P2").CanCallAlzheimer && !engine.GetPlayerView("P2").CanEndTurn,
            "final turn still requires its mandatory draw");
        engine.ProcessAction("P2", new DrawCard());
        engine.ProcessAction("P2", new DiscardDrawnCard());
        check(!engine.GetPlayerView("P2").CanCallAlzheimer && engine.GetPlayerView("P2").CanEndTurn,
            "post-resolution final prompt disables a second call and permits turn completion");
    }

    private static void TestUnknownCardReplacementAndLastDraw(Action<bool, string> check)
    {
        var engine = CreateEngine(2, 1, seed: 601);
        CompletePeeks(engine, "P1", "P2");
        check(engine.GetPlayerView("P1").OwnHand[3].Knowledge == CardKnowledge.Unknown,
            "unknown-card replacement starts with an unknown slot");
        engine.ProcessAction("P1", new DrawCard());
        var drawn = engine.GetPlayerView("P1").PendingDrawnCard!;
        var replaced = engine.ProcessAction("P1", new ReplaceHandCard(3));
        check(replaced.Accepted, "unknown hand card can be replaced");
        check(engine.GetPlayerView("P1").OwnHand[3].KnownCard == drawn,
            "replacement of unknown position leaves the new card known to its owner");
        check(engine.GetPlayerView("P1").OwnHand.Count == 4, "unknown-card replacement preserves hand size");
        check(engine.GetPublicView().DiscardPile.Count == 1, "unknown replaced card is moved to public discard");

        var exhaustion = CreateEngine(2, 1, seed: 602);
        CompletePeeks(exhaustion, "P1", "P2");
        var turn = 0;
        while (exhaustion.GetPublicView().DrawPileCount > 0)
        {
            var activePlayer = exhaustion.GetPublicView().ActivePlayerId;
            check(exhaustion.ProcessAction(activePlayer, new DrawCard()).Accepted,
                $"depletion simulation draw {turn + 1} succeeds");
            if (turn % 2 == 0)
                check(exhaustion.ProcessAction(activePlayer, new DiscardDrawnCard()).Accepted,
                    $"depletion simulation resolution {turn + 1} discards");
            else
                check(exhaustion.ProcessAction(activePlayer, new ReplaceHandCard(0)).Accepted,
                    $"depletion simulation resolution {turn + 1} replaces");

            if (exhaustion.GetPublicView().DrawPileCount == 0)
                check(exhaustion.GetPublicView().Phase == GamePhase.AwaitingPostResolutionDecision,
                    "last normal draw is fully resolved before the call/end prompt");
            check(exhaustion.ProcessAction(activePlayer, new EndTurn()).Accepted,
                $"depletion simulation end turn {turn + 1} starts reaction phase");
            if (exhaustion.GetPublicView().DrawPileCount == 0)
            {
                check(exhaustion.GetPublicView().Phase == GamePhase.AwaitingReactionResolution,
                    "last draw still opens its reaction-resolution phase");
                check(CompleteByMarkingDone(exhaustion).Accepted
                    && exhaustion.GetPublicView().RoundNumber == 2
                    && exhaustion.GetPublicView().LastCompletedRound?.Reason == RoundCompletionReason.DrawPileExhausted,
                    "round is scored and the next round starts only after the last draw reaction hook");
                break;
            }

            check(CompleteByMarkingDone(exhaustion).Accepted,
                $"depletion simulation reaction hook {turn + 1} advances");
            turn++;
        }
    }

    private static void TestSpecialCardsCanBeKeptWithoutAbilities(Action<bool, string> check)
    {
        foreach (var rank in new[] { CardRank.Jack, CardRank.Queen, CardRank.King })
        {
            var engine = CreateEngineWithNextDraw(rank);
            var drawn = engine.GetPlayerView("P1").PendingDrawnCard!;
            check(drawn.Rank == rank, $"deterministic simulation draws {rank}");
            var replace = engine.ProcessAction("P1", new ReplaceHandCard(2));
            check(replace.Accepted, $"drawn {rank} can be kept by replacing a hand card");
            check(engine.GetPublicView().Phase == GamePhase.AwaitingPostResolutionDecision,
                $"keeping drawn {rank} does not activate an ability");
            check(engine.GetPlayerView("P1").OwnHand[2].KnownCard == drawn,
                $"kept {rank} remains known to its owner");
        }
    }

    private static void TestTwoPlayerExample(Action<bool, string> check)
    {
        var engine = CreateEngine(2, 1, seed: 681);
        CompletePeeks(engine, "P1", "P2");

        check(engine.ProcessAction("P1", new DrawCard()).Accepted, "two-player example host draws");
        check(engine.GetPlayerView("P1").PendingDrawnCard is not null, "two-player example host privately sees draw");
        check(engine.ProcessAction("P1", new ReplaceHandCard(0)).Accepted,
            "two-player example host resolves by replacement");
        check(engine.ProcessAction("P1", new EndTurn()).Accepted, "two-player example host ends turn");
        check(CompleteByMarkingDone(engine).Accepted, "two-player example host phase completes");

        check(engine.GetPublicView().ActivePlayerId == "P2", "two-player example advances to guest");
        check(engine.ProcessAction("P2", new DrawCard()).Accepted, "two-player example guest draws");
        check(engine.GetPlayerView("P2").PendingDrawnCard is not null, "two-player example guest privately sees draw");
        check(engine.ProcessAction("P2", new DiscardDrawnCard()).Accepted,
            "two-player example guest resolves by discard");
        check(engine.ProcessAction("P2", new EndTurn()).Accepted, "two-player example guest ends turn");
        check(CompleteByMarkingDone(engine).Accepted, "two-player example guest phase completes");
        check(engine.GetPublicView().ActivePlayerId == "P1", "two-player example returns clockwise to host");
    }

    private static void TestDeterministicMultiTurnSimulation(Action<bool, string> check)
    {
        var engine = CreateEngine(4, 2, seed: 701, hostPlayerId: "P3");
        var clockwiseOrder = new[] { "P3", "P4", "P1", "P2" };
        CompletePeeks(engine, clockwiseOrder);

        for (var turn = 0; turn < 12; turn++)
        {
            var activePlayer = clockwiseOrder[turn % clockwiseOrder.Length];
            check(engine.GetPublicView().ActivePlayerId == activePlayer,
                $"multi-turn simulation turn {turn + 1} has correct clockwise owner");
            check(engine.ProcessAction(activePlayer, new DrawCard()).Accepted,
                $"multi-turn simulation turn {turn + 1} draws exactly once");
            if (turn % 2 == 0)
                check(engine.ProcessAction(activePlayer, new DiscardDrawnCard()).Accepted,
                    $"multi-turn simulation turn {turn + 1} resolves by discard");
            else
                check(engine.ProcessAction(activePlayer, new ReplaceHandCard(turn % 4)).Accepted,
                    $"multi-turn simulation turn {turn + 1} resolves by replacement");

            check(engine.ProcessAction(activePlayer, new EndTurn()).Accepted,
                $"multi-turn simulation turn {turn + 1} reaches reaction phase");
            check(CompleteByMarkingDone(engine).Accepted,
                $"multi-turn simulation turn {turn + 1} completes authoritative reaction hook");
            AssertCardConservation(engine, check, $"multi-turn simulation turn {turn + 1}");
        }

    }

    private static AlzheimerRoundEngine CreateEngine(
        int playerCount,
        int deckCount,
        int seed,
        string hostPlayerId = "P1",
        IGameClock? clock = null)
    {
        var players = Enumerable.Range(1, playerCount)
            .Select(index => new PlayerDefinition($"P{index}", $"Player {index}"))
            .ToArray();
        return AlzheimerRoundEngine.StartNewRound(
            players,
            hostPlayerId,
            new RoomSettings(10, deckCount),
            new Random(seed),
            CreateIdFactory(),
            clock);
    }

    private static AlzheimerRoundEngine CreateEngineWithNextDraw(CardRank rank)
    {
        for (var seed = 1; seed <= 500; seed++)
        {
            var engine = CreateEngine(2, 1, seed);
            CompletePeeks(engine, "P1", "P2");
            engine.ProcessAction("P1", new DrawCard());
            if (engine.GetPlayerView("P1").PendingDrawnCard!.Rank == rank)
                return engine;
        }

        throw new InvalidOperationException($"Could not find a deterministic draw for {rank}.");
    }

    private static void CompletePeeks(AlzheimerRoundEngine engine, params string[] playerIds)
    {
        foreach (var playerId in playerIds)
        {
            var result = engine.ProcessAction(playerId, new PeekInitialCards(0, 1));
            if (!result.Accepted)
                throw new InvalidOperationException($"Initial peek failed for {playerId}: {result.ErrorCode}.");
        }

    }

    private static TransitionResult CompleteByMarkingDone(AlzheimerRoundEngine engine)
    {
        var view = engine.GetPublicView();
        var windowId = view.ReactionWindowId
            ?? throw new InvalidOperationException("No reaction window is active.");
        TransitionResult? result = null;
        foreach (var player in view.Players)
        {
            result = engine.ProcessAction(
                player.PlayerId,
                new MarkReactionDone(windowId, Guid.NewGuid()));
            if (!result.Accepted)
                throw new InvalidOperationException($"Could not mark {player.PlayerId} done: {result.ErrorCode}.");
        }
        return result ?? throw new InvalidOperationException("No players participated in the reaction window.");
    }

    private static string Snapshot(AlzheimerRoundEngine engine, params string[] playerIds)
    {
        var projections = playerIds.Select(engine.GetPlayerView).ToArray();
        return JsonSerializer.Serialize(new { Public = engine.GetPublicView(), Players = projections });
    }

    private static void AssertCardConservation(
        AlzheimerRoundEngine engine,
        Action<bool, string> check,
        string label)
    {
        var view = engine.GetPublicView();
        var pendingCount = engine.GetPlayerView(view.ActivePlayerId).PendingDrawnCard is null ? 0 : 1;
        var visibleCardCount = view.DrawPileCount
            + view.DiscardPile.Count
            + view.Players.Sum(player => player.HandCount)
            + pendingCount;
        check(visibleCardCount == view.DeckCount * 54, $"{label}: public card conservation");

        var knownIds = view.Players
            .SelectMany(player => engine.GetPlayerView(player.PlayerId).OwnHand)
            .Where(slot => slot.KnownCard is not null)
            .Select(slot => slot.KnownCard!.PhysicalCardId)
            .Concat(view.DiscardPile.Select(card => card.PhysicalCardId))
            .Concat(engine.GetPlayerView(view.ActivePlayerId).PendingDrawnCard is { } pending
                ? new[] { pending.PhysicalCardId }
                : Array.Empty<Guid>())
            .ToArray();
        check(knownIds.Distinct().Count() == knownIds.Length, $"{label}: visible physical IDs remain unique");
    }

    private static Func<Guid> CreateIdFactory()
    {
        var next = 0;
        return () => new Guid(++next, 0, 0, new byte[8]);
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IGameClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }
}

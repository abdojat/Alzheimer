using Alzheimer.Core;
using System.Text.Json;

internal static class ReactionTests
{
    public static void Run(Action<bool, string> check)
    {
        TestMatchingChainsAndSerializedCommands(check);
        TestWrongThrowPenaltyAndPrivacy(check);
        TestUnknownCardReaction(check);
        TestDoneAndCommandValidation(check);
        TestReactionDeadline(check);
        TestQueenWindowAndFinalTop(check);
        TestPenaltyDepletionAndEmptyPilePenalty(check);
        TestAutomaticReactionCall(check);
    }

    private static void TestMatchingChainsAndSerializedCommands(Action<bool, string> check)
    {
        var engine = FindReactionWindow(candidate =>
        {
            var top = candidate.GetPublicView().DiscardPile[^1];
            var firstCard = candidate.GetPlayerView("P1").OwnHand[0].KnownCard;
            return firstCard?.Rank == top.Rank
                && firstCard.Suit != top.Suit
                && HasKnownRank(candidate, "P1", top.Rank, 1)
                && HasKnownRank(candidate, "P2", top.Rank, 0);
        }, deckCount: 3);
        var initial = engine.GetPublicView();
        var windowId = initial.ReactionWindowId!.Value;
        var deadline = initial.ReactionDeadlineUtc!.Value;
        var p1Before = engine.GetPlayerView("P1").OwnHand;
        var thirdId = engine.GetPlayerView("P2").OwnHand[0].KnownCard!.PhysicalCardId;
        var firstId = p1Before[0].KnownCard!.PhysicalCardId;
        var secondId = p1Before[1].KnownCard!.PhysicalCardId;
        var targetRank = initial.DiscardPile[^1].Rank;
        var activeSeat = initial.ActivePlayerId;

        var first = engine.ProcessAction("P1", new ThrowReactionCard(0, windowId, Id(101)));
        check(first.Accepted && first.Events.Single().Type == GameEventType.ReactionCardThrown,
            "a known card matching the current discard rank is thrown successfully");
        check(engine.GetPublicView().DiscardPile[^1].PhysicalCardId == firstId,
            "successful reaction becomes the public discard top");
        check(engine.GetPublicView().ReactionDeadlineUtc == deadline,
            "a successful throw does not restart the reaction timer");
        check(engine.GetPublicView().Phase == GamePhase.AwaitingReactionResolution,
            "throwing a Jack, Queen, or King during reactions does not activate an ability");

        var second = engine.ProcessAction("P1", new ThrowReactionCard(0, windowId, Id(102)));
        var third = engine.ProcessAction("P2", new ThrowReactionCard(0, windowId, Id(103)));
        check(second.Accepted && third.Accepted,
            "players can make multiple rank-matching throws in one reaction window");
        check(engine.GetPublicView().DiscardPile[^2].PhysicalCardId == secondId
            && engine.GetPublicView().DiscardPile[^1].PhysicalCardId == thirdId
            && engine.GetPublicView().DiscardPile[^1].Rank == targetRank,
            "serialized reaction submissions resolve in processing order");
        check(engine.GetPublicView().ActivePlayerId == activeSeat,
            "reaction throws preserve the active player's turn context");
        AssertConservation(engine, check, "ordinary reaction chain");

        var specialEngine = FindReactionWindow(candidate =>
        {
            var rank = candidate.GetPublicView().DiscardPile[^1].Rank;
            return rank is CardRank.Jack or CardRank.Queen or CardRank.King
                && HasKnownRank(candidate, "P1", rank, 0);
        });
        var specialWindowId = specialEngine.GetPublicView().ReactionWindowId!.Value;
        var specialThrow = specialEngine.ProcessAction(
            "P1",
            new ThrowReactionCard(0, specialWindowId, Id(104)));
        check(specialThrow.Accepted
            && specialEngine.GetPublicView().Phase == GamePhase.AwaitingReactionResolution,
            "a reaction-thrown Jack, Queen, or King is discarded without activating its ability");
    }

    private static void TestWrongThrowPenaltyAndPrivacy(Action<bool, string> check)
    {
        var engine = FindReactionWindow(candidate =>
        {
            var target = candidate.GetPublicView().DiscardPile[^1];
            var unknown = candidate.GetPlayerView("P1").OwnHand[2];
            return unknown.Knowledge == CardKnowledge.Unknown
                && candidate.GetPlayerView("P1").OwnHand[3].Knowledge == CardKnowledge.Unknown
                && HasKnownSuitMismatch(candidate, "P1", target);
        });
        var view = engine.GetPublicView();
        var windowId = view.ReactionWindowId!.Value;
        var ownerBefore = engine.GetPlayerView("P1").OwnHand;
        var selectedIndex = FindKnownSuitMismatch(engine, "P1", view.DiscardPile[^1]);
        var selectedId = ownerBefore[selectedIndex].KnownCard!.PhysicalCardId;
        var handCount = ownerBefore.Count;
        var drawCount = view.DrawPileCount;

        var result = engine.ProcessAction("P1", new ThrowReactionCard(selectedIndex, windowId, Id(201)));
        var ownerAfter = engine.GetPlayerView("P1");
        check(result.Accepted && result.Events.Single().Type == GameEventType.IncorrectReactionThrow,
            "a same-suit but different-rank attempt is rejected as a discard and penalized");
        check(ownerAfter.OwnHand.Count == handCount + 1
            && ownerAfter.OwnHand[selectedIndex].KnownCard?.PhysicalCardId == selectedId,
            "wrong card stays in its original hand position and becomes known to its owner");
        check(ownerAfter.OwnHand[^1].Knowledge == CardKnowledge.Unknown
            && ownerAfter.OwnHand[^1].KnownCard is null,
            "an immediate penalty draw is appended face down and unknown");
        check(engine.GetPublicView().DrawPileCount == drawCount - 1
            && engine.GetPublicView().DiscardPile[^1].PhysicalCardId != selectedId,
            "wrong throw consumes a penalty card without changing the discard pile");

        var publicJson = JsonSerializer.Serialize(engine.GetPublicView());
        var otherPlayerJson = JsonSerializer.Serialize(engine.GetPlayerView("P2"));
        var eventJson = JsonSerializer.Serialize(result.Events);
        check(!publicJson.Contains(selectedId.ToString(), StringComparison.Ordinal)
            && !otherPlayerJson.Contains(selectedId.ToString(), StringComparison.Ordinal)
            && !eventJson.Contains(selectedId.ToString(), StringComparison.Ordinal),
            "wrong-card identity is absent from public projections and events");
        check(JsonSerializer.Serialize(ownerAfter).Contains(selectedId.ToString(), StringComparison.Ordinal),
            "the wrong card's actual identity is privately revealed to its owner");
        AssertConservation(engine, check, "incorrect throw penalty");
    }

    private static void TestUnknownCardReaction(Action<bool, string> check)
    {
        AlzheimerRoundEngine? successfulEngine = null;
        for (var seed = 1; seed <= 2_000 && successfulEngine is null; seed++)
        {
            var candidate = CreateReactionWindow(seed);
            var windowId = candidate.GetPublicView().ReactionWindowId!.Value;
            var beforeCount = candidate.GetPlayerView("P1").OwnHand.Count;
            var result = candidate.ProcessAction("P1", new ThrowReactionCard(2, windowId, Id(300 + seed)));
            if (result.Accepted
                && candidate.GetPlayerView("P1").OwnHand.Count == beforeCount - 1)
                successfulEngine = candidate;
        }

        check(successfulEngine is not null,
            "a player can successfully guess a matching card whose identity is unknown to them");
        if (successfulEngine is null)
            return;

        var view = successfulEngine.GetPublicView();
        check(view.Phase == GamePhase.AwaitingReactionResolution
            && view.DiscardPile[^1].Rank == view.DiscardPile[^2].Rank,
            "unknown-card matching is validated against the server-owned rank");
        AssertConservation(successfulEngine, check, "unknown-card reaction");
    }

    private static void TestDoneAndCommandValidation(Action<bool, string> check)
    {
        var engine = CreateReactionWindow(61, playerCount: 3);
        var view = engine.GetPublicView();
        var windowId = view.ReactionWindowId!.Value;
        var before = Snapshot(engine, "P1", "P2", "P3");

        var unknownPlayer = engine.ProcessAction("P9", new MarkReactionDone(windowId, Id(401)));
        var badIndex = engine.ProcessAction("P1", new ThrowReactionCard(99, windowId, Id(402)));
        var staleWindow = engine.ProcessAction("P1", new MarkReactionDone(windowId - 1, Id(403)));
        var emptyCommandId = engine.ProcessAction("P1", new MarkReactionDone(windowId, Guid.Empty));
        check(!unknownPlayer.Accepted && unknownPlayer.ErrorCode == GameErrorCode.InvalidPlayer
            && !badIndex.Accepted && badIndex.ErrorCode == GameErrorCode.InvalidHandIndex
            && !staleWindow.Accepted && staleWindow.ErrorCode == GameErrorCode.StaleReactionWindow
            && !emptyCommandId.Accepted && emptyCommandId.ErrorCode == GameErrorCode.InvalidCommandId,
            "reaction commands reject unauthorized, stale, invalid-position, and missing-ID submissions");
        check(Snapshot(engine, "P1", "P2", "P3") == before,
            "invalid reaction commands leave gameplay state unchanged");

        var doneId = Id(404);
        var done = engine.ProcessAction("P1", new MarkReactionDone(windowId, doneId));
        var duplicate = engine.ProcessAction("P1", new MarkReactionDone(windowId, doneId));
        var resumed = engine.ProcessAction("P1", new ThrowReactionCard(0, windowId, Id(405)));
        check(done.Accepted && !duplicate.Accepted && duplicate.ErrorCode == GameErrorCode.DuplicateCommand,
            "accepted command IDs cannot be replayed");
        check(!resumed.Accepted && resumed.ErrorCode == GameErrorCode.ReactionAlreadyDone,
            "Done is irreversible for the remainder of the reaction window");
        check(engine.GetPublicView().ReactionDonePlayerIds.SequenceEqual(new[] { "P1" }),
            "Done is visible as public per-window participation state");

        engine.ProcessAction("P2", new MarkReactionDone(windowId, Id(406)));
        var allDone = engine.ProcessAction("P3", new MarkReactionDone(windowId, Id(407)));
        check(allDone.Accepted && allDone.Events.Any(gameEvent =>
            gameEvent.Type == GameEventType.ReactionWindowCompleted),
            "all players marking Done closes the window immediately");
        check(engine.GetPublicView().Phase == GamePhase.AwaitingDraw
            && engine.GetPublicView().ReactionDeadlineUtc is null
            && engine.GetPublicView().ActivePlayerId == "P2",
            "early completion clears the deadline and advances clockwise");

        engine.ProcessAction("P2", new DrawCard());
        engine.ProcessAction("P2", new DiscardDrawnCard());
        engine.ProcessAction("P2", new EndTurn());
        var replayFromPreviousWindow = engine.ProcessAction(
            "P1",
            new MarkReactionDone(windowId, Id(408)));
        check(!replayFromPreviousWindow.Accepted
            && replayFromPreviousWindow.ErrorCode == GameErrorCode.StaleReactionWindow,
            "a command from a completed window cannot be replayed in the next window");
    }

    private static void TestReactionDeadline(Action<bool, string> check)
    {
        var start = new DateTimeOffset(2026, 11, 25, 2, 0, 0, TimeSpan.FromHours(3));
        var beforeDeadlineClock = new FixedClock(start);
        var beforeDeadline = CreateReactionWindow(76, clock: beforeDeadlineClock);
        var window = beforeDeadline.GetPublicView();
        var windowId = window.ReactionWindowId!.Value;
        var deadline = window.ReactionDeadlineUtc!.Value;
        check(deadline == start.ToUniversalTime().AddSeconds(30),
            "reaction deadline is exactly 30 seconds in authoritative UTC");
        check(beforeDeadline.CompleteReactionWindow().ErrorCode == GameErrorCode.ReactionDeadlineNotReached,
            "the reaction window cannot complete before its deadline unless everyone is Done");
        beforeDeadlineClock.Set(deadline.AddTicks(-1));
        var lastMoment = beforeDeadline.ProcessAction("P1", new MarkReactionDone(windowId, Id(501)));
        check(lastMoment.Accepted && beforeDeadline.GetPublicView().Phase == GamePhase.AwaitingReactionResolution,
            "reaction commands remain valid one tick before the deadline");

        var exactClock = new FixedClock(start);
        var exactDeadline = CreateReactionWindow(76, clock: exactClock);
        var exactWindowId = exactDeadline.GetPublicView().ReactionWindowId!.Value;
        var exactDeadlineUtc = exactDeadline.GetPublicView().ReactionDeadlineUtc!.Value;
        exactClock.Set(exactDeadlineUtc);
        var lateAction = exactDeadline.ProcessAction(
            "P1",
            new MarkReactionDone(exactWindowId, Id(502)));
        check(!lateAction.Accepted && lateAction.ErrorCode == GameErrorCode.ReactionWindowExpired,
            "an action at the exact 30-second boundary is rejected after expiry");
        check(lateAction.Events.Any(gameEvent => gameEvent.Type == GameEventType.ReactionWindowCompleted)
            && exactDeadline.GetPublicView().ActivePlayerId == "P2",
            "the authoritative exact-deadline transition closes and advances the window");
        check(exactDeadline.ProcessTimeouts().ErrorCode == GameErrorCode.AbilityDeadlineNotReached,
            "an expired reaction deadline is processed only once");
    }

    private static void TestQueenWindowAndFinalTop(Action<bool, string> check)
    {
        var engine = FindQueenReactionWindow();
        var queenTargets = engine.GetPublicView().QueenReactionTargets!;
        var queenWindowId = engine.GetPublicView().ReactionWindowId!.Value;
        var p2QueenIndex = FindKnownRank(engine, "P2", CardRank.Queen);
        var p3SelectedIndex = FindKnownRank(engine, "P3", queenTargets.SelectedCardRank);
        var finalTopId = queenTargets.FinalTopCard.PhysicalCardId;

        var queenThrow = engine.ProcessAction(
            "P2",
            new ThrowReactionCard(p2QueenIndex, queenWindowId, Id(601)));
        check(queenThrow.Accepted && engine.GetPublicView().QueenReactionTargets == queenTargets,
            "Queen rank remains valid after a successful throw and both exception ranks persist");

        var shiftedSelectedIndex = p3SelectedIndex;
        var selectedThrow = engine.ProcessAction(
            "P3",
            new ThrowReactionCard(shiftedSelectedIndex, queenWindowId, Id(602)));
        check(selectedThrow.Accepted
            && engine.GetPublicView().QueenReactionTargets == queenTargets,
            "the Queen-discarded rank remains valid after other throws change the ordinary pile top");

        foreach (var playerId in new[] { "P1", "P2", "P3" })
        {
            if (!engine.GetPublicView().ReactionDonePlayerIds.Contains(playerId))
                engine.ProcessAction(playerId, new MarkReactionDone(queenWindowId, Id(610 + playerId[1])));
        }
        check(engine.GetPublicView().DiscardPile[^1].PhysicalCardId == finalTopId,
            "the original Queen-discarded card is restored as the final discard top");
        AssertConservation(engine, check, "Queen reaction window");
    }

    private static void TestPenaltyDepletionAndEmptyPilePenalty(Action<bool, string> check)
    {
        var depletedByPenalty = FindWindowWithOneDrawCard();
        var depletedWindow = depletedByPenalty.GetPublicView();
        var windowId = depletedWindow.ReactionWindowId!.Value;
        var mismatchIndex = FindKnownMismatch(depletedByPenalty, "P1", depletedWindow.DiscardPile[^1].Rank);
        var penalty = depletedByPenalty.ProcessAction(
            "P1",
            new ThrowReactionCard(mismatchIndex, windowId, Id(701)));
        check(penalty.Accepted && depletedByPenalty.GetPublicView().DrawPileCount == 0,
            "an incorrect throw can draw the final available penalty card");
        check(depletedByPenalty.GetPublicView().Phase == GamePhase.AwaitingReactionResolution,
            "penalty-card depletion does not cancel the ongoing reaction window");
        check(depletedByPenalty.ProcessAction("P2", new MarkReactionDone(windowId, Id(702))).Accepted
            && depletedByPenalty.GetPublicView().Phase == GamePhase.AwaitingReactionResolution,
            "a depleted-draw-pile reaction window still accepts Done submissions");

        var emptyPileEngine = FindWindowWithEmptyDrawPileAndMismatch(out var emptyMismatchIndex);
        var emptyView = emptyPileEngine.GetPublicView();
        var emptyWindowId = emptyView.ReactionWindowId!.Value;
        var emptyPenalty = emptyPileEngine.ProcessAction(
            "P1",
            new ThrowReactionCard(emptyMismatchIndex, emptyWindowId, Id(703)));
        check(emptyPenalty.Accepted && emptyView.DrawPileCount == 0
            && emptyPileEngine.GetPublicView().Players.Single(player => player.PlayerId == "P1").RoundPenaltyPoints == 10,
            "an incorrect throw with an empty draw pile records the +10 round penalty");
        check(emptyPileEngine.GetPublicView().Phase == GamePhase.AwaitingReactionResolution,
            "the +10 penalty leaves the reaction window active");
        AssertConservation(emptyPileEngine, check, "empty-pile reaction penalty");
    }

    private static void TestAutomaticReactionCall(Action<bool, string> check)
    {
        var engine = CreateOneCardCallerReactionScenario();
        var publicBefore = engine.GetPublicView();
        var p1Card = engine.GetPlayerView("P1").OwnHand[0].KnownCard
            ?? throw new InvalidOperationException("The caller's final card should be known.");
        var activePlayer = publicBefore.ActivePlayerId;
        var result = engine.ProcessAction(
            "P1",
            new ThrowReactionCard(0, publicBefore.ReactionWindowId!.Value, Id(801)));
        var view = engine.GetPublicView();

        check(result.Accepted && view.CallerId == "P1" && view.IsFinalTurnMode
            && view.Phase == GamePhase.FinalPhase && view.ActivePlayerId == "P2",
            "a successful empty-hand reaction automatically registers the sole caller");
        check(view.ActivePlayerId == activePlayer,
            "automatic calls during another player's reaction preserve the active-player context");
        check(view.RequiredFinalTurnPlayerIds.SequenceEqual(new[] { activePlayer })
            && view.PendingFinalTurnPlayerIds.SequenceEqual(new[] { activePlayer }),
            "the active player's in-progress turn is excluded and scheduled as their subsequent final turn");
        check(view.DiscardPile[^1].PhysicalCardId == p1Card.PhysicalCardId,
            "the successful final throw is retained face up");
        var staleThrow = engine.ProcessAction(
            activePlayer,
            new ThrowReactionCard(0, publicBefore.ReactionWindowId.Value, Id(802)));
        check(!staleThrow.Accepted && staleThrow.ErrorCode == GameErrorCode.InvalidPhase
            && engine.GetPublicView().CallerId == "P1",
            "reaction throwing stops immediately and a second caller cannot be recorded");
        AssertConservation(engine, check, "automatic reaction call");
    }

    private static AlzheimerRoundEngine FindQueenReactionWindow()
    {
        for (var seed = 1; seed <= 10_000; seed++)
        {
            var engine = CreateEngine(3, 1, seed);
            CompletePeeks(engine, "P1", "P2", "P3");
            engine.ProcessAction("P1", new DrawCard());
            if (engine.GetPlayerView("P1").PendingDrawnCard?.Rank != CardRank.Queen)
                continue;

            var selected = engine.GetPlayerView("P1").OwnHand[0].KnownCard!;
            if (selected.Rank == CardRank.Queen
                || FindKnownRank(engine, "P2", CardRank.Queen, allowMissing: true) < 0
                || FindKnownRank(engine, "P3", selected.Rank, allowMissing: true) < 0)
                continue;

            engine.ProcessAction("P1", new ActivateDrawnAbility());
            if (!engine.ProcessAction("P1", new ResolveQueenAbility(0)).Accepted)
                continue;
            engine.ProcessAction("P1", new EndTurn());
            return engine;
        }

        throw new InvalidOperationException("Could not find a deterministic Queen dual-rank reaction scenario.");
    }

    private static AlzheimerRoundEngine FindWindowWithOneDrawCard()
    {
        for (var seed = 1; seed <= 500; seed++)
        {
            var engine = CreateEngine(2, 1, seed);
            CompletePeeks(engine, "P1", "P2");
            var turnCount = 0;
            while (engine.GetPublicView().DrawPileCount > 2)
            {
                var active = engine.GetPublicView().ActivePlayerId;
                engine.ProcessAction(active, new DrawCard());
                engine.ProcessAction(active, new DiscardDrawnCard());
                engine.ProcessAction(active, new EndTurn());
                CompleteByMarkingDone(engine);
                turnCount++;
            }

            var activePlayer = engine.GetPublicView().ActivePlayerId;
            engine.ProcessAction(activePlayer, new DrawCard());
            engine.ProcessAction(activePlayer, new DiscardDrawnCard());
            engine.ProcessAction(activePlayer, new EndTurn());
            if (engine.GetPublicView().DrawPileCount == 1
                && FindKnownMismatch(engine, "P1", engine.GetPublicView().DiscardPile[^1].Rank) >= 0)
                return engine;

            if (turnCount > 100)
                break;
        }

        throw new InvalidOperationException("Could not prepare a reaction window with one penalty card left.");
    }

    private static AlzheimerRoundEngine FindWindowWithEmptyDrawPileAndMismatch(out int handIndex)
    {
        for (var seed = 1; seed <= 1_000; seed++)
        {
            var engine = CreateEngine(2, 1, seed);
            CompletePeeks(engine, "P1", "P2");
            while (engine.GetPublicView().DrawPileCount > 0)
            {
                var active = engine.GetPublicView().ActivePlayerId;
                engine.ProcessAction(active, new DrawCard());
                engine.ProcessAction(active, new DiscardDrawnCard());
                engine.ProcessAction(active, new EndTurn());
                if (engine.GetPublicView().DrawPileCount == 0)
                {
                    handIndex = FindKnownMismatch(engine, "P1", engine.GetPublicView().DiscardPile[^1].Rank);
                    if (handIndex >= 0)
                        return engine;
                }
                CompleteByMarkingDone(engine);
            }
        }

        handIndex = -1;
        throw new InvalidOperationException("Could not prepare an empty-pile reaction mismatch.");
    }

    private static AlzheimerRoundEngine CreateOneCardCallerReactionScenario()
    {
        for (var seed = 1; seed <= 500; seed++)
        {
            var engine = CreateEngine(2, 2, seed);
            CompletePeeks(engine, "P1", "P2");
            var queenUses = 0;
            while (queenUses < 3
                && engine.GetPublicView().Phase == GamePhase.AwaitingDraw
                && engine.GetPublicView().DrawPileCount > 0)
            {
                var active = engine.GetPublicView().ActivePlayerId;
                engine.ProcessAction(active, new DrawCard());
                if (active == "P1"
                    && engine.GetPlayerView("P1").PendingDrawnCard?.Rank == CardRank.Queen
                    && engine.GetPlayerView("P1").OwnHand.Count > 1)
                {
                    engine.ProcessAction(active, new ActivateDrawnAbility());
                    engine.ProcessAction(active,
                        new ResolveQueenAbility(engine.GetPlayerView("P1").OwnHand.Count - 1));
                    queenUses++;
                }
                else
                {
                    engine.ProcessAction(active, new DiscardDrawnCard());
                }

                engine.ProcessAction(active, new EndTurn());
                CompleteByMarkingDone(engine);
            }

            if (queenUses != 3
                || engine.GetPublicView().Players.Single(player => player.PlayerId == "P1").HandCount != 1
                || engine.GetPublicView().ActivePlayerId != "P2")
                continue;

            var callerCard = engine.GetPlayerView("P1").OwnHand[0].KnownCard!;
            for (var attempt = 0; attempt < 40 && engine.GetPublicView().CallerId is null; attempt++)
            {
                if (engine.GetPublicView().Phase != GamePhase.AwaitingDraw
                    || engine.GetPublicView().DrawPileCount == 0)
                    break;
                var active = engine.GetPublicView().ActivePlayerId;
                if (!engine.ProcessAction(active, new DrawCard()).Accepted)
                    break;
                if (active != "P2" || engine.GetPlayerView("P2").PendingDrawnCard!.Rank != callerCard.Rank)
                {
                    engine.ProcessAction(active, new DiscardDrawnCard());
                    engine.ProcessAction(active, new EndTurn());
                    CompleteByMarkingDone(engine);
                    continue;
                }

                engine.ProcessAction("P2", new DiscardDrawnCard());
                engine.ProcessAction("P2", new EndTurn());
                if (engine.GetPublicView().Phase == GamePhase.AwaitingReactionResolution)
                    return engine;
                break;
            }
        }

        throw new InvalidOperationException("Could not prepare a reaction-phase empty-hand call.");
    }

    private static AlzheimerRoundEngine FindReactionWindow(
        Func<AlzheimerRoundEngine, bool> predicate,
        int playerCount = 2,
        int deckCount = 1,
        int maxSeed = 10_000)
    {
        for (var seed = 1; seed <= maxSeed; seed++)
        {
            var engine = CreateReactionWindow(seed, playerCount, deckCount: deckCount);
            if (predicate(engine))
                return engine;
        }

        throw new InvalidOperationException("Could not find a deterministic matching reaction scenario.");
    }

    private static AlzheimerRoundEngine CreateReactionWindow(
        int seed,
        int playerCount = 2,
        IGameClock? clock = null,
        int deckCount = 1)
    {
        var engine = CreateEngine(playerCount, deckCount, seed, clock);
        CompletePeeks(engine, Enumerable.Range(1, playerCount).Select(index => $"P{index}").ToArray());
        engine.ProcessAction("P1", new DrawCard());
        engine.ProcessAction("P1", new DiscardDrawnCard());
        engine.ProcessAction("P1", new EndTurn());
        return engine;
    }

    private static bool HasKnownRank(
        AlzheimerRoundEngine engine,
        string playerId,
        CardRank rank,
        int handIndex) =>
        engine.GetPlayerView(playerId).OwnHand[handIndex].KnownCard?.Rank == rank;

    private static bool HasKnownSuitMismatch(
        AlzheimerRoundEngine engine,
        string playerId,
        CardView target) =>
        FindKnownSuitMismatch(engine, playerId, target) >= 0;

    private static int FindKnownSuitMismatch(AlzheimerRoundEngine engine, string playerId, CardView target)
    {
        var hand = engine.GetPlayerView(playerId).OwnHand;
        return hand.Select((slot, index) => (slot, index))
            .Where(item => item.slot.KnownCard is { } card
                && card.Suit == target.Suit
                && card.Rank != target.Rank)
            .Select(item => item.index)
            .DefaultIfEmpty(-1)
            .First();
    }

    private static int FindKnownMismatch(AlzheimerRoundEngine engine, string playerId, CardRank targetRank)
    {
        var hand = engine.GetPlayerView(playerId).OwnHand;
        return hand.Select((slot, index) => (slot, index))
            .Where(item => item.slot.KnownCard is { } card && card.Rank != targetRank)
            .Select(item => item.index)
            .DefaultIfEmpty(-1)
            .First();
    }

    private static int FindKnownRank(
        AlzheimerRoundEngine engine,
        string playerId,
        CardRank rank,
        bool allowMissing = false)
    {
        var index = engine.GetPlayerView(playerId).OwnHand
            .Select((slot, handIndex) => (slot, handIndex))
            .Where(item => item.slot.KnownCard?.Rank == rank)
            .Select(item => item.handIndex)
            .DefaultIfEmpty(-1)
            .First();
        if (index < 0 && !allowMissing)
            throw new InvalidOperationException($"No known {rank} card exists for {playerId}.");
        return index;
    }

    private static void CompletePeeks(AlzheimerRoundEngine engine, params string[] playerIds)
    {
        foreach (var playerId in playerIds)
            engine.ProcessAction(playerId, new PeekInitialCards(0, 1));
    }

    private static TransitionResult CompleteByMarkingDone(AlzheimerRoundEngine engine)
    {
        var view = engine.GetPublicView();
        var windowId = view.ReactionWindowId
            ?? throw new InvalidOperationException("No reaction window is active.");
        TransitionResult? result = null;
        foreach (var player in view.Players)
        {
            result = engine.ProcessAction(player.PlayerId, new MarkReactionDone(windowId, Guid.NewGuid()));
            if (!result.Accepted)
                throw new InvalidOperationException($"Could not mark {player.PlayerId} Done: {result.ErrorCode}.");
        }
        return result!;
    }

    private static AlzheimerRoundEngine CreateEngine(
        int playerCount,
        int deckCount,
        int seed,
        IGameClock? clock = null)
    {
        var players = Enumerable.Range(1, playerCount)
            .Select(index => new PlayerDefinition($"P{index}", $"Player {index}"))
            .ToArray();
        return AlzheimerRoundEngine.StartNewRound(
            players,
            "P1",
            new RoomSettings(10, deckCount),
            new Random(seed),
            CreateIdFactory(),
            clock);
    }

    private static string Snapshot(AlzheimerRoundEngine engine, params string[] playerIds) =>
        JsonSerializer.Serialize(new
        {
            Public = engine.GetPublicView(),
            Players = playerIds.Select(engine.GetPlayerView).ToArray()
        });

    private static void AssertConservation(AlzheimerRoundEngine engine, Action<bool, string> check, string label)
    {
        var view = engine.GetPublicView();
        var pendingCount = engine.GetPlayerView(view.ActivePlayerId).PendingDrawnCard is null ? 0 : 1;
        var total = view.DrawPileCount + view.DiscardPile.Count
            + view.Players.Sum(player => player.HandCount) + pendingCount;
        check(total == view.DeckCount * 54, $"{label}: all physical cards remain accounted for");

        var visibleIds = view.DiscardPile.Select(card => card.PhysicalCardId)
            .Concat(view.Players.SelectMany(player => engine.GetPlayerView(player.PlayerId).OwnHand)
                .Where(slot => slot.KnownCard is not null)
                .Select(slot => slot.KnownCard!.PhysicalCardId))
            .Distinct()
            .Count();
        var visibleCardCount = view.DiscardPile.Count
            + view.Players.SelectMany(player => engine.GetPlayerView(player.PlayerId).OwnHand)
                .Count(slot => slot.KnownCard is not null);
        check(visibleIds == visibleCardCount, $"{label}: every visible physical card ID is unique");
    }

    private static Func<Guid> CreateIdFactory()
    {
        var next = 0;
        return () => new Guid(++next, 0, 0, new byte[8]);
    }

    private static Guid Id(int value) => new(value, 0, 0, new byte[8]);

    private sealed class FixedClock(DateTimeOffset utcNow) : IGameClock
    {
        public DateTimeOffset UtcNow { get; private set; } = utcNow;

        public void Set(DateTimeOffset value) => UtcNow = value;
    }
}

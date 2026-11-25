using Alzheimer.Core;
using System.Text.Json;

internal static class FinalScoringTests
{
    public static void Run(Action<bool, string> check)
    {
        TestVoluntaryFinalTurnsForEveryPlayerCount(check);
        TestAutomaticReactionCallOrder(check);
        TestAbilitiesDuringFinalTurns(check);
        TestLastCardCallAndDepletion(check);
        TestPenaltyExhaustionScoring(check);
        TestScoringVariants(check);
        TestDeterministicMatchAndRoundReset(check);
    }

    private static void TestVoluntaryFinalTurnsForEveryPlayerCount(Action<bool, string> check)
    {
        for (var playerCount = 2; playerCount <= 10; playerCount++)
        {
            var engine = CreateEngine(playerCount, 1, 900 + playerCount);
            var playerIds = PlayerIds(playerCount);
            CompletePeeks(engine, playerIds);
            var oldHandIds = CollectHandIds(engine, playerIds);

            check(engine.ProcessAction("P1", new DrawCard()).Accepted,
                $"{playerCount}-player voluntary call scenario draws");
            check(engine.ProcessAction("P1", new DiscardDrawnCard()).Accepted,
                $"{playerCount}-player voluntary call scenario resolves its draw");
            var call = engine.ProcessAction("P1", new CallAlzheimer());
            var called = engine.GetPublicView();
            var expected = playerIds.Skip(1).ToArray();
            check(call.Accepted && called.CallerId == "P1" && called.IsFinalTurnMode,
                $"{playerCount}-player voluntary call starts final-turn mode");
            check(called.RequiredFinalTurnPlayerIds.SequenceEqual(expected)
                && called.PendingFinalTurnPlayerIds.SequenceEqual(expected)
                && called.ActivePlayerId == expected[0],
                $"{playerCount}-player call schedules every other player clockwise and skips caller");
            check(!JsonSerializer.Serialize(called).Contains(oldHandIds.First().ToString(), StringComparison.Ordinal),
                "public state does not expose hand identities before round completion");

            var playedTurns = new List<string>();
            foreach (var playerId in expected)
            {
                var activeView = engine.GetPlayerView(playerId);
                check(activeView.CanCallAlzheimer == false && activeView.CanEndTurn == false,
                    $"{playerId} must draw before completing a final turn");
                check(engine.ProcessAction(playerId, new DrawCard()).Accepted,
                    $"{playerId} takes its one required final draw");
                check(engine.ProcessAction(playerId, new DiscardDrawnCard()).Accepted,
                    $"{playerId} resolves its final drawn card");
                activeView = engine.GetPlayerView(playerId);
                check(!activeView.CanCallAlzheimer && activeView.CanEndTurn,
                    $"{playerId} cannot make a second call after final-turn resolution");
                check(engine.ProcessAction(playerId, new CallAlzheimer()).ErrorCode == GameErrorCode.CallerAlreadyRecorded,
                    $"{playerId} cannot replace the caller during the final phase");
                check(engine.ProcessAction(playerId, new EndTurn()).Accepted,
                    $"{playerId} completes its final turn without a reaction window");
                playedTurns.Add(playerId);

                var current = engine.GetPublicView();
                check(current.Phase != GamePhase.AwaitingReactionResolution,
                    $"{playerId}'s final turn does not open reactions");
                if (playedTurns.Count < expected.Length)
                {
                    check(current.ActivePlayerId == expected[playedTurns.Count]
                        && current.PendingFinalTurnPlayerIds.SequenceEqual(expected.Skip(playedTurns.Count)),
                        $"{playerId}'s final turn advances to the next required seat exactly once");
                }
            }

            var afterRound = engine.GetPublicView();
            var result = afterRound.LastCompletedRound;
            check(playedTurns.SequenceEqual(expected),
                $"{playerCount}-player voluntary call gives each non-caller exactly one turn");
            check(result is not null
                && result.Reason == RoundCompletionReason.FinalTurnsCompleted
                && result.CallerId == "P1"
                && result.Scores.Count == playerCount
                && result.RequiredFinalTurnPlayerIds.SequenceEqual(expected)
                && result.PendingFinalTurnPlayerIds.Count == 0,
                $"{playerCount}-player final turns complete and publish structured round scores");

            if (!afterRound.MatchCompleted)
            {
                check(afterRound.RoundNumber == 2 && afterRound.Phase == GamePhase.WaitingForInitialPeeks
                    && afterRound.ActivePlayerId == "P1",
                    $"{playerCount}-player next round resets to the host and waits for peeks");
                check(afterRound.DrawPileCount == 54 - playerCount * 4
                    && afterRound.Players.All(player => player.HandCount == 4),
                    $"{playerCount}-player new round deals a fresh deck");
                check(playerIds.All(playerId =>
                    engine.GetPlayerView(playerId).OwnHand.All(slot => slot.Knowledge == CardKnowledge.Unknown)),
                    $"{playerCount}-player new round resets private knowledge");
                CompletePeeks(engine, playerIds);
                var newHandIds = CollectHandIds(engine, playerIds);
                check(!oldHandIds.Intersect(newHandIds).Any(),
                    $"{playerCount}-player new round uses newly identified physical cards");
            }
        }
    }

    private static void TestAutomaticReactionCallOrder(Action<bool, string> check)
    {
        var engine = CreateFourPlayerReactionCallerScenario();
        var window = engine.GetPublicView();
        var callerCard = engine.GetPlayerView("P2").OwnHand[0].KnownCard
            ?? throw new InvalidOperationException("The reaction caller's final card should be known.");
        var call = engine.ProcessAction(
            "P2",
            new ThrowReactionCard(0, window.ReactionWindowId!.Value, Id(1501)));
        var view = engine.GetPublicView();

        check(call.Accepted && view.CallerId == "P2" && view.ActivePlayerId == "P3",
            "empty hand during A's reaction records B and starts the next eligible final turn");
        check(view.RequiredFinalTurnPlayerIds.SequenceEqual(new[] { "P3", "P4", "P1" })
            && view.PendingFinalTurnPlayerIds.SequenceEqual(new[] { "P3", "P4", "P1" }),
            "empty-hand reaction call orders subsequent turns C, D, A");
        check(view.DiscardPile[^1].PhysicalCardId == callerCard.PhysicalCardId
            && view.ReactionWindowId is null,
            "successful empty-hand throw is retained and closes reactions immediately");

        var activeAlreadyCounted = engine.ProcessAction("P1", new DrawCard());
        check(!activeAlreadyCounted.Accepted && activeAlreadyCounted.ErrorCode == GameErrorCode.NotActivePlayer,
            "A's reaction-time turn does not count as A's subsequent final turn");
        foreach (var playerId in new[] { "P3", "P4", "P1" })
        {
            check(engine.ProcessAction(playerId, new DrawCard()).Accepted,
                $"{playerId} draws during automatic-call final turn");
            check(engine.ProcessAction(playerId, new DiscardDrawnCard()).Accepted,
                $"{playerId} resolves automatic-call final draw");
            check(engine.ProcessAction(playerId, new EndTurn()).Accepted,
                $"{playerId} advances automatic-call final-turn sequence");
        }
        var completed = engine.GetPublicView();
        var completedRound = completed.LastCompletedRound;
        check(completedRound is not null
            && completedRound.CallerId == "P2"
            && completedRound.Reason == RoundCompletionReason.FinalTurnsCompleted
            && completedRound.RequiredFinalTurnPlayerIds.SequenceEqual(new[] { "P3", "P4", "P1" })
            && completedRound.PendingFinalTurnPlayerIds.Count == 0,
            "automatic-call sequence scores after C, D, and A finish");
        AssertCardConservation(engine, check, "four-player automatic caller");
    }

    private static void TestAbilitiesDuringFinalTurns(Action<bool, string> check)
    {
        foreach (var rank in new[] { CardRank.Jack, CardRank.Queen, CardRank.King })
        {
            var engine = CreateEngineWithFinalDraw(rank, out var clock);
            check(engine.GetPublicView().IsFinalTurnMode
                && engine.GetPublicView().ActivePlayerId == "P2",
                $"final-turn {rank} scenario begins with the next player");
            check(engine.GetPlayerView("P2").PendingDrawnCard?.Rank == rank,
                $"final-turn player can draw {rank}");
            check(engine.ProcessAction("P2", new ActivateDrawnAbility()).Accepted
                && engine.GetPublicView().Phase == GamePhase.AwaitingAbilityResolution,
                $"final-turn {rank} opens the regular ability window");

            GameAction abilityResolution = rank switch
            {
                CardRank.Jack => new ResolveJackAbility(0),
                CardRank.Queen => new ResolveQueenAbility(0),
                CardRank.King => new ResolveKingAbility("P1", 0, 0),
                _ => throw new InvalidOperationException()
            };
            check(engine.ProcessAction("P2", abilityResolution).Accepted,
                $"final-turn {rank} ability resolves normally");
            check(!engine.GetPlayerView("P2").CanCallAlzheimer
                && engine.GetPlayerView("P2").CanEndTurn,
                $"final-turn {rank} offers end-turn but no second-call choice");
            check(engine.ProcessAction("P2", new EndTurn()).Accepted
                && engine.GetPublicView().Phase != GamePhase.AwaitingReactionResolution,
                $"final-turn {rank} ends without opening reactions");

            if (rank == CardRank.Jack)
            {
                var timeoutEngine = CreateEngineWithFinalDraw(rank, out clock);
                timeoutEngine.ProcessAction("P2", new ActivateDrawnAbility());
                clock.Set(timeoutEngine.GetPlayerView("P2").AbilityDeadlineUtc!.Value);
                var timeout = timeoutEngine.ProcessTimeouts();
                check(timeout.Accepted && timeout.Events.Any(gameEvent => gameEvent.Type == GameEventType.AbilityExpired),
                    "final-turn ability timeout still discards the card and advances to end-turn decision");
                check(timeoutEngine.ProcessAction("P2", new EndTurn()).Accepted
                    && timeoutEngine.GetPublicView().Phase != GamePhase.AwaitingReactionResolution,
                    "timed-out final ability does not open a reaction window");
            }
        }
    }

    private static void TestLastCardCallAndDepletion(Action<bool, string> check)
    {
        var called = CreateOneDrawRemainingEngine(1701);
        var caller = called.GetPublicView().ActivePlayerId;
        check(called.GetPublicView().DrawPileCount == 1, "voluntary last-card scenario has one draw remaining");
        called.ProcessAction(caller, new DrawCard());
        called.ProcessAction(caller, new DiscardDrawnCard());
        check(called.GetPlayerView(caller).CanCallAlzheimer,
            "last normal draw resolves before the voluntary call choice");
        var call = called.ProcessAction(caller, new CallAlzheimer());
        check(call.Accepted && called.GetPublicView().LastCompletedRound?.CallerId == caller
            && called.GetPublicView().LastCompletedRound?.Reason == RoundCompletionReason.DrawPileExhausted,
            "calling after the last draw uses caller-specific scoring and ends without final turns");

        var depleted = CreateOneDrawRemainingEngine(1702);
        var active = depleted.GetPublicView().ActivePlayerId;
        depleted.ProcessAction(active, new DrawCard());
        depleted.ProcessAction(active, new DiscardDrawnCard());
        check(depleted.ProcessAction(active, new EndTurn()).Accepted
            && depleted.GetPublicView().Phase == GamePhase.AwaitingReactionResolution,
            "last-card End Turn opens its required reaction window");
        var oldWindowId = depleted.GetPublicView().ReactionWindowId!.Value;
        CompleteByMarkingDone(depleted);
        var roundResult = depleted.GetPublicView().LastCompletedRound
            ?? throw new InvalidOperationException("The exhausted round should expose a completion result.");
        check(roundResult.CallerId is null
            && roundResult.Reason == RoundCompletionReason.DrawPileExhausted
            && roundResult.Scores.All(score => score.AdjustedRoundScore == score.BaseScore * 2),
            "ordinary last-card depletion doubles every base score only after reactions close");
        var savedCumulativeScores = roundResult.Scores.ToDictionary(score => score.PlayerId, score => score.CumulativeScore);
        var duplicateCompletion = depleted.CompleteReactionWindow();
        depleted.ProcessTimeouts();
        check(!duplicateCompletion.Accepted
            && roundResult.Scores.All(score =>
                depleted.GetPublicView().Players.Single(player => player.PlayerId == score.PlayerId).CumulativeScore
                    == savedCumulativeScores[score.PlayerId]),
            "duplicate reaction completion and timeout processing cannot apply round scores twice");

        var next = depleted.GetPublicView();
        if (!next.MatchCompleted)
        {
            CompletePeeks(depleted, "P1", "P2");
            depleted.ProcessAction("P1", new DrawCard());
            depleted.ProcessAction("P1", new DiscardDrawnCard());
            depleted.ProcessAction("P1", new EndTurn());
            var newWindowId = depleted.GetPublicView().ReactionWindowId!.Value;
            check(newWindowId != oldWindowId,
                "reaction-window IDs remain unique across round reset");
            var replay = depleted.ProcessAction(
                "P1",
                new MarkReactionDone(oldWindowId, Id(1703)));
            check(!replay.Accepted && replay.ErrorCode == GameErrorCode.StaleReactionWindow,
                "a reaction command from the previous round cannot affect the new round");
        }
    }

    private static void TestPenaltyExhaustionScoring(Action<bool, string> check)
    {
        var (finalPenaltyEngine, finalPenaltyIndex) = FindFinalPenaltyCardScenario();
        var finalPenaltyWindow = finalPenaltyEngine.GetPublicView().ReactionWindowId!.Value;
        var lastPenalty = finalPenaltyEngine.ProcessAction(
            "P1",
            new ThrowReactionCard(finalPenaltyIndex, finalPenaltyWindow, Id(1799)));
        check(lastPenalty.Accepted && finalPenaltyEngine.GetPublicView().DrawPileCount == 0
            && finalPenaltyEngine.GetPublicView().Phase == GamePhase.AwaitingReactionResolution,
            "taking the final penalty card does not close its current reaction window");
        var afterDepletionPenalty = finalPenaltyEngine.ProcessAction(
            "P1",
            new ThrowReactionCard(finalPenaltyIndex, finalPenaltyWindow, Id(1800)));
        var afterPenaltyView = finalPenaltyEngine.GetPublicView();
        check(afterDepletionPenalty.Accepted
            && afterPenaltyView.Phase == GamePhase.AwaitingReactionResolution
            && afterPenaltyView.Players.Single(player => player.PlayerId == "P1").RoundPenaltyPoints == 10,
            "a further invalid throw after penalty depletion adds +10 while reactions remain open");
        CompleteByMarkingDone(finalPenaltyEngine);
        var lastPenaltyResult = finalPenaltyEngine.GetPublicView().LastCompletedRound
            ?? throw new InvalidOperationException("The reaction window should close after all Done submissions.");
        var lastPenaltyScore = lastPenaltyResult.Scores.Single(score => score.PlayerId == "P1");
        check(lastPenaltyResult.Reason == RoundCompletionReason.DrawPileExhausted
            && lastPenaltyScore.BaseScore >= 10
            && lastPenaltyScore.AdjustedRoundScore == lastPenaltyScore.BaseScore * 2,
            "final penalty-card depletion scores only after the remaining reaction window ends");

        var (engine, handIndex) = FindEmptyPilePenaltyScenario();
        var initial = engine.GetPublicView();
        var windowId = initial.ReactionWindowId!.Value;
        var penalty = engine.ProcessAction("P1", new ThrowReactionCard(handIndex, windowId, Id(1801)));
        check(penalty.Accepted && engine.GetPublicView().Phase == GamePhase.AwaitingReactionResolution
            && engine.GetPublicView().Players.Single(player => player.PlayerId == "P1").RoundPenaltyPoints == 10,
            "empty-pile invalid throw records +10 and leaves the reaction window open");
        var repeatedPenalty = engine.ProcessAction(
            "P1",
            new ThrowReactionCard(handIndex, windowId, Id(1802)));
        check(repeatedPenalty.Accepted && engine.GetPublicView().Phase == GamePhase.AwaitingReactionResolution
            && engine.GetPublicView().Players.Single(player => player.PlayerId == "P1").RoundPenaltyPoints == 20,
            "each further required penalty draw while depleted adds another +10 without closing reactions");
        var publicBeforeCompletion = JsonSerializer.Serialize(engine.GetPublicView());
        check(engine.GetPublicView().LastCompletedRound is null
            && !publicBeforeCompletion.Contains(JsonSerializer.Serialize(engine.GetPlayerView("P1").OwnHand[handIndex].KnownCard),
                StringComparison.Ordinal),
            "hidden hand identities stay private before the round-end reveal");

        CompleteByMarkingDone(engine);
        var result = engine.GetPublicView().LastCompletedRound
            ?? throw new InvalidOperationException("The empty-pile reaction window should complete the round.");
        var p1 = result.Scores.Single(score => score.PlayerId == "P1");
        check(result.CallerId is null && result.Reason == RoundCompletionReason.DrawPileExhausted
            && p1.BaseScore >= 20 && p1.AdjustedRoundScore == p1.BaseScore * 2,
            "all +10 penalties are included before no-caller score doubling");
        check(result.RevealedHands.Count == 2
            && result.RevealedHands.SelectMany(hand => hand.Cards).Any(),
            "round-end projection reveals all hands only after scoring");
    }

    private static void TestScoringVariants(Action<bool, string> check)
    {
        var uniqueCaller = RoundScorer.Calculate(new[]
        {
            new PlayerRoundBase("A", 2, 10),
            new PlayerRoundBase("B", 20, 0),
            new PlayerRoundBase("C", 15, 0)
        }, "A");
        check(uniqueCaller["A"] == 12 && uniqueCaller["B"] == 40 && uniqueCaller["C"] == 30,
            "unique lowest caller keeps its penalty-inclusive base while all others double");

        var tiedCaller = RoundScorer.Calculate(new[]
        {
            new PlayerRoundBase("A", 7, 0),
            new PlayerRoundBase("B", 7, 0),
            new PlayerRoundBase("C", 20, 0)
        }, "A");
        check(tiedCaller["A"] == 14 && tiedCaller["B"] == 7 && tiedCaller["C"] == 20,
            "a lowest-score tie doubles only the caller");

        var losingCaller = RoundScorer.Calculate(new[]
        {
            new PlayerRoundBase("A", 15, 10),
            new PlayerRoundBase("B", 3, 0)
        }, "A");
        check(losingCaller["A"] == 50 && losingCaller["B"] == 3,
            "caller above the lowest base score doubles only their own score");

        var jokerAndEmpty = RoundScorer.Calculate(new[]
        {
            new PlayerRoundBase("A", -1, 0),
            new PlayerRoundBase("B", 0, 10),
            new PlayerRoundBase("C", 4, 0)
        }, null);
        check(jokerAndEmpty["A"] == -2 && jokerAndEmpty["B"] == 20 && jokerAndEmpty["C"] == 8,
            "no-caller depletion doubles negative Joker, empty-hand, and penalty-inclusive scores");
    }

    private static void TestDeterministicMatchAndRoundReset(Action<bool, string> check)
    {
        var engine = CreateEngine(2, 1, 1901);
        var playerIds = new[] { "P1", "P2" };
        var previousRoundHandIds = new HashSet<Guid>();
        var completedRounds = new List<RoundCompletionView>();
        var roundCount = 0;

        while (!engine.GetPublicView().MatchCompleted && roundCount < 20)
        {
            var view = engine.GetPublicView();
            check(view.Phase == GamePhase.WaitingForInitialPeeks && view.ActivePlayerId == "P1",
                $"simulated match round {view.RoundNumber} starts with host and fresh peeks");
            CompletePeeks(engine, playerIds);
            var currentRoundIds = CollectHandIds(engine, playerIds);
            if (roundCount > 0)
                check(!previousRoundHandIds.Intersect(currentRoundIds).Any(),
                    "simulated match round uses fresh physical card identities");
            previousRoundHandIds = currentRoundIds;
            roundCount++;

            while (!engine.GetPublicView().MatchCompleted
                && engine.GetPublicView().RoundNumber == roundCount
                && engine.GetPublicView().Phase == GamePhase.AwaitingDraw)
            {
                var active = engine.GetPublicView().ActivePlayerId;
                check(engine.ProcessAction(active, new DrawCard()).Accepted,
                    $"simulated match round {roundCount} mandatory draw");
                check(engine.ProcessAction(active, new DiscardDrawnCard()).Accepted,
                    $"simulated match round {roundCount} draw resolution");
                check(engine.ProcessAction(active, new EndTurn()).Accepted,
                    $"simulated match round {roundCount} opens ordinary reactions");
                check(engine.GetPublicView().Phase == GamePhase.AwaitingReactionResolution,
                    "non-final match turn opens its normal reaction window");
                CompleteByMarkingDone(engine);
                var completedRound = engine.GetPublicView().LastCompletedRound;
                if (completedRound is not null
                    && completedRounds.All(existing => existing.RoundNumber != completedRound.RoundNumber))
                    completedRounds.Add(completedRound);
                AssertCardConservation(engine, check, $"simulated match round {roundCount}");
            }
        }

        var finished = engine.GetPublicView();
        check(finished.MatchCompleted && roundCount >= 1 && roundCount < 20,
            "deterministic full match terminates when a cumulative score reaches 100");
        check(finished.Phase == GamePhase.RoundCompleted
            && finished.WinnerPlayerIds.Count > 0
            && finished.Players.Single(player => player.PlayerId == finished.WinnerPlayerIds[0]).CumulativeScore
                == finished.Players.Min(player => player.CumulativeScore),
            "match completion exposes all lowest-score winner IDs");
        check(finished.LastCompletedRound?.Scores.All(score =>
            finished.Players.Single(player => player.PlayerId == score.PlayerId).CumulativeScore == score.CumulativeScore)
            == true,
            "round result cumulative scores match the updated public ledger");
        Console.WriteLine(
            $"SAMPLE MATCH seed=1901: {string.Join("; ", completedRounds.Select(round =>
                $"round {round.RoundNumber} ({round.Reason}) " +
                string.Join(", ", round.Scores.Select(score =>
                    $"{score.PlayerId} base {score.BaseScore}, adjusted {score.AdjustedRoundScore}, cumulative {score.CumulativeScore}"))))}; " +
            $"winners {string.Join(", ", finished.WinnerPlayerIds)}");
    }

    private static AlzheimerRoundEngine CreateFourPlayerReactionCallerScenario()
    {
        for (var seed = 1; seed <= 2_000; seed++)
        {
            var engine = CreateEngine(4, 2, seed);
            CompletePeeks(engine, "P1", "P2", "P3", "P4");
            var queenUses = 0;
            while (queenUses < 3 && engine.GetPublicView().Phase == GamePhase.AwaitingDraw)
            {
                var active = engine.GetPublicView().ActivePlayerId;
                engine.ProcessAction(active, new DrawCard());
                if (active == "P2"
                    && engine.GetPlayerView("P2").PendingDrawnCard?.Rank == CardRank.Queen)
                {
                    engine.ProcessAction(active, new ActivateDrawnAbility());
                    engine.ProcessAction(active,
                        new ResolveQueenAbility(engine.GetPlayerView("P2").OwnHand.Count - 1));
                    queenUses++;
                }
                else
                {
                    engine.ProcessAction(active, new DiscardDrawnCard());
                }

                if (engine.GetPublicView().CallerId is not null)
                    break;
                engine.ProcessAction(active, new EndTurn());
                CompleteByMarkingDone(engine);
            }

            if (queenUses != 3
                || engine.GetPublicView().Players.Single(player => player.PlayerId == "P2").HandCount != 1)
                continue;

            while (engine.GetPublicView().Phase == GamePhase.AwaitingDraw
                && engine.GetPublicView().ActivePlayerId != "P1")
                PlayOrdinaryTurn(engine);

            for (var attempt = 0; attempt < 100
                && engine.GetPublicView().Phase == GamePhase.AwaitingDraw
                && engine.GetPublicView().DrawPileCount > 0; attempt++)
            {
                engine.ProcessAction("P1", new DrawCard());
                var drawn = engine.GetPlayerView("P1").PendingDrawnCard!;
                engine.ProcessAction("P1", new DiscardDrawnCard());
                engine.ProcessAction("P1", new EndTurn());
                if (drawn.Rank == engine.GetPlayerView("P2").OwnHand[0].KnownCard?.Rank)
                    return engine;
                CompleteByMarkingDone(engine);
                while (engine.GetPublicView().Phase == GamePhase.AwaitingDraw
                    && engine.GetPublicView().ActivePlayerId != "P1")
                    PlayOrdinaryTurn(engine);
            }
        }

        throw new InvalidOperationException("Could not construct a four-player automatic reaction-call scenario.");
    }

    private static AlzheimerRoundEngine CreateFinalDrawEngine(
        CardRank rank,
        int seed,
        FixedClock clock)
    {
        var engine = CreateEngine(2, 1, seed, clock);
        CompletePeeks(engine, "P1", "P2");
        engine.ProcessAction("P1", new DrawCard());
        engine.ProcessAction("P1", new DiscardDrawnCard());
        engine.ProcessAction("P1", new CallAlzheimer());
        if (engine.GetPublicView().ActivePlayerId != "P2")
            throw new InvalidOperationException("The voluntary caller did not advance to the guest's final turn.");
        if (engine.GetPublicView().DrawPileCount == 0)
            throw new InvalidOperationException("The final-turn ability scenario unexpectedly exhausted its draw pile.");
        return engine;
    }

    private static AlzheimerRoundEngine CreateTwoDrawRemainingEngine(int seed)
    {
        var engine = CreateEngine(2, 1, seed);
        CompletePeeks(engine, "P1", "P2");
        while (engine.GetPublicView().DrawPileCount > 2)
            PlayOrdinaryTurn(engine);
        return engine;
    }

    private static AlzheimerRoundEngine CreateEngineWithFinalDraw(CardRank rank, out FixedClock clock)
    {
        for (var seed = 1; seed <= 2_000; seed++)
        {
            clock = new FixedClock(new DateTimeOffset(2026, 11, 25, 0, 0, 0, TimeSpan.Zero));
            var engine = CreateFinalDrawEngine(rank, seed, clock);
            engine.ProcessAction("P2", new DrawCard());
            if (engine.GetPlayerView("P2").PendingDrawnCard?.Rank == rank)
                return engine;
        }

        throw new InvalidOperationException($"Could not find a deterministic final-turn draw for {rank}.");
    }

    private static AlzheimerRoundEngine CreateOneDrawRemainingEngine(int seed)
    {
        var engine = CreateEngine(2, 1, seed);
        CompletePeeks(engine, "P1", "P2");
        while (engine.GetPublicView().DrawPileCount > 1)
            PlayOrdinaryTurn(engine);
        return engine;
    }

    private static (AlzheimerRoundEngine Engine, int HandIndex) FindEmptyPilePenaltyScenario()
    {
        for (var seed = 1; seed <= 500; seed++)
        {
            var engine = CreateOneDrawRemainingEngine(seed + 2_000);
            var active = engine.GetPublicView().ActivePlayerId;
            engine.ProcessAction(active, new DrawCard());
            engine.ProcessAction(active, new DiscardDrawnCard());
            engine.ProcessAction(active, new EndTurn());
            if (engine.GetPublicView().DrawPileCount != 0
                || engine.GetPublicView().Phase != GamePhase.AwaitingReactionResolution)
                continue;

            var topRank = engine.GetPublicView().DiscardPile[^1].Rank;
            var hand = engine.GetPlayerView("P1").OwnHand;
            var mismatch = -1;
            for (var index = 0; index < hand.Count; index++)
                if (hand[index].KnownCard is { } knownCard && knownCard.Rank != topRank)
                {
                    mismatch = index;
                    break;
                }
            if (mismatch >= 0)
                return (engine, mismatch);
        }

        throw new InvalidOperationException("Could not find a deterministic empty-pile penalty scenario.");
    }

    private static (AlzheimerRoundEngine Engine, int HandIndex) FindFinalPenaltyCardScenario()
    {
        for (var seed = 1; seed <= 500; seed++)
        {
            var engine = CreateTwoDrawRemainingEngine(seed + 2_500);
            var active = engine.GetPublicView().ActivePlayerId;
            engine.ProcessAction(active, new DrawCard());
            engine.ProcessAction(active, new DiscardDrawnCard());
            engine.ProcessAction(active, new EndTurn());
            var topRank = engine.GetPublicView().DiscardPile[^1].Rank;
            var hand = engine.GetPlayerView("P1").OwnHand;
            for (var index = 0; index < hand.Count; index++)
                if (hand[index].KnownCard is { } knownCard && knownCard.Rank != topRank)
                    return (engine, index);
        }

        throw new InvalidOperationException("Could not find a deterministic last-penalty-card scenario.");
    }

    private static void PlayOrdinaryTurn(AlzheimerRoundEngine engine)
    {
        var active = engine.GetPublicView().ActivePlayerId;
        if (!engine.ProcessAction(active, new DrawCard()).Accepted
            || !engine.ProcessAction(active, new DiscardDrawnCard()).Accepted
            || !engine.ProcessAction(active, new EndTurn()).Accepted)
            throw new InvalidOperationException($"Could not play ordinary turn for {active}.");
        if (engine.GetPublicView().Phase == GamePhase.AwaitingReactionResolution)
            CompleteByMarkingDone(engine);
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

    private static void CompleteByMarkingDone(AlzheimerRoundEngine engine)
    {
        var view = engine.GetPublicView();
        var windowId = view.ReactionWindowId
            ?? throw new InvalidOperationException("No reaction window is active.");
        foreach (var player in view.Players)
        {
            if (engine.GetPublicView().Phase != GamePhase.AwaitingReactionResolution)
                break;
            var result = engine.ProcessAction(
                player.PlayerId,
                new MarkReactionDone(windowId, Guid.NewGuid()));
            if (!result.Accepted)
                throw new InvalidOperationException($"Could not mark {player.PlayerId} Done: {result.ErrorCode}.");
        }
    }

    private static HashSet<Guid> CollectHandIds(AlzheimerRoundEngine engine, IEnumerable<string> playerIds) =>
        playerIds.SelectMany(playerId => engine.GetPlayerView(playerId).OwnHand)
            .Where(slot => slot.KnownCard is not null)
            .Select(slot => slot.KnownCard!.PhysicalCardId)
            .ToHashSet();

    private static string[] PlayerIds(int count) =>
        Enumerable.Range(1, count).Select(index => $"P{index}").ToArray();

    private static AlzheimerRoundEngine CreateEngine(
        int playerCount,
        int deckCount,
        int seed,
        IGameClock? clock = null)
    {
        return AlzheimerRoundEngine.StartNewRound(
            PlayerIds(playerCount).Select(playerId => new PlayerDefinition(playerId, playerId)).ToArray(),
            "P1",
            new RoomSettings(10, deckCount),
            new Random(seed),
            CreateIdFactory(),
            clock);
    }

    private static void AssertCardConservation(
        AlzheimerRoundEngine engine,
        Action<bool, string> check,
        string label)
    {
        var view = engine.GetPublicView();
        var pendingDraw = engine.GetPlayerView(view.ActivePlayerId).PendingDrawnCard is null ? 0 : 1;
        check(view.DrawPileCount + view.DiscardPile.Count
            + view.Players.Sum(player => player.HandCount) + pendingDraw == view.DeckCount * 54,
            $"{label}: all cards remain conserved");
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

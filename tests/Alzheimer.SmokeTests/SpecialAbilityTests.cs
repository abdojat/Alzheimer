using Alzheimer.Core;
using System.Text.Json;

internal static class SpecialAbilityTests
{
    public static void Run(Action<bool, string> check)
    {
        TestJackPeeksAndPrivacy(check);
        TestQueenDiscardAndReactionTargets(check);
        TestQueenEmptyHandAutomaticallyCalls(check);
        TestKingSwapsBlindlyAndInvalidatesKnowledge(check);
        TestActivationChoicesAndRejectedActions(check);
        TestAbilityDeadlines(check);
    }

    private static void TestJackPeeksAndPrivacy(Action<bool, string> check)
    {
        foreach (var handIndex in new[] { 0, 3 })
        {
            var engine = CreateEngineWithNextDraw(CardRank.Jack);
            var before = engine.GetPlayerView("P1");
            var knownBefore = before.OwnHand[handIndex].Knowledge == CardKnowledge.KnownToOwner;

            check(engine.ProcessAction("P1", new ActivateDrawnAbility()).Accepted,
                "drawn Jack opens an ability window");
            check(engine.GetPublicView().Phase == GamePhase.AwaitingAbilityResolution,
                "Jack activation enters the ability phase");
            var beforeInvalidResolution = Snapshot(engine, "P1", "P2");
            var unauthorizedResolution = engine.ProcessAction("P2", new ResolveJackAbility(handIndex));
            check(!unauthorizedResolution.Accepted
                && unauthorizedResolution.ErrorCode == GameErrorCode.NotActivePlayer,
                "another player cannot resolve the active Jack ability");
            check(Snapshot(engine, "P1", "P2") == beforeInvalidResolution,
                "unauthorized Jack resolution leaves state unchanged");
            var wrongAbility = engine.ProcessAction("P1", new ResolveQueenAbility(handIndex));
            check(!wrongAbility.Accepted && wrongAbility.ErrorCode == GameErrorCode.InvalidAbilityType,
                "a Jack window rejects a Queen resolution command");
            check(Snapshot(engine, "P1", "P2") == beforeInvalidResolution,
                "wrong ability command leaves state unchanged");
            var invalidIndex = engine.ProcessAction("P1", new ResolveJackAbility(4));
            check(!invalidIndex.Accepted && invalidIndex.ErrorCode == GameErrorCode.InvalidHandIndex,
                "Jack rejects an invalid own-hand index");
            check(Snapshot(engine, "P1", "P2") == beforeInvalidResolution,
                "invalid Jack index leaves state unchanged");
            var callDuringAbility = engine.ProcessAction("P1", new CallAlzheimer());
            check(!callDuringAbility.Accepted && callDuringAbility.ErrorCode == GameErrorCode.InvalidPhase,
                "Alzheimer cannot be called during the ability window");
            check(Snapshot(engine, "P1", "P2") == beforeInvalidResolution,
                "call attempt during ability leaves state unchanged");
            var resolution = engine.ProcessAction("P1", new ResolveJackAbility(handIndex));
            check(resolution.Accepted,
                $"Jack reveals a {(knownBefore ? "known" : "unknown")} own card");

            var ownerView = engine.GetPlayerView("P1");
            var revealedCard = ownerView.OwnHand[handIndex].KnownCard;
            var publicJson = JsonSerializer.Serialize(engine.GetPublicView());
            var otherPlayerJson = JsonSerializer.Serialize(engine.GetPlayerView("P2"));
            var eventsJson = JsonSerializer.Serialize(resolution.Events);
            check(ownerView.OwnHand[handIndex].Knowledge == CardKnowledge.KnownToOwner,
                "Jack marks the selected card known to its owner");
            check(revealedCard is not null, "Jack owner projection contains the revealed identity");
            if (revealedCard is null)
                throw new InvalidOperationException("The Jack owner projection omitted its revealed card.");
            check(!publicJson.Contains(revealedCard.PhysicalCardId.ToString(), StringComparison.Ordinal),
                "Jack-revealed identity is absent from the public projection");
            check(!otherPlayerJson.Contains(revealedCard.PhysicalCardId.ToString(), StringComparison.Ordinal),
                "Jack-revealed identity is absent from another player's projection");
            check(!eventsJson.Contains(revealedCard.PhysicalCardId.ToString(), StringComparison.Ordinal),
                "Jack-revealed identity is absent from serialized public events");
            check(engine.GetPublicView().Phase == GamePhase.AwaitingPostResolutionDecision,
                "Jack completion enters the post-resolution decision phase");
            check(engine.GetPublicView().DiscardPile.Last().Rank == CardRank.Jack,
                "used Jack is discarded face up");
            AssertCardConservation(engine, check, "Jack ability");
        }
    }

    private static void TestQueenDiscardAndReactionTargets(Action<bool, string> check)
    {
        foreach (var handIndex in new[] { 0, 3 })
        {
            var engine = CreateEngineWithNextDraw(CardRank.Queen);
            var before = engine.GetPlayerView("P1");
            var wasKnown = before.OwnHand[handIndex].Knowledge == CardKnowledge.KnownToOwner;
            var handCount = before.OwnHand.Count;

            check(engine.ProcessAction("P1", new ActivateDrawnAbility()).Accepted,
                "drawn Queen opens an ability window");
            var result = engine.ProcessAction("P1", new ResolveQueenAbility(handIndex));
            check(result.Accepted, $"Queen discards a {(wasKnown ? "known" : "unknown")} hand card");

            var view = engine.GetPublicView();
            var targets = view.QueenReactionTargets;
            var selected = view.DiscardPile.Last();
            check(view.Players.Single(player => player.PlayerId == "P1").HandCount == handCount - 1,
                "Queen reduces its user's hand size by one");
            check(view.DiscardPile[^2].Rank == CardRank.Queen && selected.PhysicalCardId != Guid.Empty,
                "Queen and selected card are both face up in the discard pile");
            check(targets is not null
                && targets.QueenRank == CardRank.Queen
                && targets.SelectedCardRank == selected.Rank
                && targets.FinalTopCard.PhysicalCardId == selected.PhysicalCardId,
                "Queen reaction state preserves both matching ranks and the final top card");
            check(view.Phase == GamePhase.AwaitingPostResolutionDecision,
                "Queen completion enters the post-resolution decision phase");
            check(!JsonSerializer.Serialize(result.Events).Contains(selected.PhysicalCardId.ToString(),
                StringComparison.Ordinal), "Queen resolution events contain no card identity");

            check(engine.ProcessAction("P1", new EndTurn()).Accepted,
                "Queen user can proceed to the reaction phase");
            check(engine.GetPublicView().QueenReactionTargets is not null,
                "Queen's two-rank targets persist through the reaction phase");
            check(engine.CompleteReactionWindow().Accepted, "Queen reaction phase can complete");
            check(engine.GetPublicView().DiscardPile.Last().PhysicalCardId == selected.PhysicalCardId,
                "Queen-selected card is the discard top after the reaction phase");
            check(engine.GetPublicView().QueenReactionTargets is null,
                "Queen reaction targets clear after the reaction phase");
            AssertCardConservation(engine, check, "Queen ability");
        }

        var removesJack = CreateEngineWithNextDrawAndKnownHandCard(CardRank.Queen, CardRank.Jack);
        removesJack.ProcessAction("P1", new ActivateDrawnAbility());
        var removedSpecial = removesJack.ProcessAction("P1", new ResolveQueenAbility(0));
        check(removedSpecial.Accepted && removedSpecial.Events.Count == 1
            && removedSpecial.Events[0].Type == GameEventType.AbilityResolved
            && removesJack.GetPublicView().DiscardPile.Last().Rank == CardRank.Jack,
            "Queen removes a Jack from hand without activating the removed card's ability");
    }

    private static void TestQueenEmptyHandAutomaticallyCalls(Action<bool, string> check)
    {
        var engine = CreateEngineWhereQueenEmptiesPlayerHand(out var queenAbilitiesUsed);
        var view = engine.GetPublicView();
        check(queenAbilitiesUsed == 4, "four Queen abilities reduce the player's hand to zero");
        check(view.Players.Single(player => player.PlayerId == "P1").HandCount == 0,
            "Queen can leave its user with an empty hand");
        check(view.CallerId == "P1" && view.Phase == GamePhase.FinalPhase,
            "empty-hand Queen ability immediately records the automatic Alzheimer call");
        check(view.QueenReactionTargets is null,
            "Queen-caused automatic call does not leave an upcoming reaction window");
        AssertCardConservation(engine, check, "empty-hand Queen ability");
    }

    private static void TestKingSwapsBlindlyAndInvalidatesKnowledge(Action<bool, string> check)
    {
        foreach (var targetPlayerId in new[] { "P2", "P3" })
        foreach (var (ownHandIndex, targetHandIndex) in new[] { (0, 3), (3, 0) })
        {
            var engine = CreateEngineWithNextDraw(CardRank.King, playerCount: 3);
            var ownCardBefore = engine.GetPlayerView("P1").OwnHand[ownHandIndex];
            var targetCardBefore = engine.GetPlayerView(targetPlayerId).OwnHand[targetHandIndex];
            var ownKnownId = ownCardBefore.KnownCard?.PhysicalCardId;
            var targetKnownId = targetCardBefore.KnownCard?.PhysicalCardId;
            check(ownCardBefore.Knowledge == (ownKnownId is null ? CardKnowledge.Unknown : CardKnowledge.KnownToOwner)
                && targetCardBefore.Knowledge == (targetKnownId is null ? CardKnowledge.Unknown : CardKnowledge.KnownToOwner),
                "King scenario includes both known and unknown hand positions");

            check(engine.ProcessAction("P1", new ActivateDrawnAbility()).Accepted,
                "drawn King opens an ability window");
            var beforeInvalid = Snapshot(engine, "P1", "P2", "P3");
            var selfTarget = engine.ProcessAction("P1", new ResolveKingAbility("P1", 0, 0));
            check(!selfTarget.Accepted && selfTarget.ErrorCode == GameErrorCode.InvalidAbilityTarget,
                "King rejects its owner as the opponent target");
            check(Snapshot(engine, "P1", "P2", "P3") == beforeInvalid,
                "invalid King opponent leaves state unchanged");
            var badIndex = engine.ProcessAction("P1", new ResolveKingAbility(targetPlayerId, 0, 99));
            check(!badIndex.Accepted && badIndex.ErrorCode == GameErrorCode.InvalidHandIndex,
                "King rejects an out-of-range opponent hand index");
            check(Snapshot(engine, "P1", "P2", "P3") == beforeInvalid,
                "invalid King hand index leaves state unchanged");
            var badOwnIndex = engine.ProcessAction("P1", new ResolveKingAbility(targetPlayerId, -1, targetHandIndex));
            check(!badOwnIndex.Accepted && badOwnIndex.ErrorCode == GameErrorCode.InvalidHandIndex,
                "King rejects an out-of-range owner hand index");
            check(Snapshot(engine, "P1", "P2", "P3") == beforeInvalid,
                "invalid King owner index leaves state unchanged");

            var resolved = engine.ProcessAction(
                "P1",
                new ResolveKingAbility(targetPlayerId, ownHandIndex, targetHandIndex));
            check(resolved.Accepted, $"King can blind-swap with opponent {targetPlayerId}");
            check(engine.GetPlayerView("P1").OwnHand[ownHandIndex].Knowledge == CardKnowledge.Unknown
                && engine.GetPlayerView("P1").OwnHand[ownHandIndex].KnownCard is null,
                "King makes the received card unknown to the active player");
            check(engine.GetPlayerView(targetPlayerId).OwnHand[targetHandIndex].Knowledge == CardKnowledge.Unknown
                && engine.GetPlayerView(targetPlayerId).OwnHand[targetHandIndex].KnownCard is null,
                "King makes the received card unknown to the opponent");

            var publicJson = JsonSerializer.Serialize(engine.GetPublicView());
            var ownerJson = JsonSerializer.Serialize(engine.GetPlayerView("P1"));
            var targetJson = JsonSerializer.Serialize(engine.GetPlayerView(targetPlayerId));
            var thirdPartyJson = JsonSerializer.Serialize(engine.GetPlayerView(
                targetPlayerId == "P2" ? "P3" : "P2"));
            var eventJson = JsonSerializer.Serialize(resolved.Events);
            foreach (var knownId in new[] { ownKnownId, targetKnownId }.OfType<Guid>())
            {
                check(!publicJson.Contains(knownId.ToString(), StringComparison.Ordinal),
                    "King-swapped identity is not exposed publicly");
                check(!ownerJson.Contains(knownId.ToString(), StringComparison.Ordinal)
                    && !targetJson.Contains(knownId.ToString(), StringComparison.Ordinal),
                    "King-swapped identity is hidden from both new owners");
                check(!thirdPartyJson.Contains(knownId.ToString(), StringComparison.Ordinal),
                    "King-swapped identity is not exposed to an uninvolved player");
                check(!eventJson.Contains(knownId.ToString(), StringComparison.Ordinal),
                    "King transition events contain no swapped card identity");
            }
            check(engine.GetPublicView().DiscardPile.Last().Rank == CardRank.King,
                "used King is discarded face up");
            AssertCardConservation(engine, check, $"King swap with {targetPlayerId}");
        }
    }

    private static void TestActivationChoicesAndRejectedActions(Action<bool, string> check)
    {
        var noKingTargets = DrawResolutionRules.GetAvailableOptions(CardRank.King, 4, 0);
        check(noKingTargets.SequenceEqual(new[] { DrawResolutionOption.Discard, DrawResolutionOption.Replace }),
            "King with no opponent target offers only discard or keep-by-replacement");
        var noOwnTarget = DrawResolutionRules.GetAvailableOptions(CardRank.Queen, 0, 3);
        check(noOwnTarget.SequenceEqual(new[] { DrawResolutionOption.Discard }),
            "ability without an own-hand target offers only discard");

        var ordinary = CreateEngineWithNextDraw(CardRank.Seven);
        var ordinaryView = ordinary.GetPlayerView("P1");
        check(ordinaryView.AvailableDrawResolutions.SequenceEqual(new[]
            { DrawResolutionOption.Discard, DrawResolutionOption.Replace }),
            "ordinary draw offers discard and replace but no ability");
        var ordinaryBefore = Snapshot(ordinary, "P1", "P2");
        var ordinaryActivation = ordinary.ProcessAction("P1", new ActivateDrawnAbility());
        check(!ordinaryActivation.Accepted && ordinaryActivation.ErrorCode == GameErrorCode.InvalidAbilityCard,
            "only a drawn Jack, Queen, or King can activate an ability");
        check(Snapshot(ordinary, "P1", "P2") == ordinaryBefore,
            "ordinary-card ability activation rejection changes no state");

        foreach (var rank in new[] { CardRank.Jack, CardRank.Queen, CardRank.King })
        {
            var engine = CreateEngineWithNextDraw(rank);
            var choices = engine.GetPlayerView("P1").AvailableDrawResolutions;
            check(choices.Contains(DrawResolutionOption.Discard)
                && choices.Contains(DrawResolutionOption.Replace)
                && choices.Contains(DrawResolutionOption.ActivateAbility),
                $"drawn {rank} offers discard, replace, and ability activation");

            var before = Snapshot(engine, "P1", "P2");
            var unauthorized = engine.ProcessAction("P2", new ActivateDrawnAbility());
            check(!unauthorized.Accepted && unauthorized.ErrorCode == GameErrorCode.NotActivePlayer,
                $"non-active player cannot activate drawn {rank}");
            check(Snapshot(engine, "P1", "P2") == before,
                $"unauthorized {rank} activation leaves state unchanged");

            var discarded = engine.ProcessAction("P1", new DiscardDrawnCard());
            check(discarded.Accepted && engine.GetPublicView().Phase == GamePhase.AwaitingPostResolutionDecision,
                $"drawn {rank} can be discarded without its ability");
            check(discarded.Events.All(gameEvent => gameEvent.Type != GameEventType.AbilityWindowOpened),
                $"discarding {rank} does not activate its ability");
        }

        var kingOptions = CreateEngineWithNextDraw(CardRank.King);
        check(kingOptions.GetPlayerView("P1").AvailableDrawResolutions.Contains(DrawResolutionOption.ActivateAbility),
            "King activation is offered when an opponent has a valid target card");
    }

    private static void TestAbilityDeadlines(Action<bool, string> check)
    {
        var initialTime = new DateTimeOffset(2026, 11, 25, 1, 0, 0, TimeSpan.FromHours(3));
        var clock = new FixedClock(initialTime);
        var engine = CreateEngineWithNextDraw(CardRank.Jack, clock: clock);
        check(engine.ProcessAction("P1", new ActivateDrawnAbility()).Accepted,
            "ability timer starts when the ability is activated");
        var deadline = engine.GetPlayerView("P1").AbilityDeadlineUtc;
        check(deadline == initialTime.ToUniversalTime().AddSeconds(30),
            "ability deadline is an authoritative UTC time exactly 30 seconds later");
        check(engine.GetPlayerView("P2").AbilityDeadlineUtc is null,
            "ability deadline is private to the active player projection");
        var beforeDeadline = Snapshot(engine, "P1", "P2");
        check(engine.ProcessTimeouts().ErrorCode == GameErrorCode.AbilityDeadlineNotReached,
            "ability cannot expire before its deadline");
        check(Snapshot(engine, "P1", "P2") == beforeDeadline,
            "early timeout check leaves the state unchanged");

        clock.Set(deadline!.Value.AddTicks(-1));
        check(engine.ProcessAction("P1", new ResolveJackAbility(3)).Accepted,
            "ability can complete immediately before the deadline");
        check(engine.GetPublicView().Phase == GamePhase.AwaitingPostResolutionDecision,
            "completed ability leaves the ability phase");

        var timeoutClock = new FixedClock(initialTime);
        var timeoutEngine = CreateEngineWithNextDraw(CardRank.Jack, clock: timeoutClock);
        var drawnJack = timeoutEngine.GetPlayerView("P1").PendingDrawnCard!;
        timeoutEngine.ProcessAction("P1", new ActivateDrawnAbility());
        var timeoutDeadline = timeoutEngine.GetPlayerView("P1").AbilityDeadlineUtc!.Value;
        timeoutClock.Set(timeoutDeadline);
        var lateAbility = timeoutEngine.ProcessAction("P1", new ResolveJackAbility(3));
        check(!lateAbility.Accepted && lateAbility.ErrorCode == GameErrorCode.AbilityWindowExpired,
            "an ability action at the exact deadline is rejected");
        check(lateAbility.Events.Any(gameEvent => gameEvent.Type == GameEventType.AbilityExpired),
            "deadline-boundary rejection reports the timeout transition");
        check(timeoutEngine.GetPublicView().Phase == GamePhase.AwaitingPostResolutionDecision
            && timeoutEngine.GetPublicView().DiscardPile.Last().PhysicalCardId == drawnJack.PhysicalCardId,
            "expired ability discards its drawn card and advances to the decision phase");
        check(timeoutEngine.GetPlayerView("P1").OwnHand[3].Knowledge == CardKnowledge.Unknown,
            "expired Jack has no peek effect");
        check(timeoutEngine.ProcessTimeouts().ErrorCode == GameErrorCode.AbilityDeadlineNotReached,
            "an ability timeout is processed only once");
        AssertCardConservation(timeoutEngine, check, "expired Jack ability");

        var expiryClock = new FixedClock(initialTime);
        var expiryEngine = CreateEngineWithNextDraw(CardRank.Queen, clock: expiryClock);
        expiryEngine.ProcessAction("P1", new ActivateDrawnAbility());
        var expiryDeadline = expiryEngine.GetPlayerView("P1").AbilityDeadlineUtc!.Value;
        expiryClock.Set(expiryDeadline);
        var expired = expiryEngine.ProcessTimeouts();
        check(expired.Accepted && expired.Events.Single().Type == GameEventType.AbilityExpired,
            "server timeout processing expires an incomplete Queen ability");
        check(expiryEngine.GetPublicView().Phase == GamePhase.AwaitingPostResolutionDecision
            && expiryEngine.GetPublicView().DiscardPile.Last().Rank == CardRank.Queen,
            "timeout discards the Queen without removing a hand card");
        check(expiryEngine.GetPublicView().QueenReactionTargets is null,
            "an expired Queen creates no reaction targets");
        AssertCardConservation(expiryEngine, check, "expired Queen ability");
    }

    private static AlzheimerRoundEngine CreateEngineWithNextDraw(
        CardRank rank,
        int playerCount = 2,
        int deckCount = 1,
        IGameClock? clock = null)
    {
        for (var seed = 1; seed <= 2_000; seed++)
        {
            var engine = CreateEngine(playerCount, deckCount, seed, clock);
            CompletePeeks(engine, Enumerable.Range(1, playerCount).Select(index => $"P{index}").ToArray());
            engine.ProcessAction("P1", new DrawCard());
            if (engine.GetPlayerView("P1").PendingDrawnCard!.Rank == rank)
                return engine;
        }

        throw new InvalidOperationException($"Could not find a deterministic draw for {rank}.");
    }

    private static AlzheimerRoundEngine CreateEngineWithNextDrawAndKnownHandCard(
        CardRank drawRank,
        CardRank handRank)
    {
        for (var seed = 1; seed <= 2_000; seed++)
        {
            var engine = CreateEngine(2, 1, seed);
            CompletePeeks(engine, "P1", "P2");
            engine.ProcessAction("P1", new DrawCard());
            if (engine.GetPlayerView("P1").PendingDrawnCard!.Rank == drawRank
                && engine.GetPlayerView("P1").OwnHand[0].KnownCard?.Rank == handRank)
                return engine;
        }

        throw new InvalidOperationException($"Could not find a deterministic {drawRank}/{handRank} card pair.");
    }

    private static AlzheimerRoundEngine CreateEngineWhereQueenEmptiesPlayerHand(out int queenAbilitiesUsed)
    {
        for (var seed = 1; seed <= 500; seed++)
        {
            var engine = CreateEngine(2, 2, seed);
            CompletePeeks(engine, "P1", "P2");
            var used = 0;
            while (engine.GetPublicView().Phase is not (GamePhase.FinalPhase or GamePhase.RoundCompleted)
                && engine.GetPublicView().DrawPileCount > 0)
            {
                var activePlayer = engine.GetPublicView().ActivePlayerId;
                engine.ProcessAction(activePlayer, new DrawCard());
                var drawn = engine.GetPlayerView(activePlayer).PendingDrawnCard!;
                if (activePlayer == "P1" && drawn.Rank == CardRank.Queen)
                {
                    engine.ProcessAction(activePlayer, new ActivateDrawnAbility());
                    engine.ProcessAction(activePlayer, new ResolveQueenAbility(0));
                    used++;
                }
                else
                {
                    engine.ProcessAction(activePlayer, new DiscardDrawnCard());
                }

                if (engine.GetPublicView().Phase == GamePhase.FinalPhase)
                    break;
                engine.ProcessAction(activePlayer, new EndTurn());
                engine.CompleteReactionWindow();
            }

            if (used == 4 && engine.GetPublicView().CallerId == "P1")
            {
                queenAbilitiesUsed = used;
                return engine;
            }
        }

        throw new InvalidOperationException("Could not find a deterministic four-Queen empty-hand sequence.");
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

    private static void CompletePeeks(AlzheimerRoundEngine engine, params string[] playerIds)
    {
        foreach (var playerId in playerIds)
        {
            var result = engine.ProcessAction(playerId, new PeekInitialCards(0, 1));
            if (!result.Accepted)
                throw new InvalidOperationException($"Initial peek failed for {playerId}: {result.ErrorCode}.");
        }
    }

    private static string Snapshot(AlzheimerRoundEngine engine, params string[] playerIds) =>
        JsonSerializer.Serialize(new
        {
            Public = engine.GetPublicView(),
            Players = playerIds.Select(engine.GetPlayerView).ToArray()
        });

    private static void AssertCardConservation(AlzheimerRoundEngine engine, Action<bool, string> check, string label)
    {
        var view = engine.GetPublicView();
        var pendingCount = engine.GetPlayerView(view.ActivePlayerId).PendingDrawnCard is null ? 0 : 1;
        check(view.DrawPileCount + view.DiscardPile.Count
            + view.Players.Sum(player => player.HandCount) + pendingCount == view.DeckCount * 54,
            $"{label}: public card count is conserved");
    }

    private static Func<Guid> CreateIdFactory()
    {
        var next = 0;
        return () => new Guid(++next, 0, 0, new byte[8]);
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IGameClock
    {
        public DateTimeOffset UtcNow { get; private set; } = utcNow;

        public void Set(DateTimeOffset value) => UtcNow = value;
    }
}

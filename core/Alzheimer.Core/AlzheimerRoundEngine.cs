namespace Alzheimer.Core;

public sealed class AlzheimerRoundEngine
{
    private readonly MatchState _state;
    private readonly IGameClock _clock;
    private Dictionary<string, PlayerState> _playersById;
    private readonly Random _random;
    private readonly Func<Guid> _physicalCardIdFactory;

    private AlzheimerRoundEngine(MatchState state, IGameClock clock)
    {
        _state = state;
        _clock = clock;
        _random = state.Random;
        _physicalCardIdFactory = state.PhysicalCardIdFactory;
        _playersById = state.Round.Players.ToDictionary(player => player.PlayerId, StringComparer.Ordinal);
        ValidateInvariants();
    }

    public static AlzheimerRoundEngine StartNewRound(
        IReadOnlyList<PlayerDefinition> players,
        string hostPlayerId,
        RoomSettings settings,
        Random? random = null,
        Func<Guid>? physicalCardIdFactory = null,
        IGameClock? clock = null)
    {
        ArgumentNullException.ThrowIfNull(players);
        ArgumentNullException.ThrowIfNull(settings);
        if (players.Count is < 2 or > 10 || players.Count > settings.MaxPlayers)
            throw new ArgumentOutOfRangeException(nameof(players), "The round requires 2–10 players within the room capacity.");
        if (players.Any(player => player is null
            || string.IsNullOrWhiteSpace(player.PlayerId)
            || string.IsNullOrWhiteSpace(player.DisplayName)))
            throw new ArgumentException("Player IDs and display names must be non-empty.", nameof(players));
        if (players.Select(player => player.PlayerId).Distinct(StringComparer.Ordinal).Count() != players.Count)
            throw new ArgumentException("Player IDs must be unique.", nameof(players));

        var hostIndex = -1;
        for (var i = 0; i < players.Count; i++)
            if (string.Equals(players[i].PlayerId, hostPlayerId, StringComparison.Ordinal))
                hostIndex = i;
        if (hostIndex < 0)
            throw new ArgumentException("The host must be one of the round players.", nameof(hostPlayerId));

        var orderedDefinitions = Enumerable.Range(0, players.Count)
            .Select(offset => players[(hostIndex + offset) % players.Count])
            .ToArray();
        random ??= new Random();
        physicalCardIdFactory ??= Guid.NewGuid;
        var round = CreateRound(orderedDefinitions, hostPlayerId, settings, random, physicalCardIdFactory, 1);
        var match = new MatchState
        {
            CumulativeScores = orderedDefinitions.ToDictionary(player => player.PlayerId, _ => 0, StringComparer.Ordinal),
            Players = Array.AsReadOnly(orderedDefinitions),
            Settings = settings,
            Random = random,
            PhysicalCardIdFactory = physicalCardIdFactory,
            Round = round
        };

        return new AlzheimerRoundEngine(match, clock ?? new SystemGameClock());
    }

    private static RoundState CreateRound(
        IReadOnlyList<PlayerDefinition> orderedDefinitions,
        string hostPlayerId,
        RoomSettings settings,
        Random random,
        Func<Guid> physicalCardIdFactory,
        int roundNumber)
    {
        var orderedPlayers = orderedDefinitions
            .Select(player => new PlayerState(player.PlayerId, player.DisplayName))
            .ToList();
        var cards = DeckFactory.CreateShuffled(settings.DeckCount, random, physicalCardIdFactory);
        var dealtCardCount = orderedPlayers.Count * 4;
        if (dealtCardCount > cards.Count)
            throw new ArgumentException("The selected decks do not contain enough cards for the starting hands.", nameof(settings));

        var cardIndex = 0;
        foreach (var player in orderedPlayers)
            for (var handIndex = 0; handIndex < 4; handIndex++)
                player.Hand.Add(new CardSlot(cards[cardIndex++], CardKnowledge.Unknown));

        var round = new RoundState
        {
            DeckCount = settings.DeckCount,
            HostPlayerId = hostPlayerId,
            Players = orderedPlayers,
            DrawPile = new DrawPile(cards.Skip(cardIndex)),
            DiscardPile = new DiscardPile(),
            Turn = new TurnState(0),
            Phase = GamePhase.WaitingForInitialPeeks,
            RoundNumber = roundNumber
        };
        return round;
    }

    public TransitionResult ProcessAction(string? playerId, GameAction? action)
    {
        var occurredAtUtc = _clock.UtcNow.ToUniversalTime();
        var timeoutEvents = ProcessAbilityExpiration(occurredAtUtc);
        var reactionTimeoutEvents = ProcessReactionExpiration(occurredAtUtc);
        timeoutEvents.AddRange(reactionTimeoutEvents);

        if (reactionTimeoutEvents.Count != 0 && action is ThrowReactionCard or MarkReactionDone)
            return Rejected(GameErrorCode.ReactionWindowExpired, occurredAtUtc, timeoutEvents);

        if (string.IsNullOrWhiteSpace(playerId) || !_playersById.ContainsKey(playerId))
            return Rejected(GameErrorCode.InvalidPlayer, occurredAtUtc, timeoutEvents);
        if (action is null)
            return Rejected(GameErrorCode.InvalidAction, occurredAtUtc, timeoutEvents);
        if (timeoutEvents.Count != 0 && action is (ResolveJackAbility or ResolveQueenAbility or ResolveKingAbility))
            return Rejected(GameErrorCode.AbilityWindowExpired, occurredAtUtc, timeoutEvents);

        var result = action switch
        {
            PeekInitialCards peek => Peek(playerId, peek, occurredAtUtc),
            DrawCard => Draw(playerId, occurredAtUtc),
            DiscardDrawnCard => DiscardDrawn(playerId, occurredAtUtc),
            ReplaceHandCard replace => Replace(playerId, replace.HandIndex, occurredAtUtc),
            ActivateDrawnAbility => ActivateAbility(playerId, occurredAtUtc),
            ResolveJackAbility jack => ResolveJack(playerId, jack.HandIndex, occurredAtUtc),
            ResolveQueenAbility queen => ResolveQueen(playerId, queen.HandIndex, occurredAtUtc),
            ResolveKingAbility king => ResolveKing(
                playerId,
                king.TargetPlayerId,
                king.OwnHandIndex,
                king.TargetHandIndex,
                occurredAtUtc),
            CallAlzheimer => Call(playerId, occurredAtUtc),
            EndTurn => End(playerId, occurredAtUtc),
            ThrowReactionCard reaction => ThrowReaction(playerId, reaction, occurredAtUtc),
            MarkReactionDone done => MarkDone(playerId, done, occurredAtUtc),
            _ => Rejected(GameErrorCode.InvalidAction, occurredAtUtc)
        };

        return timeoutEvents.Count == 0
            ? result
            : IncludeEvents(result, timeoutEvents);
    }

    public TransitionResult ProcessTimeouts()
    {
        var occurredAtUtc = _clock.UtcNow.ToUniversalTime();
        var events = ProcessAbilityExpiration(occurredAtUtc);
        if (events.Count == 0)
            events = ProcessReactionExpiration(occurredAtUtc);
        if (events.Count != 0)
            return Accepted(occurredAtUtc, events);

        return Rejected(
            _state.Round.Phase == GamePhase.AwaitingReactionResolution
                ? GameErrorCode.ReactionDeadlineNotReached
                : GameErrorCode.AbilityDeadlineNotReached,
            occurredAtUtc);
    }

    public TransitionResult CompleteReactionWindow()
    {
        var occurredAtUtc = _clock.UtcNow.ToUniversalTime();
        if (_state.Round.Phase != GamePhase.AwaitingReactionResolution)
            return Rejected(GameErrorCode.InvalidPhase, occurredAtUtc);
        if (!IsReactionDeadlineReached(occurredAtUtc))
            return Rejected(GameErrorCode.ReactionDeadlineNotReached, occurredAtUtc);

        return Accepted(occurredAtUtc, CompleteReactionWindowCore());
    }

    public PublicGameView GetPublicView()
    {
        var round = _state.Round;
        var players = round.Players.Select(player => new PublicPlayerView(
            player.PlayerId,
            player.DisplayName,
            player.Hand.Count,
            _state.CumulativeScores[player.PlayerId],
            player.UndrawnPenaltyPoints)).ToArray();
        var discardCards = round.DiscardPile.Cards.Select(ToCardView).ToArray();

        return new PublicGameView(
            round.Phase,
            round.HostPlayerId,
            round.Players[round.Turn.ActiveSeatIndex].PlayerId,
            round.CallerId,
            round.DeckCount,
            round.DrawPile.Count,
            Array.AsReadOnly(players),
            Array.AsReadOnly(discardCards),
            round.QueenReactionTargets is { } targets
                ? new QueenReactionTargetsView(
                    targets.Queen.Rank,
                    targets.SelectedCard.Rank,
                    ToCardView(targets.SelectedCard))
                : null,
            round.ReactionWindowId,
            round.ReactionDeadlineUtc,
            Array.AsReadOnly(round.Players
                .Where(player => round.ReactionDonePlayerIds.Contains(player.PlayerId))
                .Select(player => player.PlayerId)
                .ToArray()),
            Array.AsReadOnly(round.RequiredFinalTurnPlayerIds.ToArray()),
            Array.AsReadOnly(round.PendingFinalTurnPlayerIds.ToArray()),
            round.IsFinalTurnMode,
            round.RoundNumber,
            _state.LastCompletedRound,
            _state.MatchCompleted,
            Array.AsReadOnly(_state.WinnerPlayerIds.ToArray()));
    }

    public PlayerGameView GetPlayerView(string playerId)
    {
        if (string.IsNullOrWhiteSpace(playerId) || !_playersById.TryGetValue(playerId, out var player))
            throw new ArgumentException("The requested player is not in this round.", nameof(playerId));

        var ownHand = player.Hand.Select((slot, handIndex) => new CardSlotView(
            handIndex,
            slot.Knowledge,
            slot.Knowledge == CardKnowledge.KnownToOwner ? ToCardView(slot.Card) : null)).ToArray();
        var round = _state.Round;
        var pendingDrawnCard = string.Equals(
            round.Players[round.Turn.ActiveSeatIndex].PlayerId,
            playerId,
            StringComparison.Ordinal)
            ? round.Turn.PendingDrawnCard
            : null;
        var isActivePlayer = string.Equals(
            round.Players[round.Turn.ActiveSeatIndex].PlayerId,
            playerId,
            StringComparison.Ordinal);

        return new PlayerGameView(
            GetPublicView(),
            Array.AsReadOnly(ownHand),
            pendingDrawnCard is null ? null : ToCardView(pendingDrawnCard),
            GetAvailableDrawResolutions(playerId, isActivePlayer),
            isActivePlayer ? round.Turn.AbilityDeadlineUtc : null,
            isActivePlayer
                && round.CallerId is null
                && round.Phase == GamePhase.AwaitingPostResolutionDecision,
            isActivePlayer && round.Phase == GamePhase.AwaitingPostResolutionDecision);
    }

    private TransitionResult Peek(string playerId, PeekInitialCards action, DateTimeOffset occurredAtUtc)
    {
        var round = _state.Round;
        if (round.Phase != GamePhase.WaitingForInitialPeeks)
            return Rejected(GameErrorCode.InvalidPhase, occurredAtUtc);

        var player = _playersById[playerId];
        if (player.InitialPeekCompleted)
            return Rejected(GameErrorCode.InitialPeekAlreadyCompleted, occurredAtUtc);
        if (action.FirstHandIndex == action.SecondHandIndex
            || action.FirstHandIndex < 0
            || action.SecondHandIndex < 0
            || action.FirstHandIndex >= player.Hand.Count
            || action.SecondHandIndex >= player.Hand.Count)
            return Rejected(GameErrorCode.InvalidPeekSelection, occurredAtUtc);

        player.Hand[action.FirstHandIndex].Knowledge = CardKnowledge.KnownToOwner;
        player.Hand[action.SecondHandIndex].Knowledge = CardKnowledge.KnownToOwner;
        player.InitialPeekCompleted = true;

        var events = new List<GameEvent>
        {
            new(GameEventType.InitialCardsPeeked, playerId)
        };
        if (round.Players.All(candidate => candidate.InitialPeekCompleted))
        {
            round.Phase = GamePhase.AwaitingDraw;
            events.Add(new GameEvent(GameEventType.TurnStarted, round.Players[round.Turn.ActiveSeatIndex].PlayerId));
        }

        ValidateInvariants();
        return Accepted(occurredAtUtc, events);
    }

    private TransitionResult Draw(string playerId, DateTimeOffset occurredAtUtc)
    {
        var round = _state.Round;
        if (round.Phase != GamePhase.AwaitingDraw
            && !(round.IsFinalTurnMode && round.Phase == GamePhase.FinalPhase))
            return Rejected(GameErrorCode.InvalidPhase, occurredAtUtc);
        if (!IsActivePlayer(playerId))
            return Rejected(GameErrorCode.NotActivePlayer, occurredAtUtc);
        if (round.DrawPile.Count == 0)
            return Rejected(GameErrorCode.DrawPileEmpty, occurredAtUtc);

        round.Turn.PendingDrawnCard = round.DrawPile.Draw();
        round.Phase = GamePhase.AwaitingDrawResolution;
        ValidateInvariants();
        return Accepted(occurredAtUtc, new[] { new GameEvent(GameEventType.CardDrawn, playerId) });
    }

    private TransitionResult DiscardDrawn(string playerId, DateTimeOffset occurredAtUtc)
    {
        var round = _state.Round;
        if (round.Phase != GamePhase.AwaitingDrawResolution)
            return Rejected(GameErrorCode.InvalidPhase, occurredAtUtc);
        if (!IsActivePlayer(playerId))
            return Rejected(GameErrorCode.NotActivePlayer, occurredAtUtc);

        round.DiscardPile.Add(round.Turn.PendingDrawnCard!);
        round.Turn.PendingDrawnCard = null;
        round.Phase = GamePhase.AwaitingPostResolutionDecision;
        ValidateInvariants();
        return Accepted(occurredAtUtc, new[] { new GameEvent(GameEventType.DrawnCardDiscarded, playerId) });
    }

    private TransitionResult Replace(string playerId, int handIndex, DateTimeOffset occurredAtUtc)
    {
        var round = _state.Round;
        if (round.Phase != GamePhase.AwaitingDrawResolution)
            return Rejected(GameErrorCode.InvalidPhase, occurredAtUtc);
        if (!IsActivePlayer(playerId))
            return Rejected(GameErrorCode.NotActivePlayer, occurredAtUtc);

        var hand = _playersById[playerId].Hand;
        if (handIndex < 0 || handIndex >= hand.Count)
            return Rejected(GameErrorCode.InvalidHandIndex, occurredAtUtc);

        var previousCard = hand[handIndex].Card;
        hand[handIndex].Card = round.Turn.PendingDrawnCard!;
        hand[handIndex].Knowledge = CardKnowledge.KnownToOwner;
        round.DiscardPile.Add(previousCard);
        round.Turn.PendingDrawnCard = null;
        round.Phase = GamePhase.AwaitingPostResolutionDecision;
        ValidateInvariants();
        return Accepted(occurredAtUtc, new[] { new GameEvent(GameEventType.HandCardReplaced, playerId, handIndex) });
    }

    private TransitionResult ActivateAbility(string playerId, DateTimeOffset occurredAtUtc)
    {
        var round = _state.Round;
        if (round.Phase != GamePhase.AwaitingDrawResolution)
            return Rejected(GameErrorCode.InvalidPhase, occurredAtUtc);
        if (!IsActivePlayer(playerId))
            return Rejected(GameErrorCode.NotActivePlayer, occurredAtUtc);

        var ability = GetAbility(round.Turn.PendingDrawnCard?.Rank);
        if (ability is null)
            return Rejected(GameErrorCode.InvalidAbilityCard, occurredAtUtc);
        if (!GetResolutionOptions(playerId, round.Turn.PendingDrawnCard!.Rank)
            .Contains(DrawResolutionOption.ActivateAbility))
            return Rejected(GameErrorCode.AbilityUnavailable, occurredAtUtc);

        round.Turn.PendingAbility = ability;
        round.Turn.AbilityDeadlineUtc = occurredAtUtc.AddSeconds(30);
        round.Phase = GamePhase.AwaitingAbilityResolution;
        ValidateInvariants();
        return Accepted(occurredAtUtc, new[] { new GameEvent(GameEventType.AbilityWindowOpened, playerId) });
    }

    private TransitionResult ResolveJack(string playerId, int handIndex, DateTimeOffset occurredAtUtc)
    {
        var round = _state.Round;
        var validationError = ValidateAbilityResolution(playerId, SpecialAbility.Jack, occurredAtUtc);
        if (validationError is not null)
            return validationError;

        var hand = _playersById[playerId].Hand;
        if (handIndex < 0 || handIndex >= hand.Count)
            return Rejected(GameErrorCode.InvalidHandIndex, occurredAtUtc);

        hand[handIndex].Knowledge = CardKnowledge.KnownToOwner;
        DiscardAbilityCard(round);
        round.Phase = GamePhase.AwaitingPostResolutionDecision;
        ValidateInvariants();
        return Accepted(occurredAtUtc, new[] { new GameEvent(GameEventType.AbilityResolved, playerId) });
    }

    private TransitionResult ResolveQueen(string playerId, int handIndex, DateTimeOffset occurredAtUtc)
    {
        var round = _state.Round;
        var validationError = ValidateAbilityResolution(playerId, SpecialAbility.Queen, occurredAtUtc);
        if (validationError is not null)
            return validationError;

        var hand = _playersById[playerId].Hand;
        if (handIndex < 0 || handIndex >= hand.Count)
            return Rejected(GameErrorCode.InvalidHandIndex, occurredAtUtc);

        var selectedCard = hand[handIndex].Card;
        hand.RemoveAt(handIndex);
        var queen = DiscardAbilityCard(round);
        round.DiscardPile.Add(selectedCard);

        var events = new List<GameEvent>
        {
            new(GameEventType.AbilityResolved, playerId)
        };
        if (hand.Count == 0)
        {
            var isFirstCaller = round.CallerId is null;
            if (isFirstCaller)
            {
                round.CallerId = playerId;
                RecordRequiredFinalTurns(round, playerId);
                round.IsFinalTurnMode = true;
                round.PendingFinalTurnPlayerIds.AddRange(round.RequiredFinalTurnPlayerIds);
            }
            round.QueenReactionTargets = null;
            ClearReactionWindow(round);
            if (isFirstCaller)
            {
                events.Add(new GameEvent(GameEventType.AlzheimerCalled, playerId));
                events.AddRange(BeginFinalTurns());
            }
            else
            {
                round.Phase = GamePhase.AwaitingPostResolutionDecision;
            }
        }
        else
        {
            round.QueenReactionTargets = new QueenReactionTargets(queen, selectedCard);
            round.Phase = GamePhase.AwaitingPostResolutionDecision;
        }

        ValidateInvariants();
        return Accepted(occurredAtUtc, events);
    }

    private TransitionResult ResolveKing(
        string playerId,
        string targetPlayerId,
        int ownHandIndex,
        int targetHandIndex,
        DateTimeOffset occurredAtUtc)
    {
        var round = _state.Round;
        var validationError = ValidateAbilityResolution(playerId, SpecialAbility.King, occurredAtUtc);
        if (validationError is not null)
            return validationError;
        if (string.IsNullOrWhiteSpace(targetPlayerId)
            || string.Equals(playerId, targetPlayerId, StringComparison.Ordinal)
            || !_playersById.TryGetValue(targetPlayerId, out var targetPlayer))
            return Rejected(GameErrorCode.InvalidAbilityTarget, occurredAtUtc);

        var ownHand = _playersById[playerId].Hand;
        var targetHand = targetPlayer.Hand;
        if (ownHandIndex < 0 || ownHandIndex >= ownHand.Count
            || targetHandIndex < 0 || targetHandIndex >= targetHand.Count)
            return Rejected(GameErrorCode.InvalidHandIndex, occurredAtUtc);

        (ownHand[ownHandIndex].Card, targetHand[targetHandIndex].Card) =
            (targetHand[targetHandIndex].Card, ownHand[ownHandIndex].Card);
        ownHand[ownHandIndex].Knowledge = CardKnowledge.Unknown;
        targetHand[targetHandIndex].Knowledge = CardKnowledge.Unknown;
        DiscardAbilityCard(round);
        round.Phase = GamePhase.AwaitingPostResolutionDecision;
        ValidateInvariants();
        return Accepted(occurredAtUtc, new[] { new GameEvent(GameEventType.AbilityResolved, playerId) });
    }

    private TransitionResult? ValidateAbilityResolution(
        string playerId,
        SpecialAbility expectedAbility,
        DateTimeOffset occurredAtUtc)
    {
        var round = _state.Round;
        if (round.Phase != GamePhase.AwaitingAbilityResolution)
            return Rejected(GameErrorCode.InvalidPhase, occurredAtUtc);
        if (!IsActivePlayer(playerId))
            return Rejected(GameErrorCode.NotActivePlayer, occurredAtUtc);
        if (round.Turn.PendingAbility != expectedAbility)
            return Rejected(GameErrorCode.InvalidAbilityType, occurredAtUtc);
        return null;
    }

    private IReadOnlyList<DrawResolutionOption> GetAvailableDrawResolutions(string playerId, bool isActivePlayer)
    {
        var round = _state.Round;
        if (!isActivePlayer
            || round.Phase != GamePhase.AwaitingDrawResolution
            || round.Turn.PendingDrawnCard is null)
            return Array.Empty<DrawResolutionOption>();

        return GetResolutionOptions(playerId, round.Turn.PendingDrawnCard.Rank);
    }

    private IReadOnlyList<DrawResolutionOption> GetResolutionOptions(string playerId, CardRank drawnRank)
    {
        var ownHandCount = _playersById[playerId].Hand.Count;
        var opponentsWithCards = _state.Round.Players
            .Where(player => !string.Equals(player.PlayerId, playerId, StringComparison.Ordinal))
            .Sum(player => player.Hand.Count);
        return DrawResolutionRules.GetAvailableOptions(drawnRank, ownHandCount, opponentsWithCards);
    }

    private static SpecialAbility? GetAbility(CardRank? rank) => rank switch
    {
        CardRank.Jack => SpecialAbility.Jack,
        CardRank.Queen => SpecialAbility.Queen,
        CardRank.King => SpecialAbility.King,
        _ => null
    };

    private static PlayingCard DiscardAbilityCard(RoundState round)
    {
        var card = round.Turn.PendingDrawnCard
            ?? throw new InvalidOperationException("An ability must have a pending drawn card.");
        round.DiscardPile.Add(card);
        round.Turn.PendingDrawnCard = null;
        round.Turn.PendingAbility = null;
        round.Turn.AbilityDeadlineUtc = null;
        return card;
    }

    private List<GameEvent> ProcessAbilityExpiration(DateTimeOffset occurredAtUtc)
    {
        var round = _state.Round;
        if (round.Phase != GamePhase.AwaitingAbilityResolution
            || round.Turn.AbilityDeadlineUtc is not { } deadline
            || occurredAtUtc < deadline)
            return new List<GameEvent>();

        DiscardAbilityCard(round);
        round.Phase = GamePhase.AwaitingPostResolutionDecision;
        ValidateInvariants();
        return new List<GameEvent> { new(GameEventType.AbilityExpired, round.Players[round.Turn.ActiveSeatIndex].PlayerId) };
    }

    private TransitionResult Call(string playerId, DateTimeOffset occurredAtUtc)
    {
        var round = _state.Round;
        if (round.CallerId is not null)
            return Rejected(GameErrorCode.CallerAlreadyRecorded, occurredAtUtc);
        if (round.Phase != GamePhase.AwaitingPostResolutionDecision)
            return Rejected(GameErrorCode.InvalidPhase, occurredAtUtc);
        if (!IsActivePlayer(playerId))
            return Rejected(GameErrorCode.NotActivePlayer, occurredAtUtc);

        round.CallerId = playerId;
        RecordRequiredFinalTurns(round, playerId);
        round.IsFinalTurnMode = true;
        round.PendingFinalTurnPlayerIds.AddRange(round.RequiredFinalTurnPlayerIds);
        round.QueenReactionTargets = null;
        var events = new List<GameEvent> { new(GameEventType.AlzheimerCalled, playerId) };
        events.AddRange(BeginFinalTurns());
        ValidateInvariants();
        return Accepted(occurredAtUtc, events);
    }

    private TransitionResult End(string playerId, DateTimeOffset occurredAtUtc)
    {
        var round = _state.Round;
        if (round.Phase != GamePhase.AwaitingPostResolutionDecision)
            return Rejected(GameErrorCode.InvalidPhase, occurredAtUtc);
        if (!IsActivePlayer(playerId))
            return Rejected(GameErrorCode.NotActivePlayer, occurredAtUtc);

        if (round.IsFinalTurnMode)
        {
            var events = new List<GameEvent> { new(GameEventType.TurnEnded, playerId) };
            FinishFinalTurn(events);
            ValidateInvariants();
            return Accepted(occurredAtUtc, events);
        }

        _state.ReactionWindowSequence++;
        round.ReactionWindowId = _state.ReactionWindowSequence;
        round.ReactionDeadlineUtc = occurredAtUtc.AddSeconds(30);
        round.ReactionDonePlayerIds.Clear();
        round.ProcessedReactionCommandIds.Clear();
        round.Phase = GamePhase.AwaitingReactionResolution;
        ValidateInvariants();
        return Accepted(occurredAtUtc, new[]
        {
            new GameEvent(GameEventType.TurnEnded, playerId),
            new GameEvent(GameEventType.ReactionWindowStarted, playerId)
        });
    }

    private TransitionResult ThrowReaction(
        string playerId,
        ThrowReactionCard action,
        DateTimeOffset occurredAtUtc)
    {
        var round = _state.Round;
        var validationError = ValidateReactionCommand(
            playerId,
            action.ReactionWindowId,
            action.CommandId,
            occurredAtUtc);
        if (validationError is not null)
            return validationError;

        var player = _playersById[playerId];
        if (action.HandIndex < 0 || action.HandIndex >= player.Hand.Count)
            return Rejected(GameErrorCode.InvalidHandIndex, occurredAtUtc);

        var card = player.Hand[action.HandIndex].Card;
        var discardTop = round.DiscardPile.Top;
        if (discardTop is null)
            return Rejected(GameErrorCode.InvalidPhase, occurredAtUtc);

        var matches = round.QueenReactionTargets is { } queenTargets
            ? ReactionRules.MatchesQueenWindow(card.Rank, queenTargets.SelectedCard.Rank)
            : ReactionRules.MatchesTop(card.Rank, discardTop.Rank);

        RememberReactionCommand(playerId, action.CommandId);
        var events = new List<GameEvent>();
        if (matches)
        {
            player.Hand.RemoveAt(action.HandIndex);
            round.DiscardPile.Add(card);
            events.Add(new GameEvent(GameEventType.ReactionCardThrown, playerId, action.HandIndex));

            if (player.Hand.Count == 0 && round.CallerId is null)
            {
                round.CallerId = playerId;
                RecordRequiredFinalTurns(round, playerId);
                round.IsFinalTurnMode = true;
                round.PendingFinalTurnPlayerIds.AddRange(round.RequiredFinalTurnPlayerIds);
                events.Add(new GameEvent(GameEventType.AlzheimerCalled, playerId));
                events.Add(new GameEvent(GameEventType.ReactionWindowCompleted,
                    round.Players[round.Turn.ActiveSeatIndex].PlayerId));
                CloseReactionWindow(round, restoreQueenTop: true);
                events.AddRange(BeginFinalTurns());
            }
        }
        else
        {
            player.Hand[action.HandIndex].Knowledge = CardKnowledge.KnownToOwner;
            if (round.DrawPile.Count == 0)
            {
                player.UndrawnPenaltyPoints += 10;
            }
            else
            {
                player.Hand.Add(new CardSlot(round.DrawPile.Draw(), CardKnowledge.Unknown));
            }

            events.Add(new GameEvent(GameEventType.IncorrectReactionThrow, playerId, action.HandIndex));
        }

        ValidateInvariants();
        return Accepted(occurredAtUtc, events);
    }

    private TransitionResult MarkDone(
        string playerId,
        MarkReactionDone action,
        DateTimeOffset occurredAtUtc)
    {
        var validationError = ValidateReactionCommand(
            playerId,
            action.ReactionWindowId,
            action.CommandId,
            occurredAtUtc);
        if (validationError is not null)
            return validationError;

        RememberReactionCommand(playerId, action.CommandId);
        var round = _state.Round;
        round.ReactionDonePlayerIds.Add(playerId);
        var events = new List<GameEvent> { new(GameEventType.ReactionDone, playerId) };
        if (round.Players.All(player => round.ReactionDonePlayerIds.Contains(player.PlayerId)))
            events.AddRange(CompleteReactionWindowCore());

        ValidateInvariants();
        return Accepted(occurredAtUtc, events);
    }

    private TransitionResult? ValidateReactionCommand(
        string playerId,
        long reactionWindowId,
        Guid commandId,
        DateTimeOffset occurredAtUtc)
    {
        var round = _state.Round;
        if (round.Phase != GamePhase.AwaitingReactionResolution)
            return Rejected(GameErrorCode.InvalidPhase, occurredAtUtc);
        if (round.ReactionWindowId != reactionWindowId)
            return Rejected(GameErrorCode.StaleReactionWindow, occurredAtUtc);
        if (commandId == Guid.Empty)
            return Rejected(GameErrorCode.InvalidCommandId, occurredAtUtc);
        if (round.ProcessedReactionCommandIds.TryGetValue(playerId, out var commandIds)
            && commandIds.Contains(commandId))
            return Rejected(GameErrorCode.DuplicateCommand, occurredAtUtc);
        if (round.ReactionDonePlayerIds.Contains(playerId))
            return Rejected(GameErrorCode.ReactionAlreadyDone, occurredAtUtc);
        return null;
    }

    private void RememberReactionCommand(string playerId, Guid commandId)
    {
        var round = _state.Round;
        if (!round.ProcessedReactionCommandIds.TryGetValue(playerId, out var commandIds))
        {
            commandIds = new HashSet<Guid>();
            round.ProcessedReactionCommandIds.Add(playerId, commandIds);
        }
        commandIds.Add(commandId);
    }

    private List<GameEvent> ProcessReactionExpiration(DateTimeOffset occurredAtUtc)
    {
        var round = _state.Round;
        if (round.Phase != GamePhase.AwaitingReactionResolution
            || !IsReactionDeadlineReached(occurredAtUtc))
            return new List<GameEvent>();

        return CompleteReactionWindowCore();
    }

    private bool IsReactionDeadlineReached(DateTimeOffset occurredAtUtc) =>
        _state.Round.ReactionDeadlineUtc is { } deadline && occurredAtUtc >= deadline;

    private List<GameEvent> CompleteReactionWindowCore()
    {
        var round = _state.Round;
        var activePlayerId = round.Players[round.Turn.ActiveSeatIndex].PlayerId;
        var events = new List<GameEvent>
        {
            new(GameEventType.ReactionWindowCompleted, activePlayerId)
        };

        CloseReactionWindow(round, restoreQueenTop: true);
        if (round.DrawPile.Count == 0)
        {
            events.AddRange(CompleteRound(RoundCompletionReason.DrawPileExhausted));
        }
        else
        {
            round.Turn.ActiveSeatIndex = (round.Turn.ActiveSeatIndex + 1) % round.Players.Count;
            round.Phase = GamePhase.AwaitingDraw;
            events.Add(new GameEvent(GameEventType.TurnStarted, round.Players[round.Turn.ActiveSeatIndex].PlayerId));
        }

        ValidateInvariants();
        return events;
    }

    private static void CloseReactionWindow(RoundState round, bool restoreQueenTop)
    {
        if (restoreQueenTop && round.QueenReactionTargets is { } queenTargets)
            round.DiscardPile.MakeTop(queenTargets.SelectedCard);

        round.QueenReactionTargets = null;
        round.ReactionWindowId = null;
        round.ReactionDeadlineUtc = null;
        round.ReactionDonePlayerIds.Clear();
        round.ProcessedReactionCommandIds.Clear();
    }

    private static void ClearReactionWindow(RoundState round)
    {
        round.ReactionWindowId = null;
        round.ReactionDeadlineUtc = null;
        round.ReactionDonePlayerIds.Clear();
        round.ProcessedReactionCommandIds.Clear();
    }

    private static void RecordRequiredFinalTurns(RoundState round, string callerId)
    {
        var finalTurns = new List<string>(round.Players.Count - 1);
        for (var offset = 1; offset <= round.Players.Count; offset++)
        {
            var playerId = round.Players[(round.Turn.ActiveSeatIndex + offset) % round.Players.Count].PlayerId;
            if (!string.Equals(playerId, callerId, StringComparison.Ordinal))
                finalTurns.Add(playerId);
        }
        round.RequiredFinalTurnPlayerIds = Array.AsReadOnly(finalTurns.ToArray());
    }

    private List<GameEvent> BeginFinalTurns()
    {
        var round = _state.Round;
        if (round.DrawPile.Count == 0)
            return CompleteRound(RoundCompletionReason.DrawPileExhausted);
        if (round.PendingFinalTurnPlayerIds.Count == 0)
            return CompleteRound(RoundCompletionReason.FinalTurnsCompleted);

        round.Turn.ActiveSeatIndex = round.Players.FindIndex(
            player => player.PlayerId == round.PendingFinalTurnPlayerIds[0]);
        round.Phase = GamePhase.FinalPhase;
        return new List<GameEvent>
        {
            new(GameEventType.TurnStarted, round.PendingFinalTurnPlayerIds[0])
        };
    }

    private void FinishFinalTurn(List<GameEvent> events)
    {
        var round = _state.Round;
        if (round.PendingFinalTurnPlayerIds.Count == 0
            || round.PendingFinalTurnPlayerIds[0] != round.Players[round.Turn.ActiveSeatIndex].PlayerId)
            throw new InvalidOperationException("The active final turn is not the next required player.");

        if (round.DrawPile.Count == 0)
        {
            events.AddRange(CompleteRound(RoundCompletionReason.DrawPileExhausted));
            return;
        }

        round.PendingFinalTurnPlayerIds.RemoveAt(0);
        if (round.PendingFinalTurnPlayerIds.Count == 0)
        {
            events.AddRange(CompleteRound(RoundCompletionReason.FinalTurnsCompleted));
            return;
        }

        round.Turn.ActiveSeatIndex = round.Players.FindIndex(
            player => player.PlayerId == round.PendingFinalTurnPlayerIds[0]);
        round.Phase = GamePhase.FinalPhase;
        events.Add(new GameEvent(GameEventType.TurnStarted, round.PendingFinalTurnPlayerIds[0]));
    }

    private List<GameEvent> CompleteRound(RoundCompletionReason reason)
    {
        var round = _state.Round;
        if (round.ScoreApplied)
            return new List<GameEvent>();

        var baseScores = round.Players
            .Select(player => new PlayerRoundBase(
                player.PlayerId,
                player.Hand.Sum(slot => slot.Card.Points),
                player.UndrawnPenaltyPoints))
            .ToArray();
        var adjustedScores = RoundScorer.Calculate(baseScores, round.CallerId);

        foreach (var player in round.Players)
            _state.CumulativeScores[player.PlayerId] = checked(
                _state.CumulativeScores[player.PlayerId] + adjustedScores[player.PlayerId]);

        var scoreViews = round.Players.Select(player => new PlayerRoundScoreView(
            player.PlayerId,
            baseScores.Single(score => score.PlayerId == player.PlayerId).BasePoints,
            adjustedScores[player.PlayerId],
            _state.CumulativeScores[player.PlayerId])).ToArray();
        var revealedHands = round.Players.Select(player => new RoundEndHandView(
            player.PlayerId,
            Array.AsReadOnly(player.Hand.Select(slot => ToCardView(slot.Card)).ToArray()))).ToArray();

        _state.LastCompletedRound = new RoundCompletionView(
            round.RoundNumber,
            reason,
            round.CallerId,
            Array.AsReadOnly(scoreViews),
            Array.AsReadOnly(revealedHands),
            Array.AsReadOnly(round.RequiredFinalTurnPlayerIds.ToArray()),
            Array.AsReadOnly(round.PendingFinalTurnPlayerIds.ToArray()));
        round.ScoreApplied = true;
        round.Phase = GamePhase.RoundCompleted;

        var events = new List<GameEvent> { new(GameEventType.RoundCompleted) };
        if (RoundScorer.IsMatchOver(_state.CumulativeScores.Values))
        {
            _state.MatchCompleted = true;
            _state.WinnerPlayerIds = RoundScorer.GetMatchWinners(_state.CumulativeScores);
            events.Add(new GameEvent(GameEventType.MatchCompleted));
            return events;
        }

        _state.Round = CreateRound(
            _state.Players,
            round.HostPlayerId,
            _state.Settings,
            _random,
            _physicalCardIdFactory,
            round.RoundNumber + 1);
        _playersById = _state.Round.Players.ToDictionary(player => player.PlayerId, StringComparer.Ordinal);
        events.Add(new GameEvent(GameEventType.RoundStarted, _state.Round.HostPlayerId));
        ValidateInvariants();
        return events;
    }

    private bool IsActivePlayer(string playerId) =>
        string.Equals(
            _state.Round.Players[_state.Round.Turn.ActiveSeatIndex].PlayerId,
            playerId,
            StringComparison.Ordinal);

    private void ValidateInvariants()
    {
        var round = _state.Round;
        var cardIds = round.Players
            .SelectMany(player => player.Hand)
            .Select(slot => slot.Card)
            .Concat(round.DrawPile.Cards)
            .Concat(round.DiscardPile.Cards)
            .Concat(round.Turn.PendingDrawnCard is null
                ? Array.Empty<PlayingCard>()
                : new[] { round.Turn.PendingDrawnCard })
            .Select(card => card.PhysicalCardId)
            .ToArray();
        var expectedCount = round.DeckCount * 54;
        if (cardIds.Length != expectedCount || cardIds.Distinct().Count() != expectedCount)
            throw new InvalidOperationException("The round state violated card conservation or physical ID uniqueness.");
        if (round.Players.Count is < 2 or > 10
            || round.Players[round.Turn.ActiveSeatIndex].PlayerId != round.HostPlayerId
                && round.Phase == GamePhase.WaitingForInitialPeeks)
            throw new InvalidOperationException("The round state violated player or host-turn invariants.");
        var abilityPhase = round.Phase == GamePhase.AwaitingAbilityResolution;
        if (abilityPhase != (round.Turn.PendingAbility is not null
                && round.Turn.PendingDrawnCard is not null
                && round.Turn.AbilityDeadlineUtc is not null)
            || (!abilityPhase
                && (round.Turn.PendingAbility is not null || round.Turn.AbilityDeadlineUtc is not null)))
            throw new InvalidOperationException("The round state violated ability-window invariants.");
        var reactionPhase = round.Phase == GamePhase.AwaitingReactionResolution;
        if (reactionPhase != (round.ReactionWindowId is not null && round.ReactionDeadlineUtc is not null)
            || (!reactionPhase
                && (round.ReactionWindowId is not null
                    || round.ReactionDeadlineUtc is not null
                    || round.ReactionDonePlayerIds.Count != 0
                    || round.ProcessedReactionCommandIds.Count != 0)))
            throw new InvalidOperationException("The round state violated reaction-window invariants.");
        if (round.ReactionDonePlayerIds.Any(playerId => !_playersById.ContainsKey(playerId))
            || round.RequiredFinalTurnPlayerIds.Distinct(StringComparer.Ordinal).Count()
                != round.RequiredFinalTurnPlayerIds.Count
            || round.RequiredFinalTurnPlayerIds.Any(playerId =>
                !_playersById.ContainsKey(playerId)
                || string.Equals(playerId, round.CallerId, StringComparison.Ordinal))
            || round.PendingFinalTurnPlayerIds.Distinct(StringComparer.Ordinal).Count()
                != round.PendingFinalTurnPlayerIds.Count
            || round.PendingFinalTurnPlayerIds.Any(playerId =>
                !round.RequiredFinalTurnPlayerIds.Contains(playerId, StringComparer.Ordinal))
            || (round.IsFinalTurnMode && round.CallerId is null)
            || (!round.IsFinalTurnMode && round.PendingFinalTurnPlayerIds.Count != 0)
            || (round.IsFinalTurnMode
                && round.Phase != GamePhase.RoundCompleted
                && round.PendingFinalTurnPlayerIds.Count != 0
                && round.Players[round.Turn.ActiveSeatIndex].PlayerId != round.PendingFinalTurnPlayerIds[0])
            || (round.CallerId is not null && reactionPhase))
            throw new InvalidOperationException("The round state violated reaction or final-turn identity invariants.");
    }

    private static CardView ToCardView(PlayingCard card) =>
        new(card.PhysicalCardId, card.Rank, card.Suit);

    private static TransitionResult Accepted(DateTimeOffset occurredAtUtc, IEnumerable<GameEvent> events) =>
        new(true, null, occurredAtUtc, Array.AsReadOnly(events.ToArray()));

    private static TransitionResult IncludeEvents(TransitionResult result, IEnumerable<GameEvent> precedingEvents) =>
        new(
            result.Accepted,
            result.ErrorCode,
            result.OccurredAtUtc,
            Array.AsReadOnly(precedingEvents.Concat(result.Events).ToArray()));

    private static TransitionResult Rejected(
        GameErrorCode errorCode,
        DateTimeOffset occurredAtUtc,
        IEnumerable<GameEvent>? events = null) =>
        new(false, errorCode, occurredAtUtc, Array.AsReadOnly(events?.ToArray() ?? Array.Empty<GameEvent>()));
}

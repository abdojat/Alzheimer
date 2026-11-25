namespace Alzheimer.Core;

public sealed class AlzheimerRoundEngine
{
    private readonly MatchState _state;
    private readonly IGameClock _clock;
    private readonly Dictionary<string, PlayerState> _playersById;

    private AlzheimerRoundEngine(MatchState state, IGameClock clock)
    {
        _state = state;
        _clock = clock;
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

        var orderedPlayers = Enumerable.Range(0, players.Count)
            .Select(offset => players[(hostIndex + offset) % players.Count])
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
            Phase = GamePhase.WaitingForInitialPeeks
        };
        var match = new MatchState
        {
            CumulativeScores = orderedPlayers.ToDictionary(player => player.PlayerId, _ => 0, StringComparer.Ordinal),
            Round = round
        };

        return new AlzheimerRoundEngine(match, clock ?? new SystemGameClock());
    }

    public TransitionResult ProcessAction(string? playerId, GameAction? action)
    {
        var occurredAtUtc = _clock.UtcNow.ToUniversalTime();
        var timeoutEvents = ProcessAbilityExpiration(occurredAtUtc);

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
        return events.Count == 0
            ? Rejected(GameErrorCode.AbilityDeadlineNotReached, occurredAtUtc)
            : Accepted(occurredAtUtc, events);
    }

    public TransitionResult CompleteReactionWindow()
    {
        var occurredAtUtc = _clock.UtcNow.ToUniversalTime();
        if (_state.Round.Phase != GamePhase.AwaitingReactionResolution)
            return Rejected(GameErrorCode.InvalidPhase, occurredAtUtc);

        var round = _state.Round;
        var events = new List<GameEvent>
        {
            new(GameEventType.ReactionWindowCompleted, round.Players[round.Turn.ActiveSeatIndex].PlayerId)
        };

        if (round.QueenReactionTargets is { } queenTargets)
        {
            round.DiscardPile.MakeTop(queenTargets.SelectedCard);
            round.QueenReactionTargets = null;
        }

        if (round.DrawPile.Count == 0)
        {
            round.Phase = GamePhase.RoundCompleted;
            events.Add(new GameEvent(GameEventType.RoundCompleted));
        }
        else
        {
            round.Turn.ActiveSeatIndex = (round.Turn.ActiveSeatIndex + 1) % round.Players.Count;
            round.Phase = GamePhase.AwaitingDraw;
            events.Add(new GameEvent(GameEventType.TurnStarted, round.Players[round.Turn.ActiveSeatIndex].PlayerId));
        }

        ValidateInvariants();
        return Accepted(occurredAtUtc, events);
    }

    public PublicGameView GetPublicView()
    {
        var round = _state.Round;
        var players = round.Players.Select(player => new PublicPlayerView(
            player.PlayerId,
            player.DisplayName,
            player.Hand.Count,
            _state.CumulativeScores[player.PlayerId])).ToArray();
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
                : null);
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
            isActivePlayer ? round.Turn.AbilityDeadlineUtc : null);
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
        if (round.Phase != GamePhase.AwaitingDraw)
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
            round.CallerId ??= playerId;
            round.QueenReactionTargets = null;
            round.Phase = GamePhase.FinalPhase;
            if (isFirstCaller)
                events.Add(new GameEvent(GameEventType.AlzheimerCalled, playerId));
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
        round.QueenReactionTargets = null;
        round.Phase = GamePhase.FinalPhase;
        ValidateInvariants();
        return Accepted(occurredAtUtc, new[] { new GameEvent(GameEventType.AlzheimerCalled, playerId) });
    }

    private TransitionResult End(string playerId, DateTimeOffset occurredAtUtc)
    {
        var round = _state.Round;
        if (round.Phase != GamePhase.AwaitingPostResolutionDecision)
            return Rejected(GameErrorCode.InvalidPhase, occurredAtUtc);
        if (!IsActivePlayer(playerId))
            return Rejected(GameErrorCode.NotActivePlayer, occurredAtUtc);

        round.Phase = GamePhase.AwaitingReactionResolution;
        ValidateInvariants();
        return Accepted(occurredAtUtc, new[] { new GameEvent(GameEventType.TurnEnded, playerId) });
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

namespace Alzheimer.Core;

internal sealed class MatchState
{
    public required Dictionary<string, int> CumulativeScores { get; init; }
    public required RoundState Round { get; init; }
}

internal sealed class RoundState
{
    public required int DeckCount { get; init; }
    public required string HostPlayerId { get; init; }
    public required List<PlayerState> Players { get; init; }
    public required DrawPile DrawPile { get; init; }
    public required DiscardPile DiscardPile { get; init; }
    public required TurnState Turn { get; init; }
    public GamePhase Phase { get; set; }
    public string? CallerId { get; set; }
    public QueenReactionTargets? QueenReactionTargets { get; set; }
    public long ReactionWindowSequence { get; set; }
    public long? ReactionWindowId { get; set; }
    public DateTimeOffset? ReactionDeadlineUtc { get; set; }
    public HashSet<string> ReactionDonePlayerIds { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, HashSet<Guid>> ProcessedReactionCommandIds { get; } = new(StringComparer.Ordinal);
    public IReadOnlyList<string> RequiredFinalTurnPlayerIds { get; set; } = Array.Empty<string>();
}

internal sealed class PlayerState(string playerId, string displayName)
{
    public string PlayerId { get; } = playerId;
    public string DisplayName { get; } = displayName;
    public List<CardSlot> Hand { get; } = new();
    public int UndrawnPenaltyPoints { get; set; }
    public bool InitialPeekCompleted { get; set; }
}

internal sealed class CardSlot(PlayingCard card, CardKnowledge knowledge)
{
    public PlayingCard Card { get; set; } = card;
    public CardKnowledge Knowledge { get; set; } = knowledge;
}

internal sealed class DrawPile(IEnumerable<PlayingCard> cards)
{
    private readonly List<PlayingCard> _cards = cards.ToList();

    public int Count => _cards.Count;

    public PlayingCard Draw()
    {
        var index = _cards.Count - 1;
        var card = _cards[index];
        _cards.RemoveAt(index);
        return card;
    }

    public IEnumerable<PlayingCard> Cards => _cards;
}

internal sealed class DiscardPile
{
    private readonly List<PlayingCard> _cards = new();

    public int Count => _cards.Count;
    public PlayingCard? Top => _cards.Count == 0 ? null : _cards[^1];
    public IEnumerable<PlayingCard> Cards => _cards;

    public void Add(PlayingCard card) => _cards.Add(card);

    public void MakeTop(PlayingCard card)
    {
        var index = _cards.FindLastIndex(candidate => candidate.PhysicalCardId == card.PhysicalCardId);
        if (index < 0)
            throw new InvalidOperationException("The selected Queen card is not in the discard pile.");
        _cards.RemoveAt(index);
        _cards.Add(card);
    }
}

internal sealed class TurnState(int activeSeatIndex)
{
    public int ActiveSeatIndex { get; set; } = activeSeatIndex;
    public PlayingCard? PendingDrawnCard { get; set; }
    public SpecialAbility? PendingAbility { get; set; }
    public DateTimeOffset? AbilityDeadlineUtc { get; set; }
}

internal enum SpecialAbility
{
    Jack,
    Queen,
    King
}

internal sealed record QueenReactionTargets(PlayingCard Queen, PlayingCard SelectedCard);

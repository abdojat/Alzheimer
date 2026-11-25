namespace Alzheimer.Core;

public enum GamePhase
{
    WaitingForInitialPeeks,
    AwaitingDraw,
    AwaitingDrawResolution,
    AwaitingAbilityResolution,
    AwaitingPostResolutionDecision,
    AwaitingReactionResolution,
    FinalPhase,
    RoundCompleted
}

public enum CardKnowledge
{
    Unknown,
    KnownToOwner
}

public sealed record PlayerDefinition(string PlayerId, string DisplayName);

public abstract record GameAction;
public sealed record PeekInitialCards(int FirstHandIndex, int SecondHandIndex) : GameAction;
public sealed record DrawCard : GameAction;
public sealed record DiscardDrawnCard : GameAction;
public sealed record ReplaceHandCard(int HandIndex) : GameAction;
public sealed record CallAlzheimer : GameAction;
public sealed record EndTurn : GameAction;

public enum GameErrorCode
{
    InvalidPlayer,
    NotActivePlayer,
    InvalidPhase,
    InitialPeekAlreadyCompleted,
    InvalidPeekSelection,
    InvalidHandIndex,
    DrawPileEmpty,
    CallerAlreadyRecorded,
    InvalidAction
}

public enum GameEventType
{
    InitialCardsPeeked,
    TurnStarted,
    CardDrawn,
    DrawnCardDiscarded,
    HandCardReplaced,
    AlzheimerCalled,
    TurnEnded,
    ReactionWindowCompleted,
    RoundCompleted
}

public sealed record GameEvent(GameEventType Type, string? PlayerId = null, int? HandIndex = null);

public sealed record TransitionResult(
    bool Accepted,
    GameErrorCode? ErrorCode,
    DateTimeOffset OccurredAtUtc,
    IReadOnlyList<GameEvent> Events);

public sealed record CardView(Guid PhysicalCardId, CardRank Rank, CardSuit Suit);

public sealed record CardSlotView(int HandIndex, CardKnowledge Knowledge, CardView? KnownCard);

public sealed record PublicPlayerView(
    string PlayerId,
    string DisplayName,
    int HandCount,
    int CumulativeScore);

public sealed record PublicGameView(
    GamePhase Phase,
    string HostPlayerId,
    string ActivePlayerId,
    string? CallerId,
    int DeckCount,
    int DrawPileCount,
    IReadOnlyList<PublicPlayerView> Players,
    IReadOnlyList<CardView> DiscardPile);

public sealed record PlayerGameView(
    PublicGameView Public,
    IReadOnlyList<CardSlotView> OwnHand,
    CardView? PendingDrawnCard);

public interface IGameClock
{
    DateTimeOffset UtcNow { get; }
}

public sealed class SystemGameClock : IGameClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

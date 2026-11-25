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

public enum RoundCompletionReason
{
    FinalTurnsCompleted,
    DrawPileExhausted
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
public sealed record ActivateDrawnAbility : GameAction;
public sealed record ResolveJackAbility(int HandIndex) : GameAction;
public sealed record ResolveQueenAbility(int HandIndex) : GameAction;
public sealed record ResolveKingAbility(string TargetPlayerId, int OwnHandIndex, int TargetHandIndex) : GameAction;
public sealed record CallAlzheimer : GameAction;
public sealed record EndTurn : GameAction;
public sealed record ThrowReactionCard(int HandIndex, long ReactionWindowId, Guid CommandId) : GameAction;
public sealed record MarkReactionDone(long ReactionWindowId, Guid CommandId) : GameAction;

public enum DrawResolutionOption
{
    Discard,
    Replace,
    ActivateAbility
}

public static class DrawResolutionRules
{
    public static IReadOnlyList<DrawResolutionOption> GetAvailableOptions(
        CardRank drawnRank,
        int ownHandCount,
        int opponentsWithCards)
    {
        if (ownHandCount < 0)
            throw new ArgumentOutOfRangeException(nameof(ownHandCount));
        if (opponentsWithCards < 0)
            throw new ArgumentOutOfRangeException(nameof(opponentsWithCards));

        var options = new List<DrawResolutionOption> { DrawResolutionOption.Discard };
        if (ownHandCount > 0)
        {
            options.Add(DrawResolutionOption.Replace);
            if (drawnRank is CardRank.Jack or CardRank.Queen
                || drawnRank == CardRank.King && opponentsWithCards > 0)
                options.Add(DrawResolutionOption.ActivateAbility);
        }

        return Array.AsReadOnly(options.ToArray());
    }
}

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
    InvalidAbilityCard,
    AbilityUnavailable,
    InvalidAbilityType,
    InvalidAbilityTarget,
    AbilityDeadlineNotReached,
    AbilityWindowExpired,
    ReactionDeadlineNotReached,
    ReactionWindowExpired,
    StaleReactionWindow,
    ReactionAlreadyDone,
    DuplicateCommand,
    InvalidCommandId,
    InvalidAction
}

public enum GameEventType
{
    InitialCardsPeeked,
    TurnStarted,
    CardDrawn,
    DrawnCardDiscarded,
    HandCardReplaced,
    AbilityWindowOpened,
    AbilityResolved,
    AbilityExpired,
    ReactionWindowStarted,
    AlzheimerCalled,
    TurnEnded,
    ReactionCardThrown,
    IncorrectReactionThrow,
    ReactionDone,
    ReactionWindowCompleted,
    RoundCompleted,
    RoundStarted,
    MatchCompleted
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
    int CumulativeScore,
    int RoundPenaltyPoints);

public sealed record PublicGameView(
    GamePhase Phase,
    string HostPlayerId,
    string ActivePlayerId,
    string? CallerId,
    int DeckCount,
    int DrawPileCount,
    IReadOnlyList<PublicPlayerView> Players,
    IReadOnlyList<CardView> DiscardPile,
    QueenReactionTargetsView? QueenReactionTargets,
    long? ReactionWindowId,
    DateTimeOffset? ReactionDeadlineUtc,
    IReadOnlyList<string> ReactionDonePlayerIds,
    IReadOnlyList<string> RequiredFinalTurnPlayerIds,
    IReadOnlyList<string> PendingFinalTurnPlayerIds,
    bool IsFinalTurnMode,
    int RoundNumber,
    RoundCompletionView? LastCompletedRound,
    bool MatchCompleted,
    IReadOnlyList<string> WinnerPlayerIds);

public sealed record PlayerGameView(
    PublicGameView Public,
    IReadOnlyList<CardSlotView> OwnHand,
    CardView? PendingDrawnCard,
    IReadOnlyList<DrawResolutionOption> AvailableDrawResolutions,
    DateTimeOffset? AbilityDeadlineUtc,
    bool CanCallAlzheimer,
    bool CanEndTurn);

public sealed record PlayerRoundScoreView(
    string PlayerId,
    int BaseScore,
    int AdjustedRoundScore,
    int CumulativeScore);

public sealed record RoundEndHandView(string PlayerId, IReadOnlyList<CardView> Cards);

public sealed record RoundCompletionView(
    int RoundNumber,
    RoundCompletionReason Reason,
    string? CallerId,
    IReadOnlyList<PlayerRoundScoreView> Scores,
    IReadOnlyList<RoundEndHandView> RevealedHands,
    IReadOnlyList<string> RequiredFinalTurnPlayerIds,
    IReadOnlyList<string> PendingFinalTurnPlayerIds);

public sealed record QueenReactionTargetsView(
    CardRank QueenRank,
    CardRank SelectedCardRank,
    CardView FinalTopCard);

public interface IGameClock
{
    DateTimeOffset UtcNow { get; }
}

public sealed class SystemGameClock : IGameClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

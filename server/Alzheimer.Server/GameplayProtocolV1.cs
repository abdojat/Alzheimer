using Alzheimer.Core;

namespace Alzheimer.Server;

public enum GameplayActionTypeV1
{
    PeekInitialCards,
    DrawCard,
    DiscardDrawnCard,
    ReplaceHandCard,
    ActivateDrawnAbility,
    ResolveJackAbility,
    ResolveQueenAbility,
    ResolveKingAbility,
    CallAlzheimer,
    EndTurn,
    ThrowReactionCard,
    MarkReactionDone
}

public sealed record GameplayCommandV1(
    int ProtocolVersion,
    Guid CommandId,
    GameplayActionTypeV1 Action,
    int? FirstHandIndex = null,
    int? SecondHandIndex = null,
    int? HandIndex = null,
    string? TargetPlayerId = null,
    int? TargetHandIndex = null,
    long? ReactionWindowId = null);

public sealed record GamePublicUpdateV1(
    int ProtocolVersion,
    long Sequence,
    PublicGameView View,
    IReadOnlyList<GameEvent> Events);

public sealed record GamePrivateUpdateV1(int ProtocolVersion, long Sequence, PlayerGameView View);

public sealed record GameCommandResponseV1(
    int ProtocolVersion,
    long? StateSequence,
    Guid? CommandId,
    bool Accepted,
    string? ErrorCode,
    IReadOnlyList<GameEvent> Events,
    PublicGameView? PublicView,
    PlayerGameView? PrivateView);

public sealed record StartMatchResponseV1(
    int ProtocolVersion,
    long? StateSequence,
    bool Accepted,
    string? ErrorCode,
    PublicGameView? PublicView,
    PlayerGameView? PrivateView);

internal sealed record GameStateUpdateBundle(
    string RoomCode,
    GamePublicUpdateV1 PublicUpdate,
    IReadOnlyList<(string ConnectionId, GamePrivateUpdateV1 PrivateUpdate)> PrivateUpdates);

internal sealed record GameCommandOutcome(
    GameCommandResponseV1 Response,
    GameStateUpdateBundle? Update);

internal static class GameplayCommandMapperV1
{
    public static bool TryMap(GameplayCommandV1 command, out GameAction? action, out string? errorCode)
    {
        action = null;
        errorCode = null;

        if (command.ProtocolVersion != 1)
        {
            errorCode = "UnsupportedProtocolVersion";
            return false;
        }
        if (command.CommandId == Guid.Empty)
        {
            errorCode = "InvalidCommandId";
            return false;
        }
        if (!Enum.IsDefined(command.Action))
        {
            errorCode = "InvalidCommandType";
            return false;
        }

        switch (command.Action)
        {
            case GameplayActionTypeV1.PeekInitialCards when HasOnly(command, nameof(command.FirstHandIndex), nameof(command.SecondHandIndex))
                && command.FirstHandIndex.HasValue && command.SecondHandIndex.HasValue:
                action = new PeekInitialCards(command.FirstHandIndex.Value, command.SecondHandIndex.Value);
                break;
            case GameplayActionTypeV1.DrawCard when HasOnly(command):
                action = new DrawCard();
                break;
            case GameplayActionTypeV1.DiscardDrawnCard when HasOnly(command):
                action = new DiscardDrawnCard();
                break;
            case GameplayActionTypeV1.ReplaceHandCard when HasOnly(command, nameof(command.HandIndex))
                && command.HandIndex.HasValue:
                action = new ReplaceHandCard(command.HandIndex.Value);
                break;
            case GameplayActionTypeV1.ActivateDrawnAbility when HasOnly(command):
                action = new ActivateDrawnAbility();
                break;
            case GameplayActionTypeV1.ResolveJackAbility when HasOnly(command, nameof(command.HandIndex))
                && command.HandIndex.HasValue:
                action = new ResolveJackAbility(command.HandIndex.Value);
                break;
            case GameplayActionTypeV1.ResolveQueenAbility when HasOnly(command, nameof(command.HandIndex))
                && command.HandIndex.HasValue:
                action = new ResolveQueenAbility(command.HandIndex.Value);
                break;
            case GameplayActionTypeV1.ResolveKingAbility when HasOnly(
                    command,
                    nameof(command.HandIndex),
                    nameof(command.TargetPlayerId),
                    nameof(command.TargetHandIndex))
                && command.HandIndex.HasValue
                && !string.IsNullOrWhiteSpace(command.TargetPlayerId)
                && command.TargetPlayerId.Length <= 64
                && command.TargetHandIndex.HasValue:
                action = new ResolveKingAbility(
                    command.TargetPlayerId,
                    command.HandIndex.Value,
                    command.TargetHandIndex.Value);
                break;
            case GameplayActionTypeV1.CallAlzheimer when HasOnly(command):
                action = new CallAlzheimer();
                break;
            case GameplayActionTypeV1.EndTurn when HasOnly(command):
                action = new EndTurn();
                break;
            case GameplayActionTypeV1.ThrowReactionCard when HasOnly(
                    command,
                    nameof(command.HandIndex),
                    nameof(command.ReactionWindowId))
                && command.HandIndex.HasValue
                && command.ReactionWindowId.HasValue:
                action = new ThrowReactionCard(
                    command.HandIndex.Value,
                    command.ReactionWindowId.Value,
                    command.CommandId);
                break;
            case GameplayActionTypeV1.MarkReactionDone when HasOnly(command, nameof(command.ReactionWindowId))
                && command.ReactionWindowId.HasValue:
                action = new MarkReactionDone(command.ReactionWindowId.Value, command.CommandId);
                break;
            default:
                errorCode = "InvalidCommandPayload";
                return false;
        }

        return true;
    }

    private static bool HasOnly(GameplayCommandV1 command, params string[] allowedProperties)
    {
        var allowed = new HashSet<string>(allowedProperties, StringComparer.Ordinal);
        return (command.FirstHandIndex is null || allowed.Contains(nameof(command.FirstHandIndex)))
            && (command.SecondHandIndex is null || allowed.Contains(nameof(command.SecondHandIndex)))
            && (command.HandIndex is null || allowed.Contains(nameof(command.HandIndex)))
            && (command.TargetPlayerId is null || allowed.Contains(nameof(command.TargetPlayerId)))
            && (command.TargetHandIndex is null || allowed.Contains(nameof(command.TargetHandIndex)))
            && (command.ReactionWindowId is null || allowed.Contains(nameof(command.ReactionWindowId)));
    }
}

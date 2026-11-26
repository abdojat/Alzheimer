using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Alzheimer.Client.Networking
{
    [Serializable]
    public sealed class LobbyPlayerV1
    {
        [JsonProperty("playerId")] public string PlayerId;
        [JsonProperty("displayName")] public string DisplayName;
        [JsonProperty("isHost")] public bool IsHost;
    }

    [Serializable]
    public sealed class LobbySnapshotV1
    {
        [JsonProperty("code")] public string Code;
        [JsonProperty("deckCount")] public int DeckCount;
        [JsonProperty("maxPlayers")] public int MaxPlayers;
        [JsonProperty("players")] public LobbyPlayerV1[] Players;
        [JsonProperty("localPlayerId")] public string LocalPlayerId;
    }

    [Serializable]
    public sealed class CardViewV1
    {
        [JsonProperty("physicalCardId")] public string PhysicalCardId;
        [JsonProperty("rank")] public object Rank;
        [JsonProperty("suit")] public object Suit;
    }

    [Serializable]
    public sealed class CardSlotViewV1
    {
        [JsonProperty("handIndex")] public int HandIndex;
        [JsonProperty("knowledge")] public object Knowledge;
        [JsonProperty("knownCard")] public CardViewV1 KnownCard;
    }

    [Serializable]
    public sealed class PublicPlayerViewV1
    {
        [JsonProperty("playerId")] public string PlayerId;
        [JsonProperty("displayName")] public string DisplayName;
        [JsonProperty("handCount")] public int HandCount;
        [JsonProperty("cumulativeScore")] public int CumulativeScore;
        [JsonProperty("roundPenaltyPoints")] public int RoundPenaltyPoints;
    }

    [Serializable]
    public sealed class PublicGameViewV1
    {
        [JsonProperty("phase")] public string Phase;
        [JsonProperty("hostPlayerId")] public string HostPlayerId;
        [JsonProperty("activePlayerId")] public string ActivePlayerId;
        [JsonProperty("callerId")] public string CallerId;
        [JsonProperty("deckCount")] public int DeckCount;
        [JsonProperty("drawPileCount")] public int DrawPileCount;
        [JsonProperty("players")] public PublicPlayerViewV1[] Players;
        [JsonProperty("discardPile")] public CardViewV1[] DiscardPile;
        [JsonProperty("reactionWindowId")] public long? ReactionWindowId;
        [JsonProperty("reactionDeadlineUtc")] public DateTimeOffset? ReactionDeadlineUtc;
        [JsonProperty("reactionDonePlayerIds")] public string[] ReactionDonePlayerIds;
        [JsonProperty("requiredFinalTurnPlayerIds")] public string[] RequiredFinalTurnPlayerIds;
        [JsonProperty("pendingFinalTurnPlayerIds")] public string[] PendingFinalTurnPlayerIds;
        [JsonProperty("isFinalTurnMode")] public bool IsFinalTurnMode;
        [JsonProperty("roundNumber")] public int RoundNumber;
        [JsonProperty("matchCompleted")] public bool MatchCompleted;
        [JsonProperty("winnerPlayerIds")] public string[] WinnerPlayerIds;
    }

    [Serializable]
    public sealed class PlayerGameViewV1
    {
        [JsonProperty("public")] public PublicGameViewV1 Public;
        [JsonProperty("ownHand")] public CardSlotViewV1[] OwnHand;
        [JsonProperty("pendingDrawnCard")] public CardViewV1 PendingDrawnCard;
        [JsonProperty("availableDrawResolutions")] public string[] AvailableDrawResolutions;
        [JsonProperty("abilityDeadlineUtc")] public DateTimeOffset? AbilityDeadlineUtc;
        [JsonProperty("canCallAlzheimer")] public bool CanCallAlzheimer;
        [JsonProperty("canEndTurn")] public bool CanEndTurn;
    }

    [Serializable]
    public sealed class GamePublicUpdateV1
    {
        [JsonProperty("protocolVersion")] public int ProtocolVersion;
        [JsonProperty("sequence")] public long Sequence;
        [JsonProperty("view")] public PublicGameViewV1 View;
    }

    [Serializable]
    public sealed class GamePrivateUpdateV1
    {
        [JsonProperty("protocolVersion")] public int ProtocolVersion;
        [JsonProperty("sequence")] public long Sequence;
        [JsonProperty("view")] public PlayerGameViewV1 View;
    }

    [Serializable]
    public sealed class StartMatchResponseV1
    {
        [JsonProperty("protocolVersion")] public int ProtocolVersion;
        [JsonProperty("stateSequence")] public long? StateSequence;
        [JsonProperty("accepted")] public bool Accepted;
        [JsonProperty("errorCode")] public string ErrorCode;
        [JsonProperty("publicView")] public PublicGameViewV1 PublicView;
        [JsonProperty("privateView")] public PlayerGameViewV1 PrivateView;
    }

    [Serializable]
    public sealed class GameCommandResponseV1
    {
        [JsonProperty("protocolVersion")] public int ProtocolVersion;
        [JsonProperty("stateSequence")] public long? StateSequence;
        [JsonProperty("commandId")] public Guid? CommandId;
        [JsonProperty("accepted")] public bool Accepted;
        [JsonProperty("errorCode")] public string ErrorCode;
        [JsonProperty("publicView")] public PublicGameViewV1 PublicView;
        [JsonProperty("privateView")] public PlayerGameViewV1 PrivateView;
    }

    [Serializable]
    public sealed class GameplayCommandV1
    {
        [JsonProperty("protocolVersion")] public int ProtocolVersion = 1;
        [JsonProperty("commandId")] public Guid CommandId;
        [JsonProperty("action")] public string Action;
        [JsonProperty("firstHandIndex")] public int? FirstHandIndex;
        [JsonProperty("secondHandIndex")] public int? SecondHandIndex;
        [JsonProperty("handIndex")] public int? HandIndex;
        [JsonProperty("targetPlayerId")] public string TargetPlayerId;
        [JsonProperty("targetHandIndex")] public int? TargetHandIndex;
        [JsonProperty("reactionWindowId")] public long? ReactionWindowId;

        public static GameplayCommandV1 Create(string action)
        {
            return new GameplayCommandV1
            {
                CommandId = Guid.NewGuid(),
                Action = action
            };
        }
    }

    [Serializable]
    internal sealed class NegotiateResponseV1
    {
        [JsonProperty("connectionToken")] public string ConnectionToken;
        [JsonProperty("accessToken")] public string AccessToken;
    }

    [Serializable]
    internal sealed class HubInvocationV1
    {
        [JsonProperty("type")] public int Type;
        [JsonProperty("invocationId")] public string InvocationId;
        [JsonProperty("target")] public string Target;
        [JsonProperty("arguments")] public object[] Arguments;
        [JsonProperty("result")] public object Result;
        [JsonProperty("error")] public string Error;
    }
}

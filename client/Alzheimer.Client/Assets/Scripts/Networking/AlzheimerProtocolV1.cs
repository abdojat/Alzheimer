using System;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json;

namespace Alzheimer.Client.Networking
{
    public enum GameplayActionV1
    {
        PeekInitialCards = 0,
        DrawCard = 1,
        DiscardDrawnCard = 2,
        ReplaceHandCard = 3,
        ActivateDrawnAbility = 4,
        ResolveJackAbility = 5,
        ResolveQueenAbility = 6,
        ResolveKingAbility = 7,
        CallAlzheimer = 8,
        EndTurn = 9,
        ThrowReactionCard = 10,
        MarkReactionDone = 11
    }

    public enum GamePhaseV1
    {
        WaitingForInitialPeeks = 0,
        AwaitingDraw = 1,
        AwaitingDrawResolution = 2,
        AwaitingAbilityResolution = 3,
        AwaitingPostResolutionDecision = 4,
        AwaitingReactionResolution = 5,
        FinalPhase = 6,
        RoundCompleted = 7
    }

    public enum DrawResolutionOptionV1
    {
        Discard = 0,
        Replace = 1,
        ActivateAbility = 2
    }

    public enum CardRankV1
    {
        Joker = 0,
        Ace = 1,
        Two = 2,
        Three = 3,
        Four = 4,
        Five = 5,
        Six = 6,
        Seven = 7,
        Eight = 8,
        Nine = 9,
        Ten = 10,
        Jack = 11,
        Queen = 12,
        King = 13
    }

    public enum CardSuitV1
    {
        None = 0,
        Clubs = 1,
        Diamonds = 2,
        Hearts = 3,
        Spades = 4
    }

    public enum CardKnowledgeV1
    {
        Unknown = 0,
        KnownToOwner = 1
    }

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
        [JsonProperty("rank")] public CardRankV1 Rank;
        [JsonProperty("suit")] public CardSuitV1 Suit;
    }

    [Serializable]
    public sealed class CardSlotViewV1
    {
        [JsonProperty("handIndex")] public int HandIndex;
        [JsonProperty("knowledge")] public CardKnowledgeV1 Knowledge;
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
        [JsonProperty("phase")] public GamePhaseV1 Phase;
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
        [JsonProperty("availableDrawResolutions")] public DrawResolutionOptionV1[] AvailableDrawResolutions;
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
        [JsonProperty("action")] public GameplayActionV1 Action;
        [JsonProperty("firstHandIndex")] public int? FirstHandIndex;
        [JsonProperty("secondHandIndex")] public int? SecondHandIndex;
        [JsonProperty("handIndex")] public int? HandIndex;
        [JsonProperty("targetPlayerId")] public string TargetPlayerId;
        [JsonProperty("targetHandIndex")] public int? TargetHandIndex;
        [JsonProperty("reactionWindowId")] public long? ReactionWindowId;

        public static GameplayCommandV1 Create(GameplayActionV1 action)
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

    public sealed class SignalRFrameParser
    {
        private const int MaximumFrameBytes = 1024 * 1024;
        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);
        private readonly List<byte> _frameBytes = new List<byte>();

        public IReadOnlyList<string> Append(byte[] bytes, int count)
        {
            if (bytes == null)
                throw new ArgumentNullException(nameof(bytes));
            if (count < 0 || count > bytes.Length)
                throw new ArgumentOutOfRangeException(nameof(count));

            var frames = new List<string>();
            for (var i = 0; i < count; i++)
            {
                if (bytes[i] == 0x1e)
                {
                    frames.Add(StrictUtf8.GetString(_frameBytes.ToArray()));
                    _frameBytes.Clear();
                    continue;
                }

                _frameBytes.Add(bytes[i]);
                if (_frameBytes.Count > MaximumFrameBytes)
                    throw new InvalidOperationException("SignalR frame exceeded the maximum supported size.");
            }

            return frames;
        }

        public void Reset()
        {
            _frameBytes.Clear();
        }
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

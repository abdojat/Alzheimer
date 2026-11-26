using System;
using System.Collections.Generic;
using Alzheimer.Client.Networking;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Alzheimer.Client.UI
{
    public sealed class GameTableScreen : MonoBehaviour
    {
        private Text _turn;
        private Text _status;
        private Text _timer;
        private Text _drawnCardText;
        private readonly List<CardView> _cards = new List<CardView>();
        private readonly List<int> _selectedPeekIndices = new List<int>();
        private readonly HashSet<int> _revealedHandIndices = new HashSet<int>();
        private int _selectedHandIndex = -1;
        private Transform _cardParent;
        private Transform _actionParent;
        private NetworkManager _network;
        private DateTimeOffset _handRevealUntilUtc;
        private DateTimeOffset _drawnRevealUntilUtc;
        private string _viewedDrawnCardId;
        private bool _initialRevealRequested;
        private int _jackRevealIndex = -1;

        public void Initialize(GameObject root)
        {
            var panel = CreatePanel(root.transform, "Table", new Color(0.08f, 0.24f, 0.16f));
            _turn = CreateText(panel.transform, "Turn", "Waiting for server state", 38);
            SetAnchored(_turn.rectTransform, new Vector2(0.5f, 0.93f), new Vector2(0.5f, 0.93f), new Vector2(900f, 80f));

            CreatePile(panel.transform, "Draw Pile", "DRAW", new Vector2(0.29f, 0.74f));
            CreatePile(panel.transform, "Discard Pile", "DISCARD", new Vector2(0.71f, 0.74f));

            _timer = CreateText(panel.transform, "ReactionTimer", "Reaction: --", 34);
            SetAnchored(_timer.rectTransform, new Vector2(0.5f, 0.60f), new Vector2(0.5f, 0.60f), new Vector2(900f, 70f));

            _drawnCardText = CreateText(panel.transform, "DrawnCardReveal", "", 36);
            SetAnchored(_drawnCardText.rectTransform, new Vector2(0.5f, 0.55f), new Vector2(0.5f, 0.55f), new Vector2(900f, 70f));

            var cardContainer = new GameObject("Cards");
            cardContainer.transform.SetParent(panel.transform, false);
            var cardRect = cardContainer.AddComponent<RectTransform>();
            cardRect.anchorMin = new Vector2(0.05f, 0.30f);
            cardRect.anchorMax = new Vector2(0.95f, 0.50f);
            cardRect.offsetMin = cardRect.offsetMax = Vector2.zero;
            _cardParent = cardContainer.transform;

            _status = CreateText(panel.transform, "Status", "Waiting for authorized state", 30);
            SetAnchored(_status.rectTransform, new Vector2(0.5f, 0.265f), new Vector2(0.5f, 0.265f), new Vector2(900f, 70f));

            var actionViewport = new GameObject("ActionViewport");
            actionViewport.transform.SetParent(panel.transform, false);
            var actionRect = actionViewport.AddComponent<RectTransform>();
            actionRect.anchorMin = new Vector2(0.05f, 0.065f);
            actionRect.anchorMax = new Vector2(0.95f, 0.235f);
            actionRect.offsetMin = actionRect.offsetMax = Vector2.zero;
            actionViewport.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.01f);
            actionViewport.AddComponent<RectMask2D>();
            var scroll = actionViewport.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.viewport = actionRect;

            var actionContent = new GameObject("ActionContent");
            actionContent.transform.SetParent(actionViewport.transform, false);
            var contentRect = actionContent.AddComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0f, 1f);
            contentRect.anchorMax = new Vector2(1f, 1f);
            contentRect.pivot = new Vector2(0.5f, 1f);
            contentRect.sizeDelta = Vector2.zero;
            var layout = actionContent.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(8, 8, 8, 8);
            layout.spacing = 8f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            var fitter = actionContent.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.content = contentRect;
            _actionParent = actionContent.transform;

            CreateButton(panel.transform, "Leave", "BACK / LEAVE", 34, new Vector2(0.5f, 0.03f), LeaveTable);

            _network = Object.FindAnyObjectByType<NetworkManager>();
            if (_network != null)
            {
                UnsubscribeNetworkEvents();
                _network.PublicStateChanged += HandlePublicState;
                _network.PrivateStateChanged += HandlePrivateState;
                _network.CommandRejected += HandleRejected;
                _network.RoomClosed += HandleRoomClosed;
                if (_network.PublicGame != null)
                    HandlePublicState(_network.PublicGame);
                if (_network.PrivateGame != null)
                    HandlePrivateState(_network.PrivateGame);
            }
        }

        private void OnDisable()
        {
            UnsubscribeNetworkEvents();
        }

        private void UnsubscribeNetworkEvents()
        {
            if (_network == null)
                return;
            _network.PublicStateChanged -= HandlePublicState;
            _network.PrivateStateChanged -= HandlePrivateState;
            _network.CommandRejected -= HandleRejected;
            _network.RoomClosed -= HandleRoomClosed;
        }

        private void Update()
        {
            if (_network?.PublicGame?.ReactionDeadlineUtc != null)
            {
                var seconds = (_network.PublicGame.ReactionDeadlineUtc.Value - DateTimeOffset.UtcNow).TotalSeconds;
                _timer.text = seconds > 0 ? $"Reaction: {Mathf.CeilToInt((float)seconds)}s" : "Reaction: closing";
            }
            else
                _timer.text = "Reaction: --";

            if (_handRevealUntilUtc != default && DateTimeOffset.UtcNow >= _handRevealUntilUtc)
            {
                foreach (var card in _cards)
                    if (card != null)
                        card.SetFaceDown();
                _revealedHandIndices.Clear();
                _handRevealUntilUtc = default;
            }

            if (_drawnRevealUntilUtc != default && DateTimeOffset.UtcNow >= _drawnRevealUntilUtc)
            {
                _drawnCardText.text = "";
                _drawnRevealUntilUtc = default;
            }
        }

        private void HandleRoomClosed()
        {
            _status.text = "Room closed by server. Active-match disconnect policy: no reconnect.";
        }

        private void HandleRejected(string error)
        {
            _status.text = "Server rejected action: " + error;
        }

        private void HandlePublicState(PublicGameViewV1 publicView)
        {
            var activeName = FindPlayerName(publicView, publicView.ActivePlayerId);
            _turn.text = $"Round {publicView.RoundNumber} | Turn: {activeName}";
            var drawCount = publicView.DrawPileCount;
            _status.text = $"Draw pile: {drawCount} | Phase: {FormatPhase(publicView.Phase)}";
        }

        private void HandlePrivateState(PlayerGameViewV1 privateView)
        {
            if (privateView.PendingDrawnCard == null
                || privateView.PendingDrawnCard.PhysicalCardId != _viewedDrawnCardId)
                _viewedDrawnCardId = null;

            _revealedHandIndices.Clear();
            _handRevealUntilUtc = default;
            _drawnRevealUntilUtc = default;
            _drawnCardText.text = "";

            if (_initialRevealRequested && _selectedPeekIndices.Count == 2)
            {
                RevealKnownCards(privateView, _selectedPeekIndices);
                _initialRevealRequested = false;
            }
            else if (_jackRevealIndex >= 0)
            {
                RevealKnownCards(privateView, new[] { _jackRevealIndex });
                _jackRevealIndex = -1;
            }

            RenderHand(privateView);
            RenderActions(privateView);
        }

        private void RenderHand(PlayerGameViewV1 privateView)
        {
            foreach (var card in _cards)
                if (card != null)
                    Destroy(card.gameObject);
            _cards.Clear();

            if (privateView.OwnHand == null)
                return;
            var count = privateView.OwnHand.Length;
            for (var i = 0; i < count; i++)
            {
                var slot = privateView.OwnHand[i];
                var card = CreateCard(_cardParent, i, count);
                card.Initialize(i);
                card.SetSelected(i == _selectedHandIndex || _selectedPeekIndices.Contains(i));
                if (_revealedHandIndices.Contains(i) && slot.KnownCard != null)
                    card.Reveal(FormatCard(slot.KnownCard));
                card.Selected += OnCardSelected;
                _cards.Add(card);
            }
        }

        private void OnCardSelected(CardView card)
        {
            var publicView = _network?.PrivateGame?.Public;
            if (publicView != null && publicView.Phase == GamePhaseV1.WaitingForInitialPeeks)
            {
                if (card.IsSelected)
                {
                    if (!_selectedPeekIndices.Contains(card.CardIndex))
                        _selectedPeekIndices.Add(card.CardIndex);
                }
                else
                    _selectedPeekIndices.Remove(card.CardIndex);
                if (_selectedPeekIndices.Count > 2)
                {
                    var remove = _selectedPeekIndices[0];
                    _selectedPeekIndices.RemoveAt(0);
                    foreach (var candidate in _cards)
                        if (candidate.CardIndex == remove)
                            candidate.SetSelected(false);
                }
                _status.text = _selectedPeekIndices.Count == 2
                    ? "Two cards selected. Confirm private peek."
                    : "Select exactly two card positions.";
            }
            else
            {
                _selectedPeekIndices.Clear();
                _selectedHandIndex = card.IsSelected ? card.CardIndex : -1;
                foreach (var candidate in _cards)
                    if (candidate != card)
                        candidate.SetSelected(false);
                _status.text = card.IsSelected ? $"Selected hand position {card.CardIndex}" : "Selection cleared";
            }

            if (_network?.PrivateGame != null)
                RenderActions(_network.PrivateGame);
        }

        private void RenderActions(PlayerGameViewV1 privateView)
        {
            for (var i = _actionParent.childCount - 1; i >= 0; i--)
            {
                var child = _actionParent.GetChild(i);
                child.gameObject.SetActive(false);
                Destroy(child.gameObject);
            }

            var publicView = privateView.Public;
            if (publicView == null)
                return;

            AddRevealActions(privateView);
            var isLocalTurn = publicView.ActivePlayerId == _network?.LocalPlayerId;
            if (publicView.Phase == GamePhaseV1.WaitingForInitialPeeks)
            {
                if (HasKnownHandCard(privateView))
                    _status.text = "Waiting for the other players to peek.";
                else
                    CreateActionButton("PEEK TWO CARDS", SubmitPeek);
                return;
            }

            if (publicView.Phase == GamePhaseV1.AwaitingReactionResolution)
            {
                var done = publicView.ReactionDonePlayerIds != null
                    && Array.IndexOf(publicView.ReactionDonePlayerIds, _network?.LocalPlayerId) >= 0;
                if (!done)
                    CreateActionButton("REACTION DONE", SubmitReactionDone);
                return;
            }

            if (!isLocalTurn)
                return;

            if (publicView.Phase == GamePhaseV1.AwaitingDraw)
                CreateActionButton("DRAW CARD", () => Submit(GameplayActionV1.DrawCard));
            else if (publicView.Phase == GamePhaseV1.AwaitingDrawResolution)
            {
                if (!HasViewedDrawnCard(privateView))
                {
                    _status.text = "Reveal and view your drawn card before resolving it.";
                    return;
                }
                if (HasResolution(privateView, DrawResolutionOptionV1.Discard))
                    CreateActionButton("DISCARD DRAWN", () => Submit(GameplayActionV1.DiscardDrawnCard));
                if (privateView.AvailableDrawResolutions != null)
                {
                    foreach (var option in privateView.AvailableDrawResolutions)
                    {
                        if (option == DrawResolutionOptionV1.Replace)
                            CreateActionButton("REPLACE SELECTED", SubmitReplace);
                        else if (option == DrawResolutionOptionV1.ActivateAbility)
                            CreateActionButton("ACTIVATE ABILITY", () => Submit(GameplayActionV1.ActivateDrawnAbility));
                    }
                }
            }
            else if (publicView.Phase == GamePhaseV1.AwaitingAbilityResolution)
            {
                if (!HasViewedDrawnCard(privateView))
                {
                    _status.text = "Reveal and view your ability card before resolving it.";
                    return;
                }
                RenderAbilityResolution(privateView);
            }
            else if (publicView.Phase == GamePhaseV1.AwaitingPostResolutionDecision)
            {
                if (privateView.CanCallAlzheimer)
                    CreateActionButton("CALL ALZHEIMER", () => Submit(GameplayActionV1.CallAlzheimer));
                if (privateView.CanEndTurn)
                    CreateActionButton("END TURN", () => Submit(GameplayActionV1.EndTurn));
            }
        }

        private void AddRevealActions(PlayerGameViewV1 privateView)
        {
            if (HasSelectedKnownCard(privateView))
                CreateActionButton("REVEAL SELECTED (5 SEC)", RevealSelectedCards);
            if (privateView.PendingDrawnCard != null)
                CreateActionButton("REVEAL DRAWN CARD (5 SEC)", RevealDrawnCard);
        }

        private void RenderAbilityResolution(PlayerGameViewV1 privateView)
        {
            var rank = CardRankName(privateView.PendingDrawnCard?.Rank);
            if (rank == "J")
            {
                var button = CreateActionButton("RESOLVE JACK ON SELECTED", ResolveJack);
                button.interactable = _selectedHandIndex >= 0;
                return;
            }
            if (rank == "Q")
            {
                var button = CreateActionButton("RESOLVE QUEEN ON SELECTED", ResolveQueen);
                button.interactable = _selectedHandIndex >= 0;
                return;
            }
            if (rank != "K")
                return;

            if (_selectedHandIndex < 0 || !HasOpponentCard(privateView.Public))
            {
                var prompt = CreateActionButton("SELECT YOUR CARD TO SWAP", () => { });
                prompt.interactable = false;
                return;
            }

            foreach (var player in privateView.Public.Players)
            {
                if (player.PlayerId == _network?.LocalPlayerId)
                    continue;
                for (var index = 0; index < player.HandCount; index++)
                {
                    var targetPlayerId = player.PlayerId;
                    var targetHandIndex = index;
                    CreateActionButton(
                        $"SWAP WITH {player.DisplayName} CARD {index + 1}",
                        () => ResolveKing(targetPlayerId, targetHandIndex));
                }
            }
        }

        private bool HasOpponentCard(PublicGameViewV1 view)
        {
            if (view.Players == null)
                return false;
            foreach (var player in view.Players)
                if (player.PlayerId != _network?.LocalPlayerId && player.HandCount > 0)
                    return true;
            return false;
        }

        private static bool HasKnownHandCard(PlayerGameViewV1 view)
        {
            if (view.OwnHand == null)
                return false;
            foreach (var slot in view.OwnHand)
                if (slot.KnownCard != null)
                    return true;
            return false;
        }

        private bool HasSelectedKnownCard(PlayerGameViewV1 view)
        {
            if (view.OwnHand == null)
                return false;
            if (_selectedHandIndex >= 0 && _selectedHandIndex < view.OwnHand.Length)
                return view.OwnHand[_selectedHandIndex].KnownCard != null;
            foreach (var index in _selectedPeekIndices)
                if (index >= 0 && index < view.OwnHand.Length && view.OwnHand[index].KnownCard != null)
                    return true;
            return false;
        }

        private static bool HasResolution(PlayerGameViewV1 view, DrawResolutionOptionV1 expected)
        {
            if (view.AvailableDrawResolutions == null)
                return false;
            foreach (var option in view.AvailableDrawResolutions)
                if (option == expected)
                    return true;
            return false;
        }

        private void SubmitPeek()
        {
            if (_selectedPeekIndices.Count != 2)
            {
                _status.text = "Select exactly two distinct card positions.";
                return;
            }
            _initialRevealRequested = true;
            var command = GameplayCommandV1.Create(GameplayActionV1.PeekInitialCards);
            command.FirstHandIndex = _selectedPeekIndices[0];
            command.SecondHandIndex = _selectedPeekIndices[1];
            _network?.SubmitCommand(command);
        }

        private void SubmitReplace()
        {
            if (_selectedHandIndex < 0)
            {
                _status.text = "Select a hand position to replace.";
                return;
            }
            var command = GameplayCommandV1.Create(GameplayActionV1.ReplaceHandCard);
            command.HandIndex = _selectedHandIndex;
            _network?.SubmitCommand(command);
        }

        private void Submit(GameplayActionV1 action)
        {
            _network?.SubmitCommand(GameplayCommandV1.Create(action));
        }

        private void ResolveJack()
        {
            if (_selectedHandIndex < 0)
                return;
            _jackRevealIndex = _selectedHandIndex;
            var command = GameplayCommandV1.Create(GameplayActionV1.ResolveJackAbility);
            command.HandIndex = _selectedHandIndex;
            _network?.SubmitCommand(command);
        }

        private void ResolveQueen()
        {
            if (_selectedHandIndex < 0)
                return;
            var command = GameplayCommandV1.Create(GameplayActionV1.ResolveQueenAbility);
            command.HandIndex = _selectedHandIndex;
            _network?.SubmitCommand(command);
        }

        private void ResolveKing(string targetPlayerId, int targetHandIndex)
        {
            if (_selectedHandIndex < 0)
                return;
            var command = GameplayCommandV1.Create(GameplayActionV1.ResolveKingAbility);
            command.HandIndex = _selectedHandIndex;
            command.TargetPlayerId = targetPlayerId;
            command.TargetHandIndex = targetHandIndex;
            _network?.SubmitCommand(command);
        }

        private void RevealSelectedCards()
        {
            var privateView = _network?.PrivateGame;
            if (privateView == null)
                return;
            var indices = _selectedHandIndex >= 0
                ? new[] { _selectedHandIndex }
                : _selectedPeekIndices.ToArray();
            RevealKnownCards(privateView, indices);
            RenderHand(privateView);
        }

        private void RevealDrawnCard()
        {
            var drawn = _network?.PrivateGame?.PendingDrawnCard;
            if (drawn == null)
                return;
            _viewedDrawnCardId = drawn.PhysicalCardId;
            _drawnCardText.text = "Drawn card: " + FormatCard(drawn);
            _drawnRevealUntilUtc = DateTimeOffset.UtcNow.AddSeconds(5);
        }

        private bool HasViewedDrawnCard(PlayerGameViewV1 privateView)
        {
            return privateView.PendingDrawnCard != null
                && !string.IsNullOrEmpty(privateView.PendingDrawnCard.PhysicalCardId)
                && privateView.PendingDrawnCard.PhysicalCardId == _viewedDrawnCardId;
        }

        private void RevealKnownCards(PlayerGameViewV1 privateView, IEnumerable<int> indices)
        {
            if (privateView?.OwnHand == null)
                return;
            foreach (var index in indices)
            {
                if (index >= 0 && index < privateView.OwnHand.Length
                    && privateView.OwnHand[index].KnownCard != null)
                    _revealedHandIndices.Add(index);
            }
            if (_revealedHandIndices.Count > 0)
                _handRevealUntilUtc = DateTimeOffset.UtcNow.AddSeconds(5);
        }

        private void SubmitReactionDone()
        {
            var windowId = _network?.PublicGame?.ReactionWindowId;
            if (!windowId.HasValue)
                return;
            var command = GameplayCommandV1.Create(GameplayActionV1.MarkReactionDone);
            command.ReactionWindowId = windowId;
            _network?.SubmitCommand(command);
        }

        private Button CreateActionButton(string label, UnityEngine.Events.UnityAction action)
        {
            var button = CreateButton(_actionParent, label, label, 34, new Vector2(0.5f, 0.5f), action);
            var rect = button.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.sizeDelta = Vector2.zero;
            var layout = button.gameObject.AddComponent<LayoutElement>();
            layout.preferredHeight = 96f;
            return button;
        }

        private void LeaveTable()
        {
            _network?.LeaveRoom();
            SceneManager.LoadScene("MainMenu");
        }

        private static string FindPlayerName(PublicGameViewV1 view, string playerId)
        {
            if (view.Players != null)
                foreach (var player in view.Players)
                    if (player.PlayerId == playerId)
                        return player.DisplayName;
            return playerId;
        }

        private static string FormatPhase(GamePhaseV1 phase)
        {
            return phase.ToString();
        }

        private static string FormatCard(CardViewV1 card)
        {
            if (card == null)
                return "?";
            var rank = CardRankName(card.Rank);
            var suit = CardSuitName(card.Suit);
            return suit == "" ? rank : rank + " " + suit;
        }

        private static string CardRankName(CardRankV1? rank)
        {
            if (!rank.HasValue)
                return "?";
            return rank.Value switch
            {
                CardRankV1.Joker => "Joker",
                CardRankV1.Ace => "A",
                CardRankV1.Two => "2",
                CardRankV1.Three => "3",
                CardRankV1.Four => "4",
                CardRankV1.Five => "5",
                CardRankV1.Six => "6",
                CardRankV1.Seven => "7",
                CardRankV1.Eight => "8",
                CardRankV1.Nine => "9",
                CardRankV1.Ten => "10",
                CardRankV1.Jack => "J",
                CardRankV1.Queen => "Q",
                CardRankV1.King => "K",
                _ => "?"
            };
        }

        private static string CardSuitName(CardSuitV1 suit)
        {
            return suit switch
            {
                CardSuitV1.Clubs => "C",
                CardSuitV1.Diamonds => "D",
                CardSuitV1.Hearts => "H",
                CardSuitV1.Spades => "S",
                _ => ""
            };
        }

        private CardView CreateCard(Transform parent, int index, int count)
        {
            var go = new GameObject("PlayerCard" + (index + 1));
            go.transform.SetParent(parent, false);
            var rect = go.AddComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2((index + 1f) / (count + 1f), 0.5f);
            rect.sizeDelta = new Vector2(count > 6 ? 140f : 190f, 270f);
            var image = go.AddComponent<Image>();
            image.color = new Color(0.92f, 0.92f, 0.92f);
            var button = go.AddComponent<Button>();
            button.targetGraphic = image;
            var text = CreateText(go.transform, "CardLabel", "?", 54);
            text.rectTransform.anchorMin = Vector2.zero;
            text.rectTransform.anchorMax = Vector2.one;
            text.rectTransform.offsetMin = Vector2.zero;
            text.rectTransform.offsetMax = Vector2.zero;
            text.color = Color.black;
            return go.AddComponent<CardView>();
        }

        private static void CreatePile(Transform parent, string name, string label, Vector2 anchor)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rect = go.AddComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = anchor;
            rect.sizeDelta = new Vector2(260f, 300f);
            var image = go.AddComponent<Image>();
            image.color = name == "Draw Pile" ? new Color(0.18f, 0.18f, 0.22f) : new Color(0.78f, 0.72f, 0.62f);
            var text = CreateText(go.transform, "PileLabel", label, 38);
            text.rectTransform.anchorMin = Vector2.zero;
            text.rectTransform.anchorMax = Vector2.one;
            text.rectTransform.offsetMin = Vector2.zero;
            text.rectTransform.offsetMax = Vector2.zero;
        }

        private static Image CreatePanel(Transform parent, string name, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rect = go.AddComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            var image = go.AddComponent<Image>();
            image.color = color;
            return image;
        }

        private static Text CreateText(Transform parent, string name, string value, int fontSize)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rect = go.AddComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(800f, 100f);
            var text = go.AddComponent<Text>();
            text.text = value;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = fontSize;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            return text;
        }

        private static Button CreateButton(Transform parent, string name, string label, int fontSize, Vector2 anchor, UnityEngine.Events.UnityAction action)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rect = go.AddComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = anchor;
            rect.sizeDelta = new Vector2(520f, 112f);
            var image = go.AddComponent<Image>();
            image.color = new Color(0.16f, 0.38f, 0.58f);
            var button = go.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(action);
            var text = CreateText(go.transform, "Label", label, fontSize);
            text.rectTransform.anchorMin = Vector2.zero;
            text.rectTransform.anchorMax = Vector2.one;
            text.rectTransform.offsetMin = Vector2.zero;
            text.rectTransform.offsetMax = Vector2.zero;
            return button;
        }

        private static void SetAnchored(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 size)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = size;
        }
    }
}

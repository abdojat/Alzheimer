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
        private readonly List<CardView> _cards = new List<CardView>();
        private readonly List<int> _selectedPeekIndices = new List<int>();
        private int _selectedHandIndex = -1;
        private Transform _cardParent;
        private Transform _actionParent;
        private NetworkManager _network;

        public void Initialize(GameObject root)
        {
            var panel = CreatePanel(root.transform, "Table", new Color(0.08f, 0.24f, 0.16f));
            _turn = CreateText(panel.transform, "Turn", "Waiting for server state", 38);
            SetAnchored(_turn.rectTransform, new Vector2(0.5f, 0.93f), new Vector2(0.5f, 0.93f), new Vector2(900f, 80f));

            CreatePile(panel.transform, "Draw Pile", "DRAW", new Vector2(0.29f, 0.74f));
            CreatePile(panel.transform, "Discard Pile", "DISCARD", new Vector2(0.71f, 0.74f));

            _timer = CreateText(panel.transform, "ReactionTimer", "Reaction: --", 34);
            SetAnchored(_timer.rectTransform, new Vector2(0.5f, 0.60f), new Vector2(0.5f, 0.60f), new Vector2(900f, 70f));

            var cardContainer = new GameObject("Cards");
            cardContainer.transform.SetParent(panel.transform, false);
            var cardRect = cardContainer.AddComponent<RectTransform>();
            cardRect.anchorMin = new Vector2(0.05f, 0.28f);
            cardRect.anchorMax = new Vector2(0.95f, 0.52f);
            cardRect.offsetMin = cardRect.offsetMax = Vector2.zero;
            _cardParent = cardContainer.transform;

            _status = CreateText(panel.transform, "Status", "Waiting for authorized state", 30);
            SetAnchored(_status.rectTransform, new Vector2(0.5f, 0.24f), new Vector2(0.5f, 0.24f), new Vector2(900f, 70f));

            var actionContainer = new GameObject("Actions");
            actionContainer.transform.SetParent(panel.transform, false);
            var actionRect = actionContainer.AddComponent<RectTransform>();
            actionRect.anchorMin = new Vector2(0.05f, 0.12f);
            actionRect.anchorMax = new Vector2(0.95f, 0.22f);
            actionRect.offsetMin = actionRect.offsetMax = Vector2.zero;
            _actionParent = actionContainer.transform;

            CreateButton(panel.transform, "Leave", "BACK / LEAVE", 38, new Vector2(0.5f, 0.06f), LeaveTable);

            _network = Object.FindAnyObjectByType<NetworkManager>();
            if (_network != null)
            {
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
            if (_network == null)
                return;
            _network.PublicStateChanged -= HandlePublicState;
            _network.PrivateStateChanged -= HandlePrivateState;
            _network.CommandRejected -= HandleRejected;
            _network.RoomClosed -= HandleRoomClosed;
        }

        private void Update()
        {
            if (_network?.PublicGame?.ReactionDeadlineUtc == null)
            {
                _timer.text = "Reaction: --";
                return;
            }
            var seconds = (_network.PublicGame.ReactionDeadlineUtc.Value - DateTimeOffset.UtcNow).TotalSeconds;
            _timer.text = seconds > 0 ? $"Reaction: {Mathf.CeilToInt((float)seconds)}s" : "Reaction: closing";
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
                var label = slot.KnownCard == null ? "?" : FormatCard(slot.KnownCard);
                var card = CreateCard(_cardParent, i, count, label);
                card.Initialize(i);
                card.SetSelected(i == _selectedHandIndex);
                card.Selected += OnCardSelected;
                _cards.Add(card);
            }
        }

        private void OnCardSelected(CardView card)
        {
            var publicView = _network?.PrivateGame?.Public;
            if (publicView != null && NormalizePhase(publicView.Phase) == "WaitingForInitialPeeks")
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
                _selectedHandIndex = card.IsSelected ? card.CardIndex : -1;
                foreach (var candidate in _cards)
                    if (candidate != card)
                        candidate.SetSelected(false);
                _status.text = card.IsSelected ? $"Selected hand position {card.CardIndex}" : "Selection cleared";
            }
        }

        private void RenderActions(PlayerGameViewV1 privateView)
        {
            foreach (Transform child in _actionParent)
                Destroy(child.gameObject);

            var publicView = privateView.Public;
            if (publicView == null)
                return;

            var isLocalTurn = publicView.ActivePlayerId == _network?.LocalPlayerId;
            if (NormalizePhase(publicView.Phase) == "WaitingForInitialPeeks")
            {
                CreateActionButton("PEEK TWO CARDS", () => SubmitPeek());
                return;
            }
            if (!isLocalTurn)
            {
                if (NormalizePhase(publicView.Phase) == "AwaitingReactionResolution")
                    CreateActionButton("REACTION DONE", SubmitReactionDone);
                return;
            }

            if (NormalizePhase(publicView.Phase) == "AwaitingDraw")
                CreateActionButton("DRAW CARD", () => Submit("DrawCard"));
            else if (NormalizePhase(publicView.Phase) == "AwaitingDrawResolution")
            {
                CreateActionButton("DISCARD DRAWN", () => Submit("DiscardDrawnCard"));
                if (privateView.AvailableDrawResolutions != null)
                {
                    foreach (var option in privateView.AvailableDrawResolutions)
                    {
                        if (option == "Replace" || option == "1")
                            CreateActionButton("REPLACE SELECTED", SubmitReplace);
                        else if (option == "ActivateAbility" || option == "2")
                            CreateActionButton("ACTIVATE ABILITY", () => Submit("ActivateDrawnAbility"));
                    }
                }
            }
            else if (NormalizePhase(publicView.Phase) == "AwaitingPostResolutionDecision")
            {
                if (privateView.CanCallAlzheimer)
                    CreateActionButton("CALL ALZHEIMER", () => Submit("CallAlzheimer"));
                if (privateView.CanEndTurn)
                    CreateActionButton("END TURN", () => Submit("EndTurn"));
            }
        }

        private void SubmitPeek()
        {
            if (_selectedPeekIndices.Count != 2)
            {
                _status.text = "Select exactly two distinct card positions.";
                return;
            }
            var command = GameplayCommandV1.Create("PeekInitialCards");
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
            var command = GameplayCommandV1.Create("ReplaceHandCard");
            command.HandIndex = _selectedHandIndex;
            _network?.SubmitCommand(command);
        }

        private void Submit(string action)
        {
            _network?.SubmitCommand(GameplayCommandV1.Create(action));
        }

        private void SubmitReactionDone()
        {
            var windowId = _network?.PublicGame?.ReactionWindowId;
            if (!windowId.HasValue)
                return;
            var command = GameplayCommandV1.Create("MarkReactionDone");
            command.ReactionWindowId = windowId;
            _network?.SubmitCommand(command);
        }

        private void CreateActionButton(string label, UnityEngine.Events.UnityAction action)
        {
            var button = CreateButton(_actionParent, label, label, 34, new Vector2(0.5f, 0.5f), action);
            var rect = button.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(820f, 112f);
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

        private static string FormatPhase(string phase)
        {
            if (string.IsNullOrEmpty(phase))
                return "Unknown";
            return NormalizePhase(phase);
        }

        private static string NormalizePhase(string phase)
        {
            switch (phase)
            {
                case "0": return "WaitingForInitialPeeks";
                case "1": return "AwaitingDraw";
                case "2": return "AwaitingDrawResolution";
                case "3": return "AwaitingAbilityResolution";
                case "4": return "AwaitingPostResolutionDecision";
                case "5": return "AwaitingReactionResolution";
                case "6": return "FinalPhase";
                case "7": return "RoundCompleted";
                default: return phase;
            }
        }

        private static string FormatCard(CardViewV1 card)
        {
            if (card == null)
                return "?";
            var rank = CardRankName(card.Rank);
            var suit = CardSuitName(card.Suit);
            return suit == "" ? rank : rank + " " + suit;
        }

        private static string CardRankName(object rank)
        {
            var value = rank?.ToString();
            if (int.TryParse(value, out var number))
                value = number switch
                {
                    0 => "Joker", 1 => "A", 2 => "2", 3 => "3", 4 => "4", 5 => "5",
                    6 => "6", 7 => "7", 8 => "8", 9 => "9", 10 => "10",
                    11 => "J", 12 => "Q", 13 => "K", _ => "?"
                };
            return value == null ? "?" : value;
        }

        private static string CardSuitName(object suit)
        {
            var value = suit?.ToString();
            if (int.TryParse(value, out var number))
                value = number switch { 1 => "C", 2 => "D", 3 => "H", 4 => "S", _ => "" };
            return value == null ? "" : value;
        }

        private CardView CreateCard(Transform parent, int index, int count, string label)
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
            var text = CreateText(go.transform, "CardLabel", label, 54);
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

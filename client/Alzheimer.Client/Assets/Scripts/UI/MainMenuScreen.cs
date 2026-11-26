using Alzheimer.Client.Networking;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Alzheimer.Client.UI
{
    public sealed class MainMenuScreen : MonoBehaviour
    {
        private Text _status;
        private InputField _serverUrl;
        private InputField _name;
        private InputField _roomCode;
        private InputField _deckCount;
        private InputField _maxPlayers;
        private Text _lobbyText;
        private Button _startButton;
        private NetworkManager _network;

        public void Initialize(GameObject root)
        {
            var panel = CreatePanel(root.transform, "Background", new Color(0.035f, 0.055f, 0.09f));
            var title = CreateText(panel.transform, "Title", "ALZHEIMER", 84);
            SetAnchored(title.rectTransform, new Vector2(0.5f, 0.78f), new Vector2(0.5f, 0.78f), new Vector2(850f, 150f));

            _network = Object.FindAnyObjectByType<NetworkManager>();
            if (_network != null)
            {
                UnsubscribeNetworkEvents();
                _network.StatusChanged += HandleStatus;
                _network.LobbyChanged += HandleLobby;
                _network.CommandRejected += HandleStatus;
                _network.RoomClosed += HandleRoomClosed;
                _network.PublicStateChanged += HandleMatchStarted;
            }

            _status = CreateText(panel.transform, "Status", "Configure a server, name and room.", 28);
            SetAnchored(_status.rectTransform, new Vector2(0.5f, 0.66f), new Vector2(0.5f, 0.66f), new Vector2(850f, 80f));

            _serverUrl = CreateInput(panel.transform, "ServerURL", "Server URL", 0.5f, 0.58f);
            _serverUrl.text = PlayerPrefs.GetString("Alzheimer.ServerUrl", "http://192.168.1.10:5050");
            _name = CreateInput(panel.transform, "DisplayName", "Display name", 0.5f, 0.50f);
            _name.text = PlayerPrefs.GetString("Alzheimer.PlayerName", "Player");
            _deckCount = CreateInput(panel.transform, "DeckCount", "Deck count (1-4)", 0.30f, 0.42f);
            _deckCount.text = "1";
            _maxPlayers = CreateInput(panel.transform, "MaxPlayers", "Max players (2-10)", 0.70f, 0.42f);
            _maxPlayers.text = "4";

            CreateButton(panel.transform, "Connect", "CONNECT", 40, new Vector2(0.5f, 0.34f), Connect);
            var createRoom = CreateButton(panel.transform, "Create Room", "CREATE ROOM", 40, new Vector2(0.30f, 0.26f), CreateRoom);
            createRoom.GetComponent<RectTransform>().sizeDelta = new Vector2(420f, 112f);
            var joinRoom = CreateButton(panel.transform, "Join Room", "JOIN ROOM", 40, new Vector2(0.70f, 0.26f), JoinRoom);
            joinRoom.GetComponent<RectTransform>().sizeDelta = new Vector2(420f, 112f);
            _startButton = CreateButton(panel.transform, "Start Match", "START MATCH", 40, new Vector2(0.5f, 0.105f), StartMatch);
            _startButton.interactable = false;

            _roomCode = CreateInput(panel.transform, "RoomCode", "Room code", 0.5f, 0.20f);
            _lobbyText = CreateText(panel.transform, "Lobby", "", 26);
            SetAnchored(_lobbyText.rectTransform, new Vector2(0.5f, 0.025f), new Vector2(0.5f, 0.025f), new Vector2(930f, 70f));
        }

        private void OnDisable()
        {
            UnsubscribeNetworkEvents();
        }

        private void UnsubscribeNetworkEvents()
        {
            if (_network == null)
                return;
            _network.StatusChanged -= HandleStatus;
            _network.LobbyChanged -= HandleLobby;
            _network.CommandRejected -= HandleStatus;
            _network.RoomClosed -= HandleRoomClosed;
            _network.PublicStateChanged -= HandleMatchStarted;
        }

        private void Connect()
        {
            PlayerPrefs.SetString("Alzheimer.ServerUrl", _serverUrl.text.Trim());
            PlayerPrefs.SetString("Alzheimer.PlayerName", _name.text.Trim());
            _network?.Connect(_serverUrl.text.Trim());
        }

        private void CreateRoom()
        {
            if (!ValidateName()) return;
            if (!int.TryParse(_deckCount.text, out var decks) || decks < 1 || decks > 4)
            { HandleStatus("Deck count must be 1-4."); return; }
            if (!int.TryParse(_maxPlayers.text, out var capacity) || capacity < 2 || capacity > 10)
            { HandleStatus("Max players must be 2-10."); return; }
            _network?.CreateRoom(_name.text.Trim(), decks, capacity);
        }

        private void JoinRoom()
        {
            if (!ValidateName()) return;
            if (string.IsNullOrWhiteSpace(_roomCode.text))
            { HandleStatus("Enter a room code."); return; }
            _network?.JoinRoom(_roomCode.text.Trim(), _name.text.Trim());
        }

        private bool ValidateName()
        {
            if (string.IsNullOrWhiteSpace(_name.text) || _name.text.Trim().Length > 24)
            { HandleStatus("Display name must be 1-24 characters."); return false; }
            return true;
        }

        private void HandleStatus(string message)
        {
            _status.text = message;
        }

        private void HandleLobby(Alzheimer.Client.Networking.LobbySnapshotV1 lobby)
        {
            var names = new System.Collections.Generic.List<string>();
            if (lobby.Players != null)
                foreach (var player in lobby.Players)
                {
                    var playerTag = string.IsNullOrEmpty(player.PlayerId)
                        ? ""
                        : " [" + player.PlayerId.Substring(0, System.Math.Min(6, player.PlayerId.Length)) + "]";
                    names.Add(player.DisplayName + playerTag
                        + (player.IsHost ? " (host)" : "")
                        + (player.PlayerId == lobby.LocalPlayerId ? " (you)" : ""));
                }
            var roster = string.Join(" | ", names);
            _lobbyText.text = $"Room {lobby.Code}  |  {lobby.Players?.Length ?? 0}/{lobby.MaxPlayers}  |  {lobby.DeckCount} deck(s)\n{roster}";
            _roomCode.text = lobby.Code;
            if (_startButton != null)
            {
                var host = lobby.Players == null ? null : System.Array.Find(lobby.Players, player => player.IsHost);
                _startButton.interactable = lobby.LocalPlayerId != null
                    && lobby.LocalPlayerId == host?.PlayerId
                    && lobby.Players != null
                    && lobby.Players.Length >= 2;
            }
        }

        private void HandleMatchStarted(PublicGameViewV1 view)
        {
            if (view != null)
                SceneManager.LoadScene("GameTable");
        }

        private void HandleRoomClosed()
        {
            _lobbyText.text = "";
            if (_startButton != null)
                _startButton.interactable = false;
            HandleStatus("The active match ended because a player disconnected.");
        }

        private void StartMatch()
        {
            _network?.StartMatch();
        }

        private static InputField CreateInput(Transform parent, string name, string placeholder, float x, float y)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rect = go.AddComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(x, y);
            rect.sizeDelta = new Vector2(x == 0.5f ? 700f : 330f, 86f);
            var image = go.AddComponent<Image>();
            image.color = new Color(0.96f, 0.96f, 0.96f);
            var input = go.AddComponent<InputField>();
            input.targetGraphic = image;

            var textObject = new GameObject("Text");
            textObject.transform.SetParent(go.transform, false);
            var textRect = textObject.AddComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(18f, 8f);
            textRect.offsetMax = new Vector2(-18f, -8f);
            var text = textObject.AddComponent<Text>();
            text.text = "";
            text.font = GetFont();
            text.fontSize = 34;
            text.color = Color.black;
            text.alignment = TextAnchor.MiddleLeft;

            var placeholderObject = new GameObject("Placeholder");
            placeholderObject.transform.SetParent(go.transform, false);
            var placeholderRect = placeholderObject.AddComponent<RectTransform>();
            placeholderRect.anchorMin = Vector2.zero;
            placeholderRect.anchorMax = Vector2.one;
            placeholderRect.offsetMin = new Vector2(18f, 8f);
            placeholderRect.offsetMax = new Vector2(-18f, -8f);
            var placeholderText = placeholderObject.AddComponent<Text>();
            placeholderText.text = placeholder;
            placeholderText.font = GetFont();
            placeholderText.fontSize = 30;
            placeholderText.color = new Color(0.35f, 0.35f, 0.35f);

            input.textComponent = text;
            input.placeholder = placeholderText;
            return input;
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
            text.font = GetFont();
            text.fontSize = fontSize;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            return text;
        }

        private Button CreateButton(Transform parent, string name, string label, int fontSize, Vector2 anchor, UnityEngine.Events.UnityAction action)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rect = go.AddComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = anchor;
            rect.sizeDelta = new Vector2(700f, 132f);
            var image = go.AddComponent<Image>();
            image.color = new Color(0.16f, 0.38f, 0.58f);
            var button = go.AddComponent<Button>();
            button.onClick.AddListener(action);
            button.targetGraphic = image;

            var text = CreateText(go.transform, "Label", label, fontSize);
            var textRect = text.rectTransform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;
            return button;
        }

        private static void SetAnchored(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 size)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = size;
        }

        private static Font GetFont()
        {
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font == null)
                font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            return font;
        }
    }
}

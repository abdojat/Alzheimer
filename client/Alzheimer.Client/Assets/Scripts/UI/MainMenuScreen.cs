using Alzheimer.Client.Networking;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Alzheimer.Client.UI
{
    public sealed class MainMenuScreen : MonoBehaviour
    {
        private Text _status;

        public void Initialize(GameObject root)
        {
            var panel = CreatePanel(root.transform, "Background", new Color(0.035f, 0.055f, 0.09f));
            var title = CreateText(panel.transform, "Title", "ALZHEIMER", 84);
            SetAnchored(title.rectTransform, new Vector2(0.5f, 0.78f), new Vector2(0.5f, 0.78f), new Vector2(850f, 150f));

            _status = CreateText(panel.transform, "Status", "Local preview - multiplayer is not configured", 32);
            SetAnchored(_status.rectTransform, new Vector2(0.5f, 0.64f), new Vector2(0.5f, 0.64f), new Vector2(850f, 90f));

            CreateButton(panel.transform, "Play", "PLAY", 52, new Vector2(0.5f, 0.54f), () => LoadGameTable());
            CreateButton(panel.transform, "Create Room", "CREATE ROOM", 46, new Vector2(0.5f, 0.44f), () => ShowStatus("Room creation requires the multiplayer server."));
            CreateButton(panel.transform, "Join Room", "JOIN ROOM", 46, new Vector2(0.5f, 0.34f), () => ShowStatus("Room joining requires the multiplayer server."));
            CreateButton(panel.transform, "Settings", "SETTINGS", 46, new Vector2(0.5f, 0.24f), () => ShowStatus("Settings will be available in a later milestone."));
        }

        private void LoadGameTable()
        {
            SceneManager.LoadScene("GameTable");
        }

        private void ShowStatus(string message)
        {
            _status.text = message;
            Debug.Log("Alzheimer menu: " + message);
            var network = FindObjectOfType<NetworkManager>();
            if (network != null)
                network.Disconnect();
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

        private void CreateButton(Transform parent, string name, string label, int fontSize, Vector2 anchor, UnityEngine.Events.UnityAction action)
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

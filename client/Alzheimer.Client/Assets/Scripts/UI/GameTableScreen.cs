using System.Collections.Generic;
using Alzheimer.Client.Game;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Alzheimer.Client.UI
{
    public sealed class GameTableScreen : MonoBehaviour
    {
        public void Initialize(GameObject root)
        {
            var panel = CreatePanel(root.transform, "Table", new Color(0.08f, 0.24f, 0.16f));

            var title = CreateText(panel.transform, "CurrentPlayer", "Current player: You", 42);
            SetAnchored(title.rectTransform, new Vector2(0.5f, 0.92f), new Vector2(0.5f, 0.92f), new Vector2(900f, 90f));

            var turn = CreateText(panel.transform, "Turn", "Turn: Your turn", 36);
            SetAnchored(turn.rectTransform, new Vector2(0.5f, 0.86f), new Vector2(0.5f, 0.86f), new Vector2(900f, 70f));

            CreatePile(panel.transform, "Draw Pile", "DRAW", new Vector2(0.29f, 0.70f));
            CreatePile(panel.transform, "Discard Pile", "DISCARD", new Vector2(0.71f, 0.70f));

            var timer = CreateText(panel.transform, "ReactionTimer", "Reaction: --", 38);
            SetAnchored(timer.rectTransform, new Vector2(0.5f, 0.56f), new Vector2(0.5f, 0.56f), new Vector2(900f, 80f));

            var cards = new List<CardView>();
            const float startX = 0.16f;
            const float spacing = 0.226f;
            for (var i = 0; i < 4; i++)
            {
                var card = CreateCard(panel.transform, i, new Vector2(startX + spacing * i, 0.38f));
                cards.Add(card);
            }

            var status = CreateText(panel.transform, "Status", "Tap a card to select it", 34);
            SetAnchored(status.rectTransform, new Vector2(0.5f, 0.20f), new Vector2(0.5f, 0.20f), new Vector2(900f, 90f));

            var interaction = panel.gameObject.AddComponent<CardInteractionController>();
            interaction.Initialize(cards, status);

            CreateButton(panel.transform, "Back", "BACK", 42, new Vector2(0.5f, 0.09f), () => SceneManager.LoadScene("MainMenu"));

            var timerController = Object.FindAnyObjectByType<ReactionTimerController>();
            if (timerController != null)
            {
                timerController.StartTimer(30f);
                timerController.StopTimer();
            }
        }

        private static CardView CreateCard(Transform parent, int index, Vector2 anchor)
        {
            var go = new GameObject("PlayerCard" + (index + 1));
            go.transform.SetParent(parent, false);
            var rect = go.AddComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = anchor;
            rect.sizeDelta = new Vector2(190f, 270f);
            var image = go.AddComponent<Image>();
            image.color = new Color(0.92f, 0.92f, 0.92f);
            var button = go.AddComponent<Button>();
            button.targetGraphic = image;

            var label = CreateText(go.transform, "CardLabel", "?", 72);
            var labelRect = label.rectTransform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;
            label.color = new Color(0.12f, 0.12f, 0.12f);

            var view = go.AddComponent<CardView>();
            view.Initialize(index);
            return view;
        }

        private static void CreatePile(Transform parent, string name, string label, Vector2 anchor)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rect = go.AddComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = anchor;
            rect.sizeDelta = new Vector2(260f, 360f);
            var image = go.AddComponent<Image>();
            image.color = name == "Draw Pile"
                ? new Color(0.18f, 0.18f, 0.22f)
                : new Color(0.78f, 0.72f, 0.62f);

            var text = CreateText(go.transform, "PileLabel", label, 38);
            var textRect = text.rectTransform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;
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

        private static void CreateButton(Transform parent, string name, string label, int fontSize, Vector2 anchor, UnityEngine.Events.UnityAction action)
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

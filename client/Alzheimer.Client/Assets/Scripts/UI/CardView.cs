using System;
using UnityEngine;
using UnityEngine.UI;

namespace Alzheimer.Client.UI
{
    public sealed class CardView : MonoBehaviour
    {
        public int CardIndex { get; private set; }
        public bool IsSelected { get; private set; }

        private Button _button;
        private Text _label;
        private Image _image;

        public event Action<CardView> Selected;

        public void Initialize(int cardIndex)
        {
            CardIndex = cardIndex;
            _button = GetComponent<Button>();
            _label = GetComponentInChildren<Text>(true);
            _image = GetComponent<Image>();
            if (_button == null)
                throw new InvalidOperationException("CardView requires a Button component.");
            _button.onClick.AddListener(OnTapped);
            SetSelected(false);
        }

        private void OnTapped()
        {
            SetSelected(!IsSelected);
            Selected?.Invoke(this);
        }

        public void SetSelected(bool selected)
        {
            IsSelected = selected;
            if (_image != null)
                _image.color = selected ? new Color(1f, 0.78f, 0.28f) : new Color(0.92f, 0.92f, 0.92f);
            if (_label != null)
                _label.text = selected ? $"? \n{CardIndex + 1}" : "?";
        }
    }
}

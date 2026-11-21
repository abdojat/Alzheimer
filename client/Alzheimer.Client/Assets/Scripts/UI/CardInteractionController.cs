using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Alzheimer.Client.UI
{
    public sealed class CardInteractionController : MonoBehaviour
    {
        private readonly List<CardView> _cards = new List<CardView>();
        private Text _status;

        public void Initialize(IReadOnlyList<CardView> cards, Text status)
        {
            _status = status;
            _cards.Clear();
            foreach (var card in cards)
            {
                _cards.Add(card);
                card.Selected += HandleCardSelected;
            }
        }

        private void HandleCardSelected(CardView selected)
        {
            foreach (var card in _cards)
            {
                if (card != selected)
                    card.SetSelected(false);
            }

            _status.text = selected.IsSelected
                ? $"Selected card {selected.CardIndex + 1}"
                : "Card selection cleared";
        }
    }
}

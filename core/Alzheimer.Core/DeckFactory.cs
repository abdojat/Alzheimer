namespace Alzheimer.Core;

public static class DeckFactory
{
    public static List<PlayingCard> CreateShuffled(int deckCount, Random? random = null)
    {
        if (deckCount < 1 || deckCount > 4)
            throw new ArgumentOutOfRangeException(nameof(deckCount), "Choose between 1 and 4 decks.");

        random ??= Random.Shared;
        var cards = new List<PlayingCard>(deckCount * 54);
        for (var deck = 0; deck < deckCount; deck++)
        {
            foreach (CardSuit suit in new[] { CardSuit.Clubs, CardSuit.Diamonds, CardSuit.Hearts, CardSuit.Spades })
                for (var rank = (int)CardRank.Ace; rank <= (int)CardRank.King; rank++)
                    cards.Add(new PlayingCard(Guid.NewGuid(), (CardRank)rank, suit));

            for (var joker = 0; joker < 2; joker++)
                cards.Add(new PlayingCard(Guid.NewGuid(), CardRank.Joker, CardSuit.None));
        }

        // Fisher–Yates shuffle. Random is injectable for deterministic tests.
        for (var i = cards.Count - 1; i > 0; i--)
        {
            var j = random.Next(i + 1);
            (cards[i], cards[j]) = (cards[j], cards[i]);
        }
        return cards;
    }
}

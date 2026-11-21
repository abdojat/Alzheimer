namespace Alzheimer.Core;

public enum CardSuit { None = 0, Clubs = 1, Diamonds = 2, Hearts = 3, Spades = 4 }
public enum CardRank
{
    Joker = 0, Ace = 1, Two = 2, Three = 3, Four = 4, Five = 5,
    Six = 6, Seven = 7, Eight = 8, Nine = 9, Ten = 10,
    Jack = 11, Queen = 12, King = 13
}

// PhysicalCardId distinguishes identical copies across multiple 54-card decks.
public sealed record PlayingCard(Guid PhysicalCardId, CardRank Rank, CardSuit Suit)
{
    public int Points => Rank switch
    {
        CardRank.Joker => -1,
        CardRank.Ten => 0,
        CardRank.Jack or CardRank.Queen or CardRank.King => 20,
        _ => (int)Rank
    };
}

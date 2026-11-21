namespace Alzheimer.Core;

public sealed record RoomSettings
{
    public int MaxPlayers { get; }
    public int DeckCount { get; }

    public RoomSettings(int maxPlayers, int deckCount)
    {
        if (maxPlayers is < 2 or > 10)
            throw new ArgumentOutOfRangeException(nameof(maxPlayers), "Use 2–10 players.");
        if (deckCount is < 1 or > 4)
            throw new ArgumentOutOfRangeException(nameof(deckCount), "Use 1–4 decks.");
        MaxPlayers = maxPlayers;
        DeckCount = deckCount;
    }
}

using Alzheimer.Core;

static void Check(bool value, string description)
{
    if (!value) throw new Exception("FAILED: " + description);
    Console.WriteLine("PASS: " + description);
}

var oneDeck = DeckFactory.CreateShuffled(1, new Random(12));
var fourDecks = DeckFactory.CreateShuffled(4, new Random(12));
Check(oneDeck.Count == 54, "one deck = 54 cards");
Check(fourDecks.Count == 216, "four decks = 216 cards");
Check(fourDecks.Count(c => c.Rank == CardRank.Joker) == 8, "two jokers per deck");
Check(fourDecks.Select(c => c.PhysicalCardId).Distinct().Count() == 216, "physical card IDs are unique");
Check(new PlayingCard(Guid.NewGuid(), CardRank.Joker, CardSuit.None).Points == -1, "Joker is -1");
Check(new PlayingCard(Guid.NewGuid(), CardRank.Ten, CardSuit.Hearts).Points == 0, "10 is zero");
Check(new PlayingCard(Guid.NewGuid(), CardRank.King, CardSuit.Spades).Points == 20, "King is 20");
Check(new PlayingCard(Guid.NewGuid(), CardRank.Ace, CardSuit.Spades).Points == 1, "Ace is 1");
Check(ReactionRules.MatchesTop(CardRank.Seven, CardRank.Seven), "matching rank succeeds");
Check(!ReactionRules.MatchesTop(CardRank.Eight, CardRank.Seven), "same suit alone never matters");
Check(ReactionRules.MatchesQueenWindow(CardRank.Queen, CardRank.Seven), "Queen always valid in Queen window");
Check(ReactionRules.MatchesQueenWindow(CardRank.Seven, CardRank.Seven), "Queen's discarded rank valid");
Check(!ReactionRules.MatchesQueenWindow(CardRank.Eight, CardRank.Seven), "other rank invalid in Queen window");

var scores = new[] { new PlayerRoundBase("A", 3, 0), new PlayerRoundBase("B", 8, 0), new PlayerRoundBase("C", 12, 0) };
var callerWon = RoundScorer.Calculate(scores, "A");
Check(callerWon["A"] == 3 && callerWon["B"] == 16 && callerWon["C"] == 24, "unique winning caller doubles others");
var callerTied = RoundScorer.Calculate(new[] { new PlayerRoundBase("A", 7, 0), new PlayerRoundBase("B", 7, 0) }, "A");
Check(callerTied["A"] == 14 && callerTied["B"] == 7, "tied caller doubles own score");
var depletion = RoundScorer.Calculate(new[] { new PlayerRoundBase("A", 3, 10), new PlayerRoundBase("B", 8, 0) }, null);
Check(depletion["A"] == 26 && depletion["B"] == 16, "undrawn penalty before doubling");
Check(RoundScorer.IsMatchOver(new[] { 44, 100, 81 }), "score reaching 100 ends match");
Check(!RoundScorer.IsMatchOver(new[] { 44, 99, 81 }), "below 100 does not end match");

RoundEngineTests.Run(Check);
SpecialAbilityTests.Run(Check);

Console.WriteLine("All smoke tests passed.");

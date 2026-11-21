namespace Alzheimer.Core;

public static class ReactionRules
{
    // Ordinary reaction: rank-only match against the current discard-pile top.
    public static bool MatchesTop(CardRank attempted, CardRank discardTop) => attempted == discardTop;

    // A Queen ability keeps BOTH ranks valid for the whole reaction window.
    // The ability-discarded card becomes the top AFTER that window closes.
    public static bool MatchesQueenWindow(CardRank attempted, CardRank queenDiscardedRank) =>
        attempted == CardRank.Queen || attempted == queenDiscardedRank;
}

namespace Alzheimer.Core;

public sealed record PlayerRoundBase(string PlayerId, int CardPoints, int UndrawnPenaltyPoints)
{
    public int BasePoints => checked(CardPoints + UndrawnPenaltyPoints);
}

public static class RoundScorer
{
    // Provide callerId for manual or empty-hand auto-call. Null means pile exhaustion with no caller.
    // UndrawnPenaltyPoints are included BEFORE score doubling.
    public static IReadOnlyDictionary<string, int> Calculate(
        IReadOnlyList<PlayerRoundBase> players,
        string? callerId)
    {
        if (players.Count < 2)
            throw new ArgumentException("At least two player scores are required.", nameof(players));
        if (players.Select(p => p.PlayerId).Distinct(StringComparer.Ordinal).Count() != players.Count)
            throw new ArgumentException("Player IDs must be unique.", nameof(players));
        if (callerId is not null && !players.Any(p => p.PlayerId == callerId))
            throw new ArgumentException("Caller must be in the round.", nameof(callerId));

        var scores = players.ToDictionary(p => p.PlayerId, p => p.BasePoints, StringComparer.Ordinal);
        if (callerId is null)
            return scores.ToDictionary(pair => pair.Key, pair => checked(pair.Value * 2), StringComparer.Ordinal);

        var lowest = scores.Values.Min();
        var callerWonUniquely = scores[callerId] == lowest && scores.Values.Count(value => value == lowest) == 1;

        return scores.ToDictionary(
            pair => pair.Key,
            pair => checked((callerWonUniquely ? pair.Key != callerId : pair.Key == callerId)
                ? pair.Value * 2 : pair.Value),
            StringComparer.Ordinal);
    }

    public static bool IsMatchOver(IEnumerable<int> cumulativeScores) =>
        cumulativeScores.Any(score => score >= 100);
}

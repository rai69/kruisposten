namespace Triodos.KruispostMonitor.Matching;

public static class StringSimilarity
{
    public static double Calculate(string? a, string? b)
    {
        if (a is null && b is null) return 1.0;
        if (a is null || b is null) return 0.0;

        a = a.ToLowerInvariant();
        b = b.ToLowerInvariant();

        if (a == b) return 1.0;
        if (a.Length == 0 || b.Length == 0) return 0.0;

        var charScore = CharacterSimilarity(a, b);
        var wordScore = WordOverlapScore(a, b);
        return Math.Max(charScore, wordScore);
    }

    private static double CharacterSimilarity(string a, string b)
    {
        var maxLen = Math.Max(a.Length, b.Length);
        var distance = LevenshteinDistance(a, b);
        return 1.0 - (double)distance / maxLen;
    }

    private static double WordOverlapScore(string a, string b)
    {
        var wordsA = a.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        var wordsB = b.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();

        if (wordsA.Count == 0 || wordsB.Count == 0) return 0.0;

        var intersection = wordsA.Intersect(wordsB).Count();
        return (2.0 * intersection) / (wordsA.Count + wordsB.Count);
    }

    private static int LevenshteinDistance(string a, string b)
    {
        var m = a.Length;
        var n = b.Length;
        var dp = new int[m + 1, n + 1];

        for (var i = 0; i <= m; i++) dp[i, 0] = i;
        for (var j = 0; j <= n; j++) dp[0, j] = j;

        for (var i = 1; i <= m; i++)
        {
            for (var j = 1; j <= n; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                dp[i, j] = Math.Min(
                    Math.Min(dp[i - 1, j] + 1, dp[i, j - 1] + 1),
                    dp[i - 1, j - 1] + cost);
            }
        }

        return dp[m, n];
    }
}

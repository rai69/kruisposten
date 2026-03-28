using Triodos.KruispostMonitor.Configuration;

namespace Triodos.KruispostMonitor.Matching;

public class TransactionMatcher
{
    private readonly MatchingSettings _settings;
    private const double PossibleMatchMinThreshold = 0.3;

    public TransactionMatcher(MatchingSettings settings)
    {
        _settings = settings;
    }

    public MatchResult Match(IReadOnlyList<TransactionRecord> transactions, IEnumerable<string> alreadyMatchedIds)
    {
        var excludedIds = alreadyMatchedIds.ToHashSet();

        var debits = transactions
            .Where(t => t.IsDebit && !excludedIds.Contains(t.Id))
            .ToList();

        var availableCredits = transactions
            .Where(t => t.IsCredit && !excludedIds.Contains(t.Id))
            .ToList();

        var matched = new List<MatchedPair>();
        var possibleMatches = new List<PossibleMatch>();
        var usedCreditIds = new HashSet<string>();

        foreach (var debit in debits)
        {
            var bestCredit = FindBestCredit(debit, availableCredits, usedCreditIds);

            if (bestCredit is null)
                continue;

            if (bestCredit.Value.score >= _settings.SimilarityThreshold)
            {
                matched.Add(new MatchedPair(debit, bestCredit.Value.credit, bestCredit.Value.score));
                usedCreditIds.Add(bestCredit.Value.credit.Id);
            }
            else if (bestCredit.Value.score >= PossibleMatchMinThreshold)
            {
                possibleMatches.Add(new PossibleMatch(debit, bestCredit.Value.credit, bestCredit.Value.score));
            }
        }

        var matchedDebitIds = matched.Select(m => m.Debit.Id).ToHashSet();
        var unmatchedDebits = debits
            .Where(d => !matchedDebitIds.Contains(d.Id))
            .ToList();

        var unmatchedCredits = availableCredits
            .Where(c => !usedCreditIds.Contains(c.Id))
            .ToList();

        return new MatchResult
        {
            Matched = matched,
            UnmatchedDebits = unmatchedDebits,
            UnmatchedCredits = unmatchedCredits,
            PossibleMatches = possibleMatches
        };
    }

    private static (TransactionRecord credit, double score)? FindBestCredit(
        TransactionRecord debit,
        List<TransactionRecord> credits,
        HashSet<string> usedCreditIds)
    {
        (TransactionRecord credit, double score)? best = null;

        foreach (var credit in credits)
        {
            if (usedCreditIds.Contains(credit.Id))
                continue;

            if (credit.AbsoluteAmount != debit.AbsoluteAmount)
                continue;

            // Amount match gives a base score of 0.5 (enough to exceed PossibleMatchMinThreshold)
            // Name/reference similarity adds up to 0.4
            // Date proximity adds up to 0.1 (closer = higher)
            var nameSimilarity = StringSimilarity.Calculate(debit.CounterpartName, credit.CounterpartName);
            var refSimilarity = StringSimilarity.Calculate(debit.RemittanceInformation, credit.RemittanceInformation);
            var textSimilarity = Math.Max(nameSimilarity, refSimilarity);

            var daysDiff = Math.Abs((credit.ExecutionDate - debit.ExecutionDate).TotalDays);
            var dateProximity = daysDiff <= 7 ? (7 - daysDiff) / 7.0 : 0;

            var score = 0.5 + (textSimilarity * 0.4) + (dateProximity * 0.1);

            if (best is null || score > best.Value.score)
            {
                best = (credit, score);
            }
        }

        return best;
    }
}

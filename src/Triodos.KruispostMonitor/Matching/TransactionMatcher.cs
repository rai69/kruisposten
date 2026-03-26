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

            if (bestCredit.Value.similarity >= _settings.SimilarityThreshold)
            {
                matched.Add(new MatchedPair(debit, bestCredit.Value.credit, bestCredit.Value.similarity));
                usedCreditIds.Add(bestCredit.Value.credit.Id);
            }
            else if (bestCredit.Value.similarity >= PossibleMatchMinThreshold)
            {
                possibleMatches.Add(new PossibleMatch(debit, bestCredit.Value.credit, bestCredit.Value.similarity));
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

    private static (TransactionRecord credit, double similarity)? FindBestCredit(
        TransactionRecord debit,
        List<TransactionRecord> credits,
        HashSet<string> usedCreditIds)
    {
        (TransactionRecord credit, double similarity)? best = null;

        foreach (var credit in credits)
        {
            if (usedCreditIds.Contains(credit.Id))
                continue;

            if (credit.AbsoluteAmount != debit.AbsoluteAmount)
                continue;

            var nameSimilarity = StringSimilarity.Calculate(debit.CounterpartName, credit.CounterpartName);
            var refSimilarity = StringSimilarity.Calculate(debit.RemittanceInformation, credit.RemittanceInformation);
            var similarity = Math.Max(nameSimilarity, refSimilarity);

            if (best is null || similarity > best.Value.similarity ||
                (Math.Abs(similarity - best.Value.similarity) < 0.001 &&
                 Math.Abs((credit.ExecutionDate - debit.ExecutionDate).TotalDays) <
                 Math.Abs((best.Value.credit.ExecutionDate - debit.ExecutionDate).TotalDays)))
            {
                best = (credit, similarity);
            }
        }

        return best;
    }
}

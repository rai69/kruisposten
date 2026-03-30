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
        var remainingDebits = debits
            .Where(d => !matchedDebitIds.Contains(d.Id))
            .ToList();

        var remainingCredits = availableCredits
            .Where(c => !usedCreditIds.Contains(c.Id))
            .ToList();

        // Apply split match rules
        var splitMatches = new List<SplitMatch>();
        foreach (var rule in _settings.SplitRules)
        {
            ApplySplitRule(rule, remainingCredits, remainingDebits, splitMatches);
        }

        return new MatchResult
        {
            Matched = matched,
            SplitMatches = splitMatches,
            UnmatchedDebits = remainingDebits,
            UnmatchedCredits = remainingCredits,
            PossibleMatches = possibleMatches
        };
    }

    private static void ApplySplitRule(
        SplitMatchRule rule,
        List<TransactionRecord> credits,
        List<TransactionRecord> debits,
        List<SplitMatch> splitMatches)
    {
        // Find all credits matching the rule amount
        var matchingCredits = credits
            .Where(c => c.AbsoluteAmount == rule.CreditAmount
                && (rule.TransactionType is null || c.TransactionType == rule.TransactionType))
            .ToList();

        foreach (var credit in matchingCredits)
        {
            // Find debits matching each rule amount, close in date to the credit
            var matchedDebits = new List<TransactionRecord>();
            var usedDebitIds = new HashSet<string>();
            var allFound = true;

            foreach (var debitAmount in rule.DebitAmounts)
            {
                var debit = debits.FirstOrDefault(d =>
                    !usedDebitIds.Contains(d.Id)
                    && d.AbsoluteAmount == debitAmount
                    && (rule.TransactionType is null || d.TransactionType == rule.TransactionType)
                    && Math.Abs((d.ExecutionDate - credit.ExecutionDate).TotalDays) <= 3);

                if (debit is null)
                {
                    allFound = false;
                    break;
                }

                matchedDebits.Add(debit);
                usedDebitIds.Add(debit.Id);
            }

            if (!allFound)
                continue;

            splitMatches.Add(new SplitMatch(credit, matchedDebits));
            credits.Remove(credit);
            foreach (var d in matchedDebits)
                debits.Remove(d);
        }
    }

    private static bool HasAnySharedWord(TransactionRecord a, TransactionRecord b)
    {
        return StringSimilarity.HasSharedWord(a.CounterpartName, b.CounterpartName)
            || StringSimilarity.HasSharedWord(a.RemittanceInformation, b.RemittanceInformation)
            || StringSimilarity.HasSharedWord(a.CounterpartName, b.RemittanceInformation)
            || StringSimilarity.HasSharedWord(a.RemittanceInformation, b.CounterpartName);
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

            // Require at least one shared word (3+ chars) between debit and credit text (any field combination)
            if (!HasAnySharedWord(debit, credit))
                continue;

            // Score components:
            // - Amount match: base 0.4
            // - Text similarity: up to 0.5 (best of same-field and cross-field comparisons)
            // - Date proximity: up to 0.1
            var nameSimilarity = StringSimilarity.Calculate(debit.CounterpartName, credit.CounterpartName);
            var refSimilarity = StringSimilarity.Calculate(debit.RemittanceInformation, credit.RemittanceInformation);
            var crossSimilarity1 = StringSimilarity.Calculate(debit.CounterpartName, credit.RemittanceInformation);
            var crossSimilarity2 = StringSimilarity.Calculate(debit.RemittanceInformation, credit.CounterpartName);
            var allText = $"{debit.CounterpartName} {debit.RemittanceInformation}";
            var allTextCredit = $"{credit.CounterpartName} {credit.RemittanceInformation}";
            var combinedSimilarity = StringSimilarity.Calculate(allText, allTextCredit);
            var textSimilarity = new[] { nameSimilarity, refSimilarity, crossSimilarity1, crossSimilarity2, combinedSimilarity }.Max();

            var daysDiff = Math.Abs((credit.ExecutionDate - debit.ExecutionDate).TotalDays);
            var dateProximity = daysDiff <= 7 ? (7 - daysDiff) / 7.0 : 0;

            var score = 0.4 + (textSimilarity * 0.5) + (dateProximity * 0.1);

            if (best is null || score > best.Value.score)
            {
                best = (credit, score);
            }
        }

        return best;
    }
}

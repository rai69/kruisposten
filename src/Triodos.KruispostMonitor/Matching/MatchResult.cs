namespace Triodos.KruispostMonitor.Matching;

public record TransactionRecord(
    string Id,
    decimal Amount,
    string CounterpartName,
    string RemittanceInformation,
    DateTimeOffset ExecutionDate,
    string TransactionType = "")
{
    public bool IsDebit => Amount < 0;
    public bool IsCredit => Amount > 0;
    public decimal AbsoluteAmount => Math.Abs(Amount);
}

public record MatchedPair(TransactionRecord Debit, TransactionRecord Credit, double Similarity);

public record PossibleMatch(TransactionRecord Debit, TransactionRecord Credit, double Similarity);

public class MatchResult
{
    public List<MatchedPair> Matched { get; init; } = [];
    public List<TransactionRecord> UnmatchedDebits { get; init; } = [];
    public List<TransactionRecord> UnmatchedCredits { get; init; } = [];
    public List<PossibleMatch> PossibleMatches { get; init; } = [];
}

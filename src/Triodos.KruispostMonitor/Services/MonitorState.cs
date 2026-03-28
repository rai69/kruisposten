using Triodos.KruispostMonitor.Matching;
using Triodos.KruispostMonitor.State;

namespace Triodos.KruispostMonitor.Services;

public record ProcessingRun(
    DateTimeOffset Timestamp,
    string FileName,
    int TransactionCount,
    int AutoMatched,
    int UnmatchedDebits,
    int UnmatchedCredits);

public class MonitorState
{
    private readonly object _lock = new();

    public MatchResult? CurrentMatchResult { get; private set; }
    public List<TransactionRecord> AllTransactions { get; private set; } = [];
    public decimal CurrentBalance { get; private set; }
    public string Currency { get; private set; } = "EUR";
    public string AccountIdentifier { get; private set; } = "";
    public RunState State { get; private set; } = new();
    public List<ProcessingRun> History { get; private set; } = [];
    public string? LastProcessedFile { get; private set; }
    public bool IsWatching { get; set; }

    // Pending manual matches (not yet saved to state)
    public List<ManualMatch> PendingManualMatches { get; private set; } = [];
    public List<TransactionRecord> UnmatchedDebits { get; private set; } = [];
    public List<TransactionRecord> UnmatchedCredits { get; private set; } = [];

    public void UpdateFromProcessing(
        MatchResult matchResult,
        List<TransactionRecord> allTransactions,
        decimal currentBalance,
        string currency,
        string accountIdentifier,
        RunState state,
        string fileName)
    {
        lock (_lock)
        {
            CurrentMatchResult = matchResult;
            AllTransactions = allTransactions;
            CurrentBalance = currentBalance;
            Currency = currency;
            AccountIdentifier = accountIdentifier;
            State = state;
            LastProcessedFile = fileName;
            PendingManualMatches = [];
            UnmatchedDebits = new List<TransactionRecord>(matchResult.UnmatchedDebits);
            UnmatchedCredits = new List<TransactionRecord>(matchResult.UnmatchedCredits);

            History.Insert(0, new ProcessingRun(
                DateTimeOffset.UtcNow,
                Path.GetFileName(fileName),
                allTransactions.Count,
                matchResult.Matched.Count,
                matchResult.UnmatchedDebits.Count,
                matchResult.UnmatchedCredits.Count));

            // Keep last 50 runs
            if (History.Count > 50)
                History.RemoveRange(50, History.Count - 50);
        }
    }

    public bool TryAddManualMatch(List<string> debitIds, List<string> creditIds, out string? error)
    {
        lock (_lock)
        {
            var debits = UnmatchedDebits.Where(t => debitIds.Contains(t.Id)).ToList();
            var credits = UnmatchedCredits.Where(t => creditIds.Contains(t.Id)).ToList();

            if (debits.Count == 0 || credits.Count == 0)
            {
                error = "Must select at least one debit and one credit";
                return false;
            }

            var net = debits.Sum(t => t.Amount) + credits.Sum(t => t.Amount);
            if (Math.Abs(net) >= 0.005m)
            {
                error = $"Selection does not balance: {net:F2}";
                return false;
            }

            PendingManualMatches.Add(new ManualMatch(debitIds, creditIds));
            UnmatchedDebits.RemoveAll(t => debitIds.Contains(t.Id));
            UnmatchedCredits.RemoveAll(t => creditIds.Contains(t.Id));
            error = null;
            return true;
        }
    }

    public bool TryUndoManualMatch(int index, out string? error)
    {
        lock (_lock)
        {
            if (index < 0 || index >= PendingManualMatches.Count)
            {
                error = "Invalid match index";
                return false;
            }

            var mm = PendingManualMatches[index];
            var debits = mm.DebitIds.Select(id => AllTransactions.First(t => t.Id == id));
            var credits = mm.CreditIds.Select(id => AllTransactions.First(t => t.Id == id));

            UnmatchedDebits.AddRange(debits);
            UnmatchedCredits.AddRange(credits);
            PendingManualMatches.RemoveAt(index);
            error = null;
            return true;
        }
    }

    public void SaveManualMatches()
    {
        lock (_lock)
        {
            State.ManualMatches.AddRange(PendingManualMatches);
            foreach (var mm in PendingManualMatches)
            {
                foreach (var id in mm.DebitIds) State.MatchedTransactionIds.Add(id);
                foreach (var id in mm.CreditIds) State.MatchedTransactionIds.Add(id);
            }
            PendingManualMatches = [];
        }
    }
}

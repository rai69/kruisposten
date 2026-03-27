using Triodos.KruispostMonitor.Matching;

namespace Triodos.KruispostMonitor.TransactionSource;

public record TransactionSourceResult(
    List<TransactionRecord> Transactions,
    decimal CurrentBalance,
    string Currency,
    string AccountIdentifier);

public interface ITransactionSource
{
    Task<TransactionSourceResult> FetchTransactionsAsync(DateTimeOffset? since);
}

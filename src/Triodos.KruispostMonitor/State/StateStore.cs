using Triodos.KruispostMonitor.Matching;

namespace Triodos.KruispostMonitor.State;

public interface IStateStore
{
    Task InitializeAsync();
    Task<RunState> LoadAsync();
    Task SaveAsync(RunState state);
    Task SaveTransactionsAsync(IReadOnlyList<TransactionRecord> transactions, string sourceFile);
    Task<List<TransactionRecord>> GetAllTransactionsAsync();
    Task<List<TransactionRecord>> GetMatchedTransactionsAsync();
    Task<List<TransactionRecord>> GetExcludedTransactionsAsync();
    Task RemoveExclusionAsync(string transactionId);
    Task DeleteTransactionAsync(string transactionId);
    Task ResetDatabaseAsync();
}

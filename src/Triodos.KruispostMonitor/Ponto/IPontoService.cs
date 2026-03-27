using Triodos.KruispostMonitor.Matching;

namespace Triodos.KruispostMonitor.Ponto;

public record AccountInfo(string AccountId, string Iban, decimal CurrentBalance, string Currency);

public interface IPontoService
{
    Task<string> InitializeAsync(string refreshToken);
    Task<AccountInfo?> GetAccountByIbanAsync(string iban);
    Task TriggerSynchronizationAsync(string accountId);
    Task<List<TransactionRecord>> GetTransactionsAsync(string accountId, DateTimeOffset? since);
    string? LatestRefreshToken { get; }
}

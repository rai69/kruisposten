using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Triodos.KruispostMonitor.Configuration;
using Triodos.KruispostMonitor.Ponto;

namespace Triodos.KruispostMonitor.TransactionSource;

public class PontoTransactionSource : ITransactionSource
{
    private readonly IPontoService _pontoService;
    private readonly PontoSettings _settings;
    private readonly ILogger<PontoTransactionSource> _logger;

    public string? LatestRefreshToken => _pontoService.LatestRefreshToken;

    /// <summary>
    /// Set this before calling FetchTransactionsAsync to use a stored refresh token
    /// instead of the one from configuration. Program.cs sets this from RunState.
    /// </summary>
    public string? StoredRefreshToken { get; set; }

    public PontoTransactionSource(
        IPontoService pontoService,
        IOptions<PontoSettings> settings,
        ILogger<PontoTransactionSource> logger)
    {
        _pontoService = pontoService;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<TransactionSourceResult> FetchTransactionsAsync(DateTimeOffset? since)
    {
        // Initialize Ponto — prefer stored token over config
        var refreshToken = StoredRefreshToken ?? _settings.RefreshToken;
        await _pontoService.InitializeAsync(refreshToken);

        // Find account
        var account = await _pontoService.GetAccountByIbanAsync(_settings.AccountIban)
            ?? throw new InvalidOperationException($"Account with IBAN {_settings.AccountIban} not found");

        _logger.LogInformation("Found account {Iban} with balance {Balance} {Currency}",
            account.Iban, account.CurrentBalance, account.Currency);

        // Trigger sync and fetch transactions
        await _pontoService.TriggerSynchronizationAsync(account.AccountId);
        await Task.Delay(TimeSpan.FromSeconds(5));
        var transactions = await _pontoService.GetTransactionsAsync(account.AccountId, since);

        return new TransactionSourceResult(
            transactions,
            account.CurrentBalance,
            account.Currency,
            account.Iban);
    }
}

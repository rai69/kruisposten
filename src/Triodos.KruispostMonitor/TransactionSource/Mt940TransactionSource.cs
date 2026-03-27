using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Triodos.KruispostMonitor.Configuration;
using Triodos.KruispostMonitor.Mt940;

namespace Triodos.KruispostMonitor.TransactionSource;

public class Mt940TransactionSource : ITransactionSource
{
    private readonly Mt940Settings _settings;
    private readonly ILogger<Mt940TransactionSource> _logger;

    public Mt940TransactionSource(
        IOptions<TransactionSourceSettings> settings,
        ILogger<Mt940TransactionSource> logger)
    {
        _settings = settings.Value.Mt940;
        _logger = logger;
    }

    public async Task<TransactionSourceResult> FetchTransactionsAsync(DateTimeOffset? since)
    {
        if (string.IsNullOrWhiteSpace(_settings.FilePath))
            throw new InvalidOperationException("MT940 file path is not configured");

        if (!File.Exists(_settings.FilePath))
            throw new FileNotFoundException($"MT940 file not found: {_settings.FilePath}");

        _logger.LogInformation("Reading MT940 file: {FilePath}", _settings.FilePath);
        var content = await File.ReadAllTextAsync(_settings.FilePath);

        var statement = Mt940Parser.Parse(content);
        _logger.LogInformation("Parsed {Count} transactions from MT940, account {Account}, balance {Balance} {Currency}",
            statement.Transactions.Count, statement.AccountIdentification, statement.ClosingBalance, statement.Currency);

        return new TransactionSourceResult(
            statement.Transactions,
            statement.ClosingBalance,
            statement.Currency,
            statement.AccountIdentification);
    }
}

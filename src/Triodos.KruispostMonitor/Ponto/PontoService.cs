using Ibanity.Apis.Client;
using Ibanity.Apis.Client.Http;
using Ibanity.Apis.Client.Products.PontoConnect.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Triodos.KruispostMonitor.Configuration;
using Triodos.KruispostMonitor.Matching;

namespace Triodos.KruispostMonitor.Ponto;

public class PontoService : IPontoService
{
    private readonly PontoSettings _settings;
    private readonly ILogger<PontoService> _logger;
    private IIbanityService? _ibanityService;
    private Token? _token;
    private string? _latestRefreshToken;

    public string? LatestRefreshToken => _latestRefreshToken;

    public PontoService(IOptions<PontoSettings> settings, ILogger<PontoService> logger)
    {
        _settings = settings.Value;
        _logger = logger;
    }

    public Task<string> InitializeAsync(string refreshToken)
    {
        _ibanityService = new IbanityServiceBuilder()
            .SetEndpoint(_settings.ApiEndpoint)
            .AddClientCertificate(_settings.CertificatePath, _settings.CertificatePassword)
            .AddPontoConnectOAuth2Authentication(_settings.ClientId, _settings.ClientSecret)
            .Build();

        // Construct a Token with an expired access token so the SDK will refresh it on first use
        _token = new Token(string.Empty, DateTimeOffset.MinValue, refreshToken);
        _latestRefreshToken = refreshToken;

        // Subscribe to refresh token rotation events
        _token.RefreshTokenUpdated += (_, args) =>
        {
            _latestRefreshToken = args.NewToken;
            _logger.LogInformation("Refresh token was rotated");
        };

        _logger.LogInformation("Ponto service initialized");
        return Task.FromResult(refreshToken);
    }

    public async Task<AccountInfo?> GetAccountByIbanAsync(string iban)
    {
        EnsureInitialized();
        var page = await _ibanityService!.PontoConnect.Accounts.List(_token!);

        foreach (var account in page.Items)
        {
            if (string.Equals(account.Reference, iban, StringComparison.OrdinalIgnoreCase))
            {
                return new AccountInfo(
                    account.Id.ToString(),
                    account.Reference,
                    account.CurrentBalance,
                    account.Currency);
            }
        }

        _logger.LogWarning("Account with IBAN {Iban} not found", iban);
        return null;
    }

    public async Task TriggerSynchronizationAsync(string accountId)
    {
        EnsureInitialized();
        var sync = new SynchronizationRequest
        {
            ResourceType = "account",
            ResourceId = Guid.Parse(accountId),
            Subtype = "accountTransactions"
        };

        await _ibanityService!.PontoConnect.Synchronizations.Create(_token!, sync);
        _logger.LogInformation("Synchronization triggered for account {AccountId}", accountId);
    }

    public async Task<List<TransactionRecord>> GetTransactionsAsync(string accountId, DateTimeOffset? since)
    {
        EnsureInitialized();
        var result = new List<TransactionRecord>();
        var accountGuid = Guid.Parse(accountId);

        var page = await _ibanityService!.PontoConnect.Transactions.List(_token!, accountGuid);

        while (true)
        {
            foreach (var tx in page.Items)
            {
                if (since.HasValue && tx.ExecutionDate < since.Value)
                    continue;

                result.Add(new TransactionRecord(
                    tx.Id.ToString(),
                    tx.Amount,
                    tx.CounterpartName ?? string.Empty,
                    tx.RemittanceInformation ?? string.Empty,
                    tx.ExecutionDate));
            }

            if (!page.AfterCursor.HasValue)
                break;

            page = await _ibanityService.PontoConnect.Transactions.List(_token!, accountGuid, pageAfter: page.AfterCursor);
        }

        _logger.LogInformation("Fetched {Count} transactions", result.Count);
        return result;
    }

    private void EnsureInitialized()
    {
        if (_ibanityService is null || _token is null)
            throw new InvalidOperationException("PontoService has not been initialized. Call InitializeAsync first.");
    }
}

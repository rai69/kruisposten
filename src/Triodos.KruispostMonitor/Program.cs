using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Triodos.KruispostMonitor.Configuration;
using Triodos.KruispostMonitor.Matching;
using Triodos.KruispostMonitor.Notifications;
using Triodos.KruispostMonitor.Ponto;
using Triodos.KruispostMonitor.State;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.Configure<PontoSettings>(builder.Configuration.GetSection(PontoSettings.SectionName));
builder.Services.Configure<MatchingSettings>(builder.Configuration.GetSection(MatchingSettings.SectionName));
builder.Services.Configure<NotificationSettings>(builder.Configuration.GetSection(NotificationSettings.SectionName));
builder.Services.Configure<StateSettings>(builder.Configuration.GetSection(StateSettings.SectionName));

builder.Services.AddSingleton<IPontoService, PontoService>();
builder.Services.AddSingleton<IStateStore>(sp =>
    new StateStore(sp.GetRequiredService<IOptions<StateSettings>>().Value.FilePath));
builder.Services.AddHttpClient<SlackNotificationSender>();
builder.Services.AddSingleton<INotificationSender, SlackNotificationSender>(sp => sp.GetRequiredService<SlackNotificationSender>());
builder.Services.AddSingleton<INotificationSender, EmailNotificationSender>();

using var host = builder.Build();
var logger = host.Services.GetRequiredService<ILogger<Program>>();

try
{
    var pontoService = host.Services.GetRequiredService<IPontoService>();
    var stateStore = host.Services.GetRequiredService<IStateStore>();
    var matchingSettings = host.Services.GetRequiredService<IOptions<MatchingSettings>>().Value;
    var pontoSettings = host.Services.GetRequiredService<IOptions<PontoSettings>>().Value;
    var notificationSenders = host.Services.GetRequiredService<IEnumerable<INotificationSender>>();

    // Load state
    var state = await stateStore.LoadAsync();
    logger.LogInformation("Last run: {LastRun}", state.LastRunUtc?.ToString("o") ?? "never");

    // Initialize Ponto
    var refreshToken = state.RefreshToken ?? pontoSettings.RefreshToken;
    await pontoService.InitializeAsync(refreshToken);

    // Find account
    var account = await pontoService.GetAccountByIbanAsync(pontoSettings.AccountIban);
    if (account is null)
    {
        logger.LogError("Account with IBAN {Iban} not found. Exiting.", pontoSettings.AccountIban);
        return 1;
    }

    logger.LogInformation("Found account {Iban} with balance {Balance} {Currency}",
        account.Iban, account.CurrentBalance, account.Currency);

    // Trigger sync and fetch transactions
    await pontoService.TriggerSynchronizationAsync(account.AccountId);
    await Task.Delay(TimeSpan.FromSeconds(5)); // Allow sync to complete
    var transactions = await pontoService.GetTransactionsAsync(account.AccountId, state.LastRunUtc);

    // Match transactions
    var matcher = new TransactionMatcher(matchingSettings);
    var matchResult = matcher.Match(transactions, state.MatchedTransactionIds);

    logger.LogInformation("Matched: {Matched}, Unmatched debits: {Unmatched}, Possible: {Possible}",
        matchResult.Matched.Count, matchResult.UnmatchedDebits.Count, matchResult.PossibleMatches.Count);

    // Build and send notifications
    var message = NotificationMessageBuilder.Build(matchResult, account.CurrentBalance, matchingSettings.TargetBalance);

    if (message is not null)
    {
        logger.LogWarning("Issues detected, sending notifications");
        foreach (var sender in notificationSenders.Where(s => s.IsEnabled))
        {
            try
            {
                await sender.SendAsync(message);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to send notification via {Sender}", sender.GetType().Name);
            }
        }
    }
    else
    {
        logger.LogInformation("All clear — no issues found");
        var notifyOnSuccess = host.Services.GetRequiredService<IOptions<NotificationSettings>>().Value.NotifyOnSuccess;
        if (notifyOnSuccess)
        {
            var successMsg = $"Kruispost Monitor — all clear. Balance: EUR {account.CurrentBalance:F2}";
            foreach (var sender in notificationSenders.Where(s => s.IsEnabled))
            {
                try { await sender.SendAsync(successMsg); }
                catch (Exception ex) { logger.LogError(ex, "Failed to send success notification via {Sender}", sender.GetType().Name); }
            }
        }
    }

    // Update state
    foreach (var pair in matchResult.Matched)
    {
        state.MatchedTransactionIds.Add(pair.Debit.Id);
        state.MatchedTransactionIds.Add(pair.Credit.Id);
    }

    state.LastRunUtc = DateTimeOffset.UtcNow;
    state.RefreshToken = pontoService.LatestRefreshToken;
    await stateStore.SaveAsync(state);

    logger.LogInformation("State saved. Done.");
    return 0;
}
catch (Exception ex)
{
    logger.LogCritical(ex, "Unhandled exception");
    return 1;
}

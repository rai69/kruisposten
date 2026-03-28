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
using Triodos.KruispostMonitor.TransactionSource;

var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory
});

// Ensure user secrets are loaded in all environments
builder.Configuration.AddUserSecrets<Program>(optional: true);

builder.Services.Configure<PontoSettings>(builder.Configuration.GetSection(PontoSettings.SectionName));
builder.Services.Configure<MatchingSettings>(builder.Configuration.GetSection(MatchingSettings.SectionName));
builder.Services.Configure<NotificationSettings>(builder.Configuration.GetSection(NotificationSettings.SectionName));
builder.Services.Configure<StateSettings>(builder.Configuration.GetSection(StateSettings.SectionName));
builder.Services.Configure<TransactionSourceSettings>(builder.Configuration.GetSection(TransactionSourceSettings.SectionName));

builder.Services.AddSingleton<IPontoService, PontoService>();
builder.Services.AddSingleton<IStateStore>(sp =>
    new StateStore(sp.GetRequiredService<IOptions<StateSettings>>().Value.FilePath));
builder.Services.AddHttpClient<SlackNotificationSender>();
builder.Services.AddSingleton<INotificationSender, SlackNotificationSender>(sp => sp.GetRequiredService<SlackNotificationSender>());
builder.Services.AddSingleton<INotificationSender, EmailNotificationSender>();

// Register transaction source based on configured mode
var sourceMode = builder.Configuration.GetSection(TransactionSourceSettings.SectionName)["Mode"] ?? "Ponto";
if (string.Equals(sourceMode, "Mt940", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddSingleton<ITransactionSource, Mt940TransactionSource>();
}
else
{
    builder.Services.AddSingleton<ITransactionSource, PontoTransactionSource>();
}

using var host = builder.Build();
var logger = host.Services.GetRequiredService<ILogger<Program>>();

try
{
    var transactionSource = host.Services.GetRequiredService<ITransactionSource>();
    var stateStore = host.Services.GetRequiredService<IStateStore>();
    var matchingSettings = host.Services.GetRequiredService<IOptions<MatchingSettings>>().Value;
    var notificationSenders = host.Services.GetRequiredService<IEnumerable<INotificationSender>>();

    // Load state
    var state = await stateStore.LoadAsync();
    logger.LogInformation("Last run: {LastRun}", state.LastRunUtc?.ToString("o") ?? "never");

    // Pass stored refresh token to Ponto source if available
    if (transactionSource is PontoTransactionSource pontoSource && state.RefreshToken is not null)
    {
        pontoSource.StoredRefreshToken = state.RefreshToken;
    }

    // Fetch transactions from configured source
    logger.LogInformation("Using transaction source: {Mode}", sourceMode);
    var sourceResult = await transactionSource.FetchTransactionsAsync(state.LastRunUtc);

    logger.LogInformation("Account {Account}, balance {Balance} {Currency}, {Count} transactions",
        sourceResult.AccountIdentifier, sourceResult.CurrentBalance, sourceResult.Currency, sourceResult.Transactions.Count);

    // Match transactions
    var matcher = new TransactionMatcher(matchingSettings);
    var matchResult = matcher.Match(sourceResult.Transactions, state.MatchedTransactionIds);

    logger.LogInformation("Matched: {Matched}, Unmatched debits: {Unmatched}, Possible: {Possible}",
        matchResult.Matched.Count, matchResult.UnmatchedDebits.Count, matchResult.PossibleMatches.Count);

    // Build and send notifications
    var message = NotificationMessageBuilder.Build(matchResult, sourceResult.CurrentBalance, matchingSettings.TargetBalance);

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
            var successMsg = $"Kruispost Monitor — all clear. Balance: {sourceResult.Currency} {sourceResult.CurrentBalance:F2}";
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

    // Persist refresh token only in Ponto mode
    if (transactionSource is PontoTransactionSource pts)
    {
        state.RefreshToken = pts.LatestRefreshToken;
    }

    await stateStore.SaveAsync(state);

    logger.LogInformation("State saved. Done.");
    return 0;
}
catch (Exception ex)
{
    logger.LogCritical(ex, "Unhandled exception");
    return 1;
}

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Triodos.KruispostMonitor.Configuration;
using Triodos.KruispostMonitor.Matching;
using Triodos.KruispostMonitor.Mt940;
using Triodos.KruispostMonitor.Notifications;
using Triodos.KruispostMonitor.State;
using Triodos.KruispostMonitor.TransactionSource;

namespace Triodos.KruispostMonitor.Services;

public class ProcessingService
{
    private readonly IStateStore _stateStore;
    private readonly MatchingSettings _matchingSettings;
    private readonly IEnumerable<INotificationSender> _notificationSenders;
    private readonly NotificationSettings _notificationSettings;
    private readonly MonitorState _monitorState;
    private readonly ILogger<ProcessingService> _logger;

    public ProcessingService(
        IStateStore stateStore,
        IOptions<MatchingSettings> matchingSettings,
        IEnumerable<INotificationSender> notificationSenders,
        IOptions<NotificationSettings> notificationSettings,
        MonitorState monitorState,
        ILogger<ProcessingService> logger)
    {
        _stateStore = stateStore;
        _matchingSettings = matchingSettings.Value;
        _notificationSenders = notificationSenders;
        _notificationSettings = notificationSettings.Value;
        _monitorState = monitorState;
        _logger = logger;
    }

    public async Task ProcessFileAsync(string filePath)
    {
        _logger.LogInformation("Processing MT940 file: {FilePath}", filePath);
        var sourceResult = ParseMt940File(filePath);
        await ProcessAsync(sourceResult, Path.GetFileName(filePath));
    }

    public async Task ReloadFileAsync(string filePath)
    {
        _logger.LogInformation("Reloading MT940 file for dashboard: {FilePath}", filePath);
        var sourceResult = ParseMt940File(filePath);
        await ReloadAsync(sourceResult, Path.GetFileName(filePath));
    }

    private static TransactionSourceResult ParseMt940File(string filePath)
    {
        var content = File.ReadAllText(filePath);
        var statement = Mt940Parser.Parse(content);
        return new TransactionSourceResult(
            statement.Transactions,
            statement.ClosingBalance,
            statement.Currency,
            statement.AccountIdentification);
    }

    public async Task ProcessAsync(TransactionSourceResult sourceResult, string sourceName)
    {
        _logger.LogInformation("Account {Account}, balance {Balance} {Currency}, {Count} transactions",
            sourceResult.AccountIdentifier, sourceResult.CurrentBalance, sourceResult.Currency, sourceResult.Transactions.Count);

        // Load state
        var state = await _stateStore.LoadAsync();

        // Build exclusion set (auto-matched + manual matches + excluded)
        var excludedIds = new HashSet<string>(state.MatchedTransactionIds);
        foreach (var mm in state.ManualMatches)
        {
            foreach (var id in mm.DebitIds) excludedIds.Add(id);
            foreach (var id in mm.CreditIds) excludedIds.Add(id);
        }
        excludedIds.UnionWith(state.ExcludedTransactionIds);

        // Auto-match
        var matcher = new TransactionMatcher(_matchingSettings);
        var matchResult = matcher.Match(sourceResult.Transactions, excludedIds);

        _logger.LogInformation("Matched: {Matched}, Unmatched debits: {Unmatched}, Possible: {Possible}",
            matchResult.Matched.Count, matchResult.UnmatchedDebits.Count, matchResult.PossibleMatches.Count);

        // Update state
        foreach (var pair in matchResult.Matched)
        {
            state.MatchedTransactionIds.Add(pair.Debit.Id);
            state.MatchedTransactionIds.Add(pair.Credit.Id);
        }
        state.LastRunUtc = DateTimeOffset.UtcNow;
        await _stateStore.SaveAsync(state);

        // Update monitor state for dashboard
        _monitorState.UpdateFromProcessing(
            matchResult,
            sourceResult.Transactions,
            sourceResult.CurrentBalance,
            sourceResult.Currency,
            sourceResult.AccountIdentifier,
            state,
            sourceName);

        // Send notifications
        await SendNotificationsAsync(matchResult, sourceResult.CurrentBalance, sourceResult.Currency);
    }

    public async Task ReloadAsync(TransactionSourceResult sourceResult, string sourceName)
    {
        _logger.LogInformation("Reloading dashboard state (no notifications)");

        var state = await _stateStore.LoadAsync();

        var excludedIds = new HashSet<string>(state.MatchedTransactionIds);
        foreach (var mm in state.ManualMatches)
        {
            foreach (var id in mm.DebitIds) excludedIds.Add(id);
            foreach (var id in mm.CreditIds) excludedIds.Add(id);
        }
        excludedIds.UnionWith(state.ExcludedTransactionIds);

        var matcher = new TransactionMatcher(_matchingSettings);
        var matchResult = matcher.Match(sourceResult.Transactions, excludedIds);

        _monitorState.UpdateFromProcessing(
            matchResult,
            sourceResult.Transactions,
            sourceResult.CurrentBalance,
            sourceResult.Currency,
            sourceResult.AccountIdentifier,
            state,
            sourceName,
            skipHistory: true);
    }

    private async Task SendNotificationsAsync(MatchResult matchResult, decimal currentBalance, string currency)
    {
        var message = NotificationMessageBuilder.Build(matchResult, currentBalance, _matchingSettings.TargetBalance);

        if (message is not null)
        {
            _logger.LogWarning("Issues detected, sending notifications");
            foreach (var sender in _notificationSenders.Where(s => s.IsEnabled))
            {
                try { await sender.SendAsync(message); }
                catch (Exception ex) { _logger.LogError(ex, "Failed to send notification via {Sender}", sender.GetType().Name); }
            }
        }
        else if (_notificationSettings.NotifyOnSuccess)
        {
            var successMsg = NotificationMessageBuilder.BuildSuccess(currentBalance, currency);
            foreach (var sender in _notificationSenders.Where(s => s.IsEnabled))
            {
                try { await sender.SendAsync(successMsg); }
                catch (Exception ex) { _logger.LogError(ex, "Failed to send success notification via {Sender}", sender.GetType().Name); }
            }
        }
        else
        {
            _logger.LogInformation("All clear — no issues found");
        }
    }
}

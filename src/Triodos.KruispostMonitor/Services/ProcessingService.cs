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

        // Persist transactions from this file to database
        await _stateStore.SaveTransactionsAsync(sourceResult.Transactions, sourceName);

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

        // Load ALL transactions from database (cross-file matching)
        var allTransactions = await _stateStore.GetAllTransactionsAsync();

        // Auto-match across all transactions
        var matcher = new TransactionMatcher(_matchingSettings);
        var matchResult = matcher.Match(allTransactions, excludedIds);

        _logger.LogInformation("Matched: {Matched}, Unmatched debits: {Unmatched}, Possible: {Possible}",
            matchResult.Matched.Count, matchResult.UnmatchedDebits.Count, matchResult.PossibleMatches.Count);

        // Merge previously matched pairs into the result for display (before adding new matches to state)
        var fullResult = MergeWithPreviousMatches(matchResult, state.MatchedTransactionIds, allTransactions);

        // Update state
        foreach (var pair in matchResult.Matched)
        {
            state.MatchedTransactionIds.Add(pair.Debit.Id);
            state.MatchedTransactionIds.Add(pair.Credit.Id);
        }
        foreach (var split in matchResult.SplitMatches)
        {
            state.MatchedTransactionIds.Add(split.Credit.Id);
            foreach (var d in split.Debits)
                state.MatchedTransactionIds.Add(d.Id);
        }
        state.LastRunUtc = DateTimeOffset.UtcNow;
        await _stateStore.SaveAsync(state);

        // Update monitor state for dashboard
        _monitorState.UpdateFromProcessing(
            fullResult,
            allTransactions,
            sourceResult.CurrentBalance,
            sourceResult.Currency,
            sourceResult.AccountIdentifier,
            state,
            sourceName);

        // Send notifications (only for new matches/unmatched)
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

        // Load ALL transactions from database (cross-file matching)
        var allTransactions = await _stateStore.GetAllTransactionsAsync();

        var matcher = new TransactionMatcher(_matchingSettings);
        var matchResult = matcher.Match(allTransactions, excludedIds);

        // Merge previously matched pairs into the result for display
        var fullResult = MergeWithPreviousMatches(matchResult, state.MatchedTransactionIds, allTransactions);

        _monitorState.UpdateFromProcessing(
            fullResult,
            allTransactions,
            sourceResult.CurrentBalance,
            sourceResult.Currency,
            sourceResult.AccountIdentifier,
            state,
            sourceName,
            skipHistory: true);
    }

    private MatchResult MergeWithPreviousMatches(
        MatchResult newResult,
        HashSet<string> matchedTransactionIds,
        List<TransactionRecord> allTransactions)
    {
        // Reconstruct previously matched pairs from stored IDs
        var txById = allTransactions.ToDictionary(t => t.Id);
        var matchedDebits = matchedTransactionIds
            .Where(id => txById.ContainsKey(id) && txById[id].IsDebit)
            .Select(id => txById[id])
            .ToList();
        var matchedCredits = matchedTransactionIds
            .Where(id => txById.ContainsKey(id) && txById[id].IsCredit)
            .Select(id => txById[id])
            .ToList();

        // Reconstruct split matches first (using configured rules)
        var previousSplits = new List<SplitMatch>();
        var splitUsedIds = new HashSet<string>();
        foreach (var rule in _matchingSettings.SplitRules)
        {
            foreach (var credit in matchedCredits.Where(c =>
                c.AbsoluteAmount == rule.CreditAmount
                && (rule.TransactionType is null || c.TransactionType == rule.TransactionType)))
            {
                var debits = new List<TransactionRecord>();
                var tempUsed = new HashSet<string>();
                var allFound = true;
                foreach (var amt in rule.DebitAmounts)
                {
                    var d = matchedDebits.FirstOrDefault(dd =>
                        !splitUsedIds.Contains(dd.Id) && !tempUsed.Contains(dd.Id)
                        && dd.AbsoluteAmount == amt
                        && (rule.TransactionType is null || dd.TransactionType == rule.TransactionType)
                        && Math.Abs((dd.ExecutionDate - credit.ExecutionDate).TotalDays) <= 3);
                    if (d is null) { allFound = false; break; }
                    debits.Add(d);
                    tempUsed.Add(d.Id);
                }
                if (allFound)
                {
                    previousSplits.Add(new SplitMatch(credit, debits));
                    splitUsedIds.Add(credit.Id);
                    foreach (var d in debits) splitUsedIds.Add(d.Id);
                }
            }
        }

        // Pair remaining 1:1 matches by amount (excluding split-matched IDs)
        var previousPairs = new List<MatchedPair>();
        var usedCredits = new HashSet<string>(splitUsedIds);
        foreach (var debit in matchedDebits.Where(d => !splitUsedIds.Contains(d.Id)))
        {
            var credit = matchedCredits.FirstOrDefault(c =>
                !usedCredits.Contains(c.Id) && c.AbsoluteAmount == debit.AbsoluteAmount);
            if (credit is not null)
            {
                usedCredits.Add(credit.Id);
                previousPairs.Add(new MatchedPair(debit, credit, 1.0));
            }
        }

        // Merge: previous + new
        var allMatched = new List<MatchedPair>(previousPairs);
        allMatched.AddRange(newResult.Matched);

        var allSplits = new List<SplitMatch>(previousSplits);
        allSplits.AddRange(newResult.SplitMatches);

        return new MatchResult
        {
            Matched = allMatched,
            SplitMatches = allSplits,
            UnmatchedDebits = newResult.UnmatchedDebits,
            UnmatchedCredits = newResult.UnmatchedCredits,
            PossibleMatches = newResult.PossibleMatches
        };
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

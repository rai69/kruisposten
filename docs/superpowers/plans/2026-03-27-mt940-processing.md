# MT940 Processing Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add MT940 bank statement file processing as an alternative transaction source, so the Kruispost Monitor can run without Ponto Connect API access.

**Architecture:** Introduce `ITransactionSource` abstraction with two implementations (Ponto, MT940). A hand-rolled MT940 parser extracts transactions and closing balance. Program.cs selects the source based on config and feeds results into the existing matching/notification pipeline.

**Tech Stack:** .NET 9, xUnit + FluentAssertions

---

## File Structure

```
src/Triodos.KruispostMonitor/
├── Configuration/
│   └── AppSettings.cs                    # Add TransactionSourceSettings + Mt940Settings
├── Mt940/
│   ├── Mt940Parser.cs                    # Pure parser: string → Mt940Statement
│   └── Mt940Statement.cs                # Statement record
├── TransactionSource/
│   ├── ITransactionSource.cs             # Interface + TransactionSourceResult record
│   ├── PontoTransactionSource.cs         # Wraps existing IPontoService
│   └── Mt940TransactionSource.cs         # Reads file, uses Mt940Parser
├── Program.cs                            # Refactor to use ITransactionSource
└── appsettings.json                      # Add TransactionSource section

tests/Triodos.KruispostMonitor.Tests/
└── Mt940/
    └── Mt940ParserTests.cs               # Parse MT940 content strings
```

---

### Task 1: Configuration — Add TransactionSource and Mt940 settings

**Files:**
- Modify: `src/Triodos.KruispostMonitor/Configuration/AppSettings.cs`
- Modify: `src/Triodos.KruispostMonitor/appsettings.json`

- [ ] **Step 1: Add settings classes to AppSettings.cs**

Add the following classes at the end of `src/Triodos.KruispostMonitor/Configuration/AppSettings.cs`:

```csharp
public class TransactionSourceSettings
{
    public const string SectionName = "TransactionSource";
    public string Mode { get; set; } = "Ponto";
    public Mt940Settings Mt940 { get; set; } = new();
}

public class Mt940Settings
{
    public string FilePath { get; set; } = string.Empty;
}
```

- [ ] **Step 2: Add TransactionSource section to appsettings.json**

Add the following section to `src/Triodos.KruispostMonitor/appsettings.json`, after the `"State"` section:

```json
"TransactionSource": {
  "Mode": "Ponto",
  "Mt940": {
    "FilePath": ""
  }
}
```

- [ ] **Step 3: Verify build**

Run: `cd D:/claude/triodos && dotnet build`
Expected: Build succeeded with 0 errors

- [ ] **Step 4: Commit**

```bash
cd D:/claude/triodos
git add src/Triodos.KruispostMonitor/Configuration/AppSettings.cs src/Triodos.KruispostMonitor/appsettings.json
git commit -m "feat: add TransactionSource and Mt940 configuration settings"
```

---

### Task 2: Mt940Statement record

**Files:**
- Create: `src/Triodos.KruispostMonitor/Mt940/Mt940Statement.cs`

- [ ] **Step 1: Create Mt940Statement record**

Write to `src/Triodos.KruispostMonitor/Mt940/Mt940Statement.cs`:

```csharp
using Triodos.KruispostMonitor.Matching;

namespace Triodos.KruispostMonitor.Mt940;

public record Mt940Statement(
    string AccountIdentification,
    decimal ClosingBalance,
    string Currency,
    List<TransactionRecord> Transactions);
```

- [ ] **Step 2: Verify build**

Run: `cd D:/claude/triodos && dotnet build`
Expected: Build succeeded with 0 errors

- [ ] **Step 3: Commit**

```bash
cd D:/claude/triodos
git add src/Triodos.KruispostMonitor/Mt940/Mt940Statement.cs
git commit -m "feat: add Mt940Statement record"
```

---

### Task 3: MT940 Parser with tests (TDD)

**Files:**
- Create: `tests/Triodos.KruispostMonitor.Tests/Mt940/Mt940ParserTests.cs`
- Create: `src/Triodos.KruispostMonitor/Mt940/Mt940Parser.cs`

- [ ] **Step 1: Write failing tests**

Write to `tests/Triodos.KruispostMonitor.Tests/Mt940/Mt940ParserTests.cs`:

```csharp
using FluentAssertions;
using Triodos.KruispostMonitor.Mt940;

namespace Triodos.KruispostMonitor.Tests.Mt940;

public class Mt940ParserTests
{
    private const string SampleStatement = """
        :20:STARTOFSTMT
        :25:NL91TRIO0123456789
        :28C:00001
        :60F:C260325EUR1234,56
        :61:2603250325D100,00NTRFNONREF//PREF
        :86:Counterpart A
        /REMI/Payment for invoice 001
        :61:2603250325C100,00NTRFNONREF//PREF
        :86:Counterpart B
        /REMI/Chargeback invoice 001
        :61:2603260326D50,50NTRFNONREF//PREF
        :86:Counterpart C
        /REMI/Payment for invoice 002
        :62F:C1184,06EUR
        """;

    [Fact]
    public void Parse_ExtractsAccountIdentification()
    {
        var result = Mt940Parser.Parse(SampleStatement);

        result.AccountIdentification.Should().Be("NL91TRIO0123456789");
    }

    [Fact]
    public void Parse_ExtractsClosingBalance()
    {
        var result = Mt940Parser.Parse(SampleStatement);

        result.ClosingBalance.Should().Be(1184.06m);
        result.Currency.Should().Be("EUR");
    }

    [Fact]
    public void Parse_ExtractsTransactions()
    {
        var result = Mt940Parser.Parse(SampleStatement);

        result.Transactions.Should().HaveCount(3);
    }

    [Fact]
    public void Parse_DebitTransaction_HasNegativeAmount()
    {
        var result = Mt940Parser.Parse(SampleStatement);

        var debit = result.Transactions[0];
        debit.Amount.Should().Be(-100.00m);
        debit.CounterpartName.Should().Be("Counterpart A");
        debit.RemittanceInformation.Should().Be("Payment for invoice 001");
        debit.ExecutionDate.Should().Be(new DateTimeOffset(2026, 3, 25, 0, 0, 0, TimeSpan.Zero));
        debit.IsDebit.Should().BeTrue();
    }

    [Fact]
    public void Parse_CreditTransaction_HasPositiveAmount()
    {
        var result = Mt940Parser.Parse(SampleStatement);

        var credit = result.Transactions[1];
        credit.Amount.Should().Be(100.00m);
        credit.CounterpartName.Should().Be("Counterpart B");
        credit.RemittanceInformation.Should().Be("Chargeback invoice 001");
        credit.IsCredit.Should().BeTrue();
    }

    [Fact]
    public void Parse_GeneratesDeterministicIds()
    {
        var result1 = Mt940Parser.Parse(SampleStatement);
        var result2 = Mt940Parser.Parse(SampleStatement);

        result1.Transactions[0].Id.Should().Be(result2.Transactions[0].Id);
        result1.Transactions[0].Id.Should().HaveLength(16);
    }

    [Fact]
    public void Parse_GeneratesUniqueIdsPerTransaction()
    {
        var result = Mt940Parser.Parse(SampleStatement);

        var ids = result.Transactions.Select(t => t.Id).ToList();
        ids.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Parse_EmptyContent_ThrowsFormatException()
    {
        var act = () => Mt940Parser.Parse("");

        act.Should().Throw<FormatException>()
            .WithMessage("*:25:*");
    }

    [Fact]
    public void Parse_MissingClosingBalance_ThrowsFormatException()
    {
        var content = """
            :20:STARTOFSTMT
            :25:NL91TRIO0123456789
            :28C:00001
            :60F:C260325EUR1234,56
            """;

        var act = () => Mt940Parser.Parse(content);

        act.Should().Throw<FormatException>()
            .WithMessage("*:62F:*");
    }

    [Fact]
    public void Parse_ClosingBalanceDebit_ReturnsNegativeBalance()
    {
        var content = """
            :20:STARTOFSTMT
            :25:NL91TRIO0123456789
            :28C:00001
            :60F:C260325EUR1234,56
            :62F:D500,00EUR
            """;

        var result = Mt940Parser.Parse(content);

        result.ClosingBalance.Should().Be(-500.00m);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `cd D:/claude/triodos && dotnet test --filter "FullyQualifiedName~Mt940ParserTests" --no-build 2>&1 || true`
Expected: Build failure — `Mt940Parser` does not exist yet

- [ ] **Step 3: Implement Mt940Parser**

Write to `src/Triodos.KruispostMonitor/Mt940/Mt940Parser.cs`:

```csharp
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Triodos.KruispostMonitor.Matching;

namespace Triodos.KruispostMonitor.Mt940;

public static class Mt940Parser
{
    public static Mt940Statement Parse(string fileContent)
    {
        var lines = fileContent.Split('\n').Select(l => l.TrimEnd('\r')).ToList();

        var accountId = ExtractTagValue(lines, ":25:")
            ?? throw new FormatException("MT940 statement missing required :25: (account identification) tag");

        var closingBalanceLine = ExtractTagValue(lines, ":62F:")
            ?? throw new FormatException("MT940 statement missing required :62F: (closing balance) tag");

        var (closingBalance, currency) = ParseBalanceLine(closingBalanceLine);
        var transactions = ParseTransactions(lines);

        return new Mt940Statement(accountId, closingBalance, currency, transactions);
    }

    private static string? ExtractTagValue(List<string> lines, string tag)
    {
        var line = lines.FirstOrDefault(l => l.TrimStart().StartsWith(tag));
        return line?.TrimStart()[tag.Length..].Trim();
    }

    private static (decimal balance, string currency) ParseBalanceLine(string line)
    {
        // Format: D/C + YYMMDD + Currency + Amount  OR  D/C + Amount + Currency
        // Triodos uses: C1184,06EUR or D500,00EUR (indicator + amount + currency)
        var indicator = line[0];
        var remaining = line[1..];

        // Try to find currency code (3 uppercase letters)
        string currency;
        string amountStr;

        // Check if there's a date (6 digits) after the indicator
        if (remaining.Length >= 6 && remaining[..6].All(char.IsDigit))
        {
            // Format: YYMMDD + Currency + Amount
            remaining = remaining[6..];
        }

        // Find the currency code — 3 uppercase letters
        var currencyStart = -1;
        for (var i = 0; i < remaining.Length - 2; i++)
        {
            if (char.IsUpper(remaining[i]) && char.IsUpper(remaining[i + 1]) && char.IsUpper(remaining[i + 2]))
            {
                currencyStart = i;
                break;
            }
        }

        if (currencyStart >= 0)
        {
            currency = remaining[currencyStart..(currencyStart + 3)];
            amountStr = remaining[..currencyStart] + remaining[(currencyStart + 3)..];
        }
        else
        {
            currency = "EUR";
            amountStr = remaining;
        }

        amountStr = amountStr.Trim();
        var amount = decimal.Parse(amountStr.Replace(',', '.'), CultureInfo.InvariantCulture);

        return indicator == 'D' ? (-amount, currency) : (amount, currency);
    }

    private static List<TransactionRecord> ParseTransactions(List<string> lines)
    {
        var transactions = new List<TransactionRecord>();

        for (var i = 0; i < lines.Count; i++)
        {
            var trimmed = lines[i].TrimStart();
            if (!trimmed.StartsWith(":61:"))
                continue;

            var transactionLine = trimmed[4..];
            var (date, amount, debitCredit) = ParseTransactionLine(transactionLine);

            // Collect :86: information (may span multiple lines)
            var details = CollectDetails(lines, i + 1);
            var (counterpartName, remittanceInfo) = ParseDetails(details);

            var signedAmount = debitCredit == 'D' ? -amount : amount;
            var id = GenerateId(date, signedAmount, debitCredit, counterpartName, remittanceInfo);

            transactions.Add(new TransactionRecord(
                id,
                signedAmount,
                counterpartName,
                remittanceInfo,
                new DateTimeOffset(date, TimeSpan.Zero)));
        }

        return transactions;
    }

    private static (DateTime date, decimal amount, char debitCredit) ParseTransactionLine(string line)
    {
        // Format: YYMMDD[MMDD]D/Camount...
        // First 6 chars: YYMMDD (value date)
        var year = 2000 + int.Parse(line[..2]);
        var month = int.Parse(line[2..4]);
        var day = int.Parse(line[4..6]);
        var date = new DateTime(year, month, day);

        // Skip optional booking date (4 chars MMDD)
        var pos = 6;
        if (pos + 4 <= line.Length && line[pos..].Length >= 4 &&
            char.IsDigit(line[pos]) && char.IsDigit(line[pos + 1]) &&
            char.IsDigit(line[pos + 2]) && char.IsDigit(line[pos + 3]))
        {
            pos += 4;
        }

        // D or C (or RD/RC for reversals)
        var debitCredit = line[pos];
        pos++;
        if (debitCredit == 'R')
        {
            debitCredit = line[pos];
            pos++;
        }

        // Amount: digits and comma until first letter
        var amountEnd = pos;
        while (amountEnd < line.Length && (char.IsDigit(line[amountEnd]) || line[amountEnd] == ','))
            amountEnd++;

        var amountStr = line[pos..amountEnd].Replace(',', '.');
        var amount = decimal.Parse(amountStr, CultureInfo.InvariantCulture);

        return (date, amount, debitCredit);
    }

    private static string CollectDetails(List<string> lines, int startIndex)
    {
        var sb = new StringBuilder();
        for (var i = startIndex; i < lines.Count; i++)
        {
            var trimmed = lines[i].TrimStart();
            if (i == startIndex)
            {
                if (!trimmed.StartsWith(":86:"))
                    break;
                sb.Append(trimmed[4..]);
            }
            else
            {
                // Continuation lines for :86: don't start with a tag
                if (trimmed.StartsWith(':') && trimmed.Length > 3 && trimmed[3] == ':')
                    break;
                sb.Append('\n').Append(trimmed);
            }
        }
        return sb.ToString().Trim();
    }

    private static (string counterpartName, string remittanceInfo) ParseDetails(string details)
    {
        if (string.IsNullOrWhiteSpace(details))
            return (string.Empty, string.Empty);

        var lines = details.Split('\n');
        var counterpartName = lines[0].Trim();
        var remittanceInfo = string.Empty;

        // Look for /REMI/ tag in detail lines
        foreach (var line in lines)
        {
            var remiIndex = line.IndexOf("/REMI/", StringComparison.OrdinalIgnoreCase);
            if (remiIndex >= 0)
            {
                remittanceInfo = line[(remiIndex + 6)..].Trim();
                break;
            }
        }

        // If no /REMI/ found, use remaining lines as remittance info
        if (string.IsNullOrEmpty(remittanceInfo) && lines.Length > 1)
        {
            remittanceInfo = string.Join(" ", lines.Skip(1).Select(l => l.Trim())).Trim();
        }

        return (counterpartName, remittanceInfo);
    }

    private static string GenerateId(DateTime date, decimal amount, char debitCredit, string counterpart, string remittance)
    {
        var input = $"{date:yyyyMMdd}|{amount}|{debitCredit}|{counterpart}|{remittance}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hash)[..16].ToLowerInvariant();
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `cd D:/claude/triodos && dotnet test --filter "FullyQualifiedName~Mt940ParserTests"`
Expected: All tests pass

- [ ] **Step 5: Commit**

```bash
cd D:/claude/triodos
git add src/Triodos.KruispostMonitor/Mt940/Mt940Parser.cs tests/Triodos.KruispostMonitor.Tests/Mt940/Mt940ParserTests.cs
git commit -m "feat: add MT940 parser with tests"
```

---

### Task 4: ITransactionSource interface

**Files:**
- Create: `src/Triodos.KruispostMonitor/TransactionSource/ITransactionSource.cs`

- [ ] **Step 1: Create interface and result record**

Write to `src/Triodos.KruispostMonitor/TransactionSource/ITransactionSource.cs`:

```csharp
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
```

- [ ] **Step 2: Verify build**

Run: `cd D:/claude/triodos && dotnet build`
Expected: Build succeeded with 0 errors

- [ ] **Step 3: Commit**

```bash
cd D:/claude/triodos
git add src/Triodos.KruispostMonitor/TransactionSource/ITransactionSource.cs
git commit -m "feat: add ITransactionSource interface"
```

---

### Task 5: PontoTransactionSource

**Files:**
- Create: `src/Triodos.KruispostMonitor/TransactionSource/PontoTransactionSource.cs`

- [ ] **Step 1: Implement PontoTransactionSource**

Write to `src/Triodos.KruispostMonitor/TransactionSource/PontoTransactionSource.cs`:

```csharp
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

    public PontoTransactionSource(
        IPontoService pontoService,
        IOptions<PontoSettings> settings,
        ILogger<PontoTransactionSource> logger)
    {
        _pontoService = pontoService;
        _settings = settings.Value;
        _logger = logger;
    }

    /// <summary>
    /// Set this before calling FetchTransactionsAsync to use a stored refresh token
    /// instead of the one from configuration. Program.cs sets this from RunState.
    /// </summary>
    public string? StoredRefreshToken { get; set; }

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
```

- [ ] **Step 2: Verify build**

Run: `cd D:/claude/triodos && dotnet build`
Expected: Build succeeded with 0 errors

- [ ] **Step 3: Commit**

```bash
cd D:/claude/triodos
git add src/Triodos.KruispostMonitor/TransactionSource/PontoTransactionSource.cs
git commit -m "feat: add PontoTransactionSource wrapping IPontoService"
```

---

### Task 6: Mt940TransactionSource

**Files:**
- Create: `src/Triodos.KruispostMonitor/TransactionSource/Mt940TransactionSource.cs`

- [ ] **Step 1: Implement Mt940TransactionSource**

Write to `src/Triodos.KruispostMonitor/TransactionSource/Mt940TransactionSource.cs`:

```csharp
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
```

- [ ] **Step 2: Verify build**

Run: `cd D:/claude/triodos && dotnet build`
Expected: Build succeeded with 0 errors

- [ ] **Step 3: Commit**

```bash
cd D:/claude/triodos
git add src/Triodos.KruispostMonitor/TransactionSource/Mt940TransactionSource.cs
git commit -m "feat: add Mt940TransactionSource"
```

---

### Task 7: Refactor Program.cs to use ITransactionSource

**Files:**
- Modify: `src/Triodos.KruispostMonitor/Program.cs`

- [ ] **Step 1: Rewrite Program.cs**

Write to `src/Triodos.KruispostMonitor/Program.cs`:

```csharp
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

var builder = Host.CreateApplicationBuilder(args);

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
```

- [ ] **Step 2: Verify build**

Run: `cd D:/claude/triodos && dotnet build`
Expected: Build succeeded with 0 errors

- [ ] **Step 3: Run all tests**

Run: `cd D:/claude/triodos && dotnet test`
Expected: All tests pass (existing + new MT940 parser tests)

- [ ] **Step 4: Commit**

```bash
cd D:/claude/triodos
git add src/Triodos.KruispostMonitor/Program.cs
git commit -m "refactor: use ITransactionSource abstraction in Program.cs"
```

---

## Post-Implementation Checklist

- [ ] All tests pass
- [ ] App builds without errors
- [ ] MT940 mode works: set `TransactionSource:Mode` to `Mt940`, provide a file path, run the app
- [ ] Ponto mode still works (no regression): set `TransactionSource:Mode` to `Ponto`
- [ ] Configuration documented in appsettings.json with sensible defaults

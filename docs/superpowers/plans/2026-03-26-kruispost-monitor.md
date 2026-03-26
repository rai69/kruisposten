# Kruispost Monitor Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a .NET 9 console app that monitors a Triodos kruispost account via Ponto Connect, matches debits to credits, and notifies via Slack/email when chargebacks are missing.

**Architecture:** Console app using Microsoft.Extensions.Hosting for DI/config. Uses the official Ibanity .NET SDK (`Ibanity` NuGet package) for Ponto Connect API access. Matching logic compares transactions by amount and reference similarity. Notifications via Slack webhook and MailKit SMTP. State persisted in a local JSON file.

**Tech Stack:** .NET 9, Ibanity NuGet (0.6.7), MailKit, Microsoft.Extensions.Hosting, xUnit + FluentAssertions

---

## File Structure

```
D:/claude/triodos/
├── Triodos.KruispostMonitor.sln
├── src/
│   └── Triodos.KruispostMonitor/
│       ├── Triodos.KruispostMonitor.csproj
│       ├── Program.cs
│       ├── appsettings.json
│       ├── Configuration/
│       │   └── AppSettings.cs
│       ├── Ponto/
│       │   ├── IPontoService.cs
│       │   └── PontoService.cs
│       ├── Matching/
│       │   ├── TransactionMatcher.cs
│       │   ├── MatchResult.cs
│       │   └── StringSimilarity.cs
│       ├── Notifications/
│       │   ├── INotificationSender.cs
│       │   ├── SlackNotificationSender.cs
│       │   ├── EmailNotificationSender.cs
│       │   └── NotificationMessageBuilder.cs
│       └── State/
│           ├── RunState.cs
│           └── StateStore.cs
└── tests/
    └── Triodos.KruispostMonitor.Tests/
        ├── Triodos.KruispostMonitor.Tests.csproj
        ├── Matching/
        │   ├── TransactionMatcherTests.cs
        │   └── StringSimilarityTests.cs
        ├── Notifications/
        │   └── NotificationMessageBuilderTests.cs
        └── State/
            └── StateStoreTests.cs
```

---

### Task 1: Project Scaffolding

**Files:**
- Create: `Triodos.KruispostMonitor.sln`
- Create: `src/Triodos.KruispostMonitor/Triodos.KruispostMonitor.csproj`
- Create: `tests/Triodos.KruispostMonitor.Tests/Triodos.KruispostMonitor.Tests.csproj`

- [ ] **Step 1: Create solution and projects**

```bash
cd D:/claude/triodos
dotnet new sln -n Triodos.KruispostMonitor
mkdir -p src/Triodos.KruispostMonitor
dotnet new console -n Triodos.KruispostMonitor -o src/Triodos.KruispostMonitor --framework net9.0
mkdir -p tests/Triodos.KruispostMonitor.Tests
dotnet new xunit -n Triodos.KruispostMonitor.Tests -o tests/Triodos.KruispostMonitor.Tests --framework net9.0
dotnet sln add src/Triodos.KruispostMonitor/Triodos.KruispostMonitor.csproj
dotnet sln add tests/Triodos.KruispostMonitor.Tests/Triodos.KruispostMonitor.Tests.csproj
dotnet add tests/Triodos.KruispostMonitor.Tests reference src/Triodos.KruispostMonitor
```

- [ ] **Step 2: Add NuGet packages to main project**

```bash
cd D:/claude/triodos/src/Triodos.KruispostMonitor
dotnet add package Microsoft.Extensions.Hosting
dotnet add package Microsoft.Extensions.Http
dotnet add package Ibanity --version 0.6.7
dotnet add package MailKit
```

- [ ] **Step 3: Add NuGet packages to test project**

```bash
cd D:/claude/triodos/tests/Triodos.KruispostMonitor.Tests
dotnet add package FluentAssertions
dotnet add package NSubstitute
```

- [ ] **Step 4: Verify build**

Run: `cd D:/claude/triodos && dotnet build`
Expected: Build succeeded with 0 errors

- [ ] **Step 5: Commit**

```bash
cd D:/claude/triodos
git add -A
git commit -m "feat: scaffold solution with main and test projects"
```

---

### Task 2: Configuration

**Files:**
- Create: `src/Triodos.KruispostMonitor/Configuration/AppSettings.cs`
- Create: `src/Triodos.KruispostMonitor/appsettings.json`

- [ ] **Step 1: Create appsettings.json**

Write to `src/Triodos.KruispostMonitor/appsettings.json`:

```json
{
  "Ponto": {
    "ClientId": "",
    "ClientSecret": "",
    "CertificatePath": "",
    "CertificatePassword": "",
    "RefreshToken": "",
    "AccountIban": "",
    "ApiEndpoint": "https://api.ibanity.com"
  },
  "Matching": {
    "SimilarityThreshold": 0.7,
    "TargetBalance": 300.00
  },
  "Notifications": {
    "NotifyOnSuccess": false,
    "Slack": {
      "WebhookUrl": "",
      "Enabled": false
    },
    "Email": {
      "SmtpHost": "",
      "SmtpPort": 587,
      "UseSsl": true,
      "Username": "",
      "Password": "",
      "FromAddress": "",
      "ToAddresses": [],
      "Enabled": false
    }
  },
  "State": {
    "FilePath": "state.json"
  }
}
```

- [ ] **Step 2: Create strongly-typed config classes**

Write to `src/Triodos.KruispostMonitor/Configuration/AppSettings.cs`:

```csharp
namespace Triodos.KruispostMonitor.Configuration;

public class PontoSettings
{
    public const string SectionName = "Ponto";
    public required string ClientId { get; set; }
    public required string ClientSecret { get; set; }
    public required string CertificatePath { get; set; }
    public required string CertificatePassword { get; set; }
    public required string RefreshToken { get; set; }
    public required string AccountIban { get; set; }
    public string ApiEndpoint { get; set; } = "https://api.ibanity.com";
}

public class MatchingSettings
{
    public const string SectionName = "Matching";
    public double SimilarityThreshold { get; set; } = 0.7;
    public decimal TargetBalance { get; set; } = 300.00m;
}

public class SlackSettings
{
    public string WebhookUrl { get; set; } = string.Empty;
    public bool Enabled { get; set; }
}

public class EmailSettings
{
    public string SmtpHost { get; set; } = string.Empty;
    public int SmtpPort { get; set; } = 587;
    public bool UseSsl { get; set; } = true;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string FromAddress { get; set; } = string.Empty;
    public List<string> ToAddresses { get; set; } = [];
    public bool Enabled { get; set; }
}

public class NotificationSettings
{
    public const string SectionName = "Notifications";
    public bool NotifyOnSuccess { get; set; }
    public SlackSettings Slack { get; set; } = new();
    public EmailSettings Email { get; set; } = new();
}

public class StateSettings
{
    public const string SectionName = "State";
    public string FilePath { get; set; } = "state.json";
}
```

- [ ] **Step 3: Verify build**

Run: `cd D:/claude/triodos && dotnet build`
Expected: Build succeeded with 0 errors

- [ ] **Step 4: Commit**

```bash
cd D:/claude/triodos
git add -A
git commit -m "feat: add configuration classes and appsettings.json"
```

---

### Task 3: State Store

**Files:**
- Create: `src/Triodos.KruispostMonitor/State/RunState.cs`
- Create: `src/Triodos.KruispostMonitor/State/StateStore.cs`
- Create: `tests/Triodos.KruispostMonitor.Tests/State/StateStoreTests.cs`

- [ ] **Step 1: Write failing tests for StateStore**

Write to `tests/Triodos.KruispostMonitor.Tests/State/StateStoreTests.cs`:

```csharp
using FluentAssertions;
using Triodos.KruispostMonitor.State;

namespace Triodos.KruispostMonitor.Tests.State;

public class StateStoreTests : IDisposable
{
    private readonly string _tempPath;

    public StateStoreTests()
    {
        _tempPath = Path.Combine(Path.GetTempPath(), $"kruispost-test-{Guid.NewGuid()}.json");
    }

    public void Dispose()
    {
        if (File.Exists(_tempPath))
            File.Delete(_tempPath);
    }

    [Fact]
    public async Task Load_WhenFileDoesNotExist_ReturnsDefaultState()
    {
        var store = new StateStore(_tempPath);

        var state = await store.LoadAsync();

        state.Should().NotBeNull();
        state.LastRunUtc.Should().BeNull();
        state.MatchedTransactionIds.Should().BeEmpty();
    }

    [Fact]
    public async Task SaveAndLoad_RoundTrips()
    {
        var store = new StateStore(_tempPath);
        var state = new RunState
        {
            LastRunUtc = new DateTimeOffset(2026, 3, 26, 10, 0, 0, TimeSpan.Zero),
            MatchedTransactionIds = ["tx-1", "tx-2"],
            RefreshToken = "new-refresh-token"
        };

        await store.SaveAsync(state);
        var loaded = await store.LoadAsync();

        loaded.LastRunUtc.Should().Be(state.LastRunUtc);
        loaded.MatchedTransactionIds.Should().BeEquivalentTo(["tx-1", "tx-2"]);
        loaded.RefreshToken.Should().Be("new-refresh-token");
    }

    [Fact]
    public async Task Save_OverwritesPreviousState()
    {
        var store = new StateStore(_tempPath);

        await store.SaveAsync(new RunState { MatchedTransactionIds = ["tx-1"] });
        await store.SaveAsync(new RunState { MatchedTransactionIds = ["tx-2", "tx-3"] });
        var loaded = await store.LoadAsync();

        loaded.MatchedTransactionIds.Should().BeEquivalentTo(["tx-2", "tx-3"]);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `cd D:/claude/triodos && dotnet test tests/Triodos.KruispostMonitor.Tests --filter "StateStoreTests"`
Expected: FAIL — classes do not exist

- [ ] **Step 3: Implement RunState and StateStore**

Write to `src/Triodos.KruispostMonitor/State/RunState.cs`:

```csharp
namespace Triodos.KruispostMonitor.State;

public class RunState
{
    public DateTimeOffset? LastRunUtc { get; set; }
    public HashSet<string> MatchedTransactionIds { get; set; } = [];
    public string? RefreshToken { get; set; }
}
```

Write to `src/Triodos.KruispostMonitor/State/StateStore.cs`:

```csharp
using System.Text.Json;

namespace Triodos.KruispostMonitor.State;

public interface IStateStore
{
    Task<RunState> LoadAsync();
    Task SaveAsync(RunState state);
}

public class StateStore : IStateStore
{
    private readonly string _filePath;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public StateStore(string filePath)
    {
        _filePath = filePath;
    }

    public async Task<RunState> LoadAsync()
    {
        if (!File.Exists(_filePath))
            return new RunState();

        await using var stream = File.OpenRead(_filePath);
        return await JsonSerializer.DeserializeAsync<RunState>(stream, JsonOptions) ?? new RunState();
    }

    public async Task SaveAsync(RunState state)
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        await using var stream = File.Create(_filePath);
        await JsonSerializer.SerializeAsync(stream, state, JsonOptions);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `cd D:/claude/triodos && dotnet test tests/Triodos.KruispostMonitor.Tests --filter "StateStoreTests"`
Expected: 3 tests passed

- [ ] **Step 5: Commit**

```bash
cd D:/claude/triodos
git add -A
git commit -m "feat: add state store for persisting run state to JSON"
```

---

### Task 4: String Similarity

**Files:**
- Create: `src/Triodos.KruispostMonitor/Matching/StringSimilarity.cs`
- Create: `tests/Triodos.KruispostMonitor.Tests/Matching/StringSimilarityTests.cs`

- [ ] **Step 1: Write failing tests**

Write to `tests/Triodos.KruispostMonitor.Tests/Matching/StringSimilarityTests.cs`:

```csharp
using FluentAssertions;
using Triodos.KruispostMonitor.Matching;

namespace Triodos.KruispostMonitor.Tests.Matching;

public class StringSimilarityTests
{
    [Theory]
    [InlineData("abc", "abc", 1.0)]
    [InlineData("", "", 1.0)]
    [InlineData("abc", "xyz", 0.0)]
    [InlineData("abc", "", 0.0)]
    [InlineData("", "abc", 0.0)]
    public void Calculate_ExactAndEdgeCases(string a, string b, double expected)
    {
        StringSimilarity.Calculate(a, b).Should().BeApproximately(expected, 0.01);
    }

    [Fact]
    public void Calculate_SimilarStrings_ReturnsHighScore()
    {
        var score = StringSimilarity.Calculate(
            "Boodschappen Albert Heijn",
            "Albert Heijn boodschappen");

        score.Should().BeGreaterThan(0.5);
    }

    [Fact]
    public void Calculate_DifferentStrings_ReturnsLowScore()
    {
        var score = StringSimilarity.Calculate(
            "Boodschappen Albert Heijn",
            "Hypotheek betaling");

        score.Should().BeLessThan(0.3);
    }

    [Fact]
    public void Calculate_IsCaseInsensitive()
    {
        var score1 = StringSimilarity.Calculate("Albert Heijn", "albert heijn");
        var score2 = StringSimilarity.Calculate("Albert Heijn", "Albert Heijn");

        score1.Should().Be(score2);
    }

    [Fact]
    public void Calculate_NullInputs_ReturnsZero()
    {
        StringSimilarity.Calculate(null, "abc").Should().Be(0.0);
        StringSimilarity.Calculate("abc", null).Should().Be(0.0);
        StringSimilarity.Calculate(null, null).Should().Be(1.0);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `cd D:/claude/triodos && dotnet test tests/Triodos.KruispostMonitor.Tests --filter "StringSimilarityTests"`
Expected: FAIL — class does not exist

- [ ] **Step 3: Implement StringSimilarity using Levenshtein distance**

Write to `src/Triodos.KruispostMonitor/Matching/StringSimilarity.cs`:

```csharp
namespace Triodos.KruispostMonitor.Matching;

public static class StringSimilarity
{
    public static double Calculate(string? a, string? b)
    {
        if (a is null && b is null) return 1.0;
        if (a is null || b is null) return 0.0;

        a = a.ToLowerInvariant();
        b = b.ToLowerInvariant();

        if (a == b) return 1.0;
        if (a.Length == 0 || b.Length == 0) return 0.0;

        var maxLen = Math.Max(a.Length, b.Length);
        var distance = LevenshteinDistance(a, b);
        return 1.0 - (double)distance / maxLen;
    }

    private static int LevenshteinDistance(string a, string b)
    {
        var m = a.Length;
        var n = b.Length;
        var dp = new int[m + 1, n + 1];

        for (var i = 0; i <= m; i++) dp[i, 0] = i;
        for (var j = 0; j <= n; j++) dp[0, j] = j;

        for (var i = 1; i <= m; i++)
        {
            for (var j = 1; j <= n; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                dp[i, j] = Math.Min(
                    Math.Min(dp[i - 1, j] + 1, dp[i, j - 1] + 1),
                    dp[i - 1, j - 1] + cost);
            }
        }

        return dp[m, n];
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `cd D:/claude/triodos && dotnet test tests/Triodos.KruispostMonitor.Tests --filter "StringSimilarityTests"`
Expected: All tests passed

- [ ] **Step 5: Commit**

```bash
cd D:/claude/triodos
git add -A
git commit -m "feat: add string similarity using Levenshtein distance"
```

---

### Task 5: Transaction Matcher

**Files:**
- Create: `src/Triodos.KruispostMonitor/Matching/MatchResult.cs`
- Create: `src/Triodos.KruispostMonitor/Matching/TransactionMatcher.cs`
- Create: `tests/Triodos.KruispostMonitor.Tests/Matching/TransactionMatcherTests.cs`

- [ ] **Step 1: Write failing tests**

Write to `tests/Triodos.KruispostMonitor.Tests/Matching/TransactionMatcherTests.cs`:

```csharp
using FluentAssertions;
using Triodos.KruispostMonitor.Configuration;
using Triodos.KruispostMonitor.Matching;

namespace Triodos.KruispostMonitor.Tests.Matching;

public class TransactionMatcherTests
{
    private readonly MatchingSettings _settings = new()
    {
        SimilarityThreshold = 0.7,
        TargetBalance = 300.00m
    };

    private static TransactionRecord Debit(string id, decimal amount, string counterpart, string remittance, string date = "2026-03-20") =>
        new(id, -Math.Abs(amount), counterpart, remittance, DateTimeOffset.Parse(date));

    private static TransactionRecord Credit(string id, decimal amount, string counterpart, string remittance, string date = "2026-03-21") =>
        new(id, Math.Abs(amount), counterpart, remittance, DateTimeOffset.Parse(date));

    [Fact]
    public void Match_ExactAmountAndSimilarReference_ReturnsMatched()
    {
        var transactions = new List<TransactionRecord>
        {
            Debit("d1", 35.00m, "Albert Heijn", "Boodschappen week 12"),
            Credit("c1", 35.00m, "Albert Heijn", "Boodschappen week 12")
        };

        var result = new TransactionMatcher(_settings).Match(transactions, []);

        result.Matched.Should().HaveCount(1);
        result.Matched[0].Debit.Id.Should().Be("d1");
        result.Matched[0].Credit.Id.Should().Be("c1");
        result.UnmatchedDebits.Should().BeEmpty();
    }

    [Fact]
    public void Match_NoMatchingCredit_ReturnsUnmatched()
    {
        var transactions = new List<TransactionRecord>
        {
            Debit("d1", 35.00m, "Albert Heijn", "Boodschappen")
        };

        var result = new TransactionMatcher(_settings).Match(transactions, []);

        result.Matched.Should().BeEmpty();
        result.UnmatchedDebits.Should().HaveCount(1);
        result.UnmatchedDebits[0].Id.Should().Be("d1");
    }

    [Fact]
    public void Match_SameAmountDifferentReference_ReturnsUnmatched()
    {
        var transactions = new List<TransactionRecord>
        {
            Debit("d1", 35.00m, "Albert Heijn", "Boodschappen"),
            Credit("c1", 35.00m, "Hypotheek Bank", "Maandelijkse afschrijving")
        };

        var result = new TransactionMatcher(_settings).Match(transactions, []);

        result.Matched.Should().BeEmpty();
        result.UnmatchedDebits.Should().HaveCount(1);
        result.UnmatchedCredits.Should().HaveCount(1);
    }

    [Fact]
    public void Match_AlreadyMatchedTransactions_AreExcluded()
    {
        var transactions = new List<TransactionRecord>
        {
            Debit("d1", 35.00m, "Albert Heijn", "Boodschappen"),
            Credit("c1", 35.00m, "Albert Heijn", "Boodschappen")
        };
        var alreadyMatched = new HashSet<string> { "d1", "c1" };

        var result = new TransactionMatcher(_settings).Match(transactions, alreadyMatched);

        result.Matched.Should().BeEmpty();
        result.UnmatchedDebits.Should().BeEmpty();
    }

    [Fact]
    public void Match_MultipleCreditsForOneDebit_PicksBestMatch()
    {
        var transactions = new List<TransactionRecord>
        {
            Debit("d1", 50.00m, "Bol.com", "Bestelling 12345"),
            Credit("c1", 50.00m, "Bol.com", "Bestelling 12345"),
            Credit("c2", 50.00m, "Bol.com", "Bestelling 99999")
        };

        var result = new TransactionMatcher(_settings).Match(transactions, []);

        result.Matched.Should().HaveCount(1);
        result.Matched[0].Credit.Id.Should().Be("c1");
        result.UnmatchedCredits.Should().HaveCount(1);
    }

    [Fact]
    public void Match_PossibleMatch_WhenAmountMatchesButReferenceLowSimilarity()
    {
        var settings = new MatchingSettings { SimilarityThreshold = 0.9, TargetBalance = 300m };
        var transactions = new List<TransactionRecord>
        {
            Debit("d1", 50.00m, "Bol.com", "Bestelling 12345"),
            Credit("c1", 50.00m, "Bol.com", "Terugbetaling order")
        };

        var result = new TransactionMatcher(settings).Match(transactions, []);

        result.Matched.Should().BeEmpty();
        result.PossibleMatches.Should().HaveCount(1);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `cd D:/claude/triodos && dotnet test tests/Triodos.KruispostMonitor.Tests --filter "TransactionMatcherTests"`
Expected: FAIL — classes do not exist

- [ ] **Step 3: Implement MatchResult and TransactionRecord**

Write to `src/Triodos.KruispostMonitor/Matching/MatchResult.cs`:

```csharp
namespace Triodos.KruispostMonitor.Matching;

public record TransactionRecord(
    string Id,
    decimal Amount,
    string CounterpartName,
    string RemittanceInformation,
    DateTimeOffset ExecutionDate)
{
    public bool IsDebit => Amount < 0;
    public bool IsCredit => Amount > 0;
    public decimal AbsoluteAmount => Math.Abs(Amount);
}

public record MatchedPair(TransactionRecord Debit, TransactionRecord Credit, double Similarity);

public record PossibleMatch(TransactionRecord Debit, TransactionRecord Credit, double Similarity);

public class MatchResult
{
    public List<MatchedPair> Matched { get; init; } = [];
    public List<TransactionRecord> UnmatchedDebits { get; init; } = [];
    public List<TransactionRecord> UnmatchedCredits { get; init; } = [];
    public List<PossibleMatch> PossibleMatches { get; init; } = [];
}
```

- [ ] **Step 4: Implement TransactionMatcher**

Write to `src/Triodos.KruispostMonitor/Matching/TransactionMatcher.cs`:

```csharp
using Triodos.KruispostMonitor.Configuration;

namespace Triodos.KruispostMonitor.Matching;

public class TransactionMatcher
{
    private readonly MatchingSettings _settings;
    private const double PossibleMatchMinThreshold = 0.3;

    public TransactionMatcher(MatchingSettings settings)
    {
        _settings = settings;
    }

    public MatchResult Match(IReadOnlyList<TransactionRecord> transactions, IReadOnlySet<string> alreadyMatchedIds)
    {
        var debits = transactions
            .Where(t => t.IsDebit && !alreadyMatchedIds.Contains(t.Id))
            .ToList();

        var availableCredits = transactions
            .Where(t => t.IsCredit && !alreadyMatchedIds.Contains(t.Id))
            .ToList();

        var matched = new List<MatchedPair>();
        var possibleMatches = new List<PossibleMatch>();
        var usedCreditIds = new HashSet<string>();

        foreach (var debit in debits)
        {
            var bestCredit = FindBestCredit(debit, availableCredits, usedCreditIds);

            if (bestCredit is null)
                continue;

            if (bestCredit.Value.similarity >= _settings.SimilarityThreshold)
            {
                matched.Add(new MatchedPair(debit, bestCredit.Value.credit, bestCredit.Value.similarity));
                usedCreditIds.Add(bestCredit.Value.credit.Id);
            }
            else if (bestCredit.Value.similarity >= PossibleMatchMinThreshold)
            {
                possibleMatches.Add(new PossibleMatch(debit, bestCredit.Value.credit, bestCredit.Value.similarity));
            }
        }

        var matchedDebitIds = matched.Select(m => m.Debit.Id).ToHashSet();
        var unmatchedDebits = debits
            .Where(d => !matchedDebitIds.Contains(d.Id))
            .ToList();

        var unmatchedCredits = availableCredits
            .Where(c => !usedCreditIds.Contains(c.Id))
            .ToList();

        return new MatchResult
        {
            Matched = matched,
            UnmatchedDebits = unmatchedDebits,
            UnmatchedCredits = unmatchedCredits,
            PossibleMatches = possibleMatches
        };
    }

    private static (TransactionRecord credit, double similarity)? FindBestCredit(
        TransactionRecord debit,
        List<TransactionRecord> credits,
        HashSet<string> usedCreditIds)
    {
        (TransactionRecord credit, double similarity)? best = null;

        foreach (var credit in credits)
        {
            if (usedCreditIds.Contains(credit.Id))
                continue;

            if (credit.AbsoluteAmount != debit.AbsoluteAmount)
                continue;

            var nameSimilarity = StringSimilarity.Calculate(debit.CounterpartName, credit.CounterpartName);
            var refSimilarity = StringSimilarity.Calculate(debit.RemittanceInformation, credit.RemittanceInformation);
            var similarity = Math.Max(nameSimilarity, refSimilarity);

            if (best is null || similarity > best.Value.similarity ||
                (Math.Abs(similarity - best.Value.similarity) < 0.001 &&
                 Math.Abs((credit.ExecutionDate - debit.ExecutionDate).TotalDays) <
                 Math.Abs((best.Value.credit.ExecutionDate - debit.ExecutionDate).TotalDays)))
            {
                best = (credit, similarity);
            }
        }

        return best;
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `cd D:/claude/triodos && dotnet test tests/Triodos.KruispostMonitor.Tests --filter "TransactionMatcherTests"`
Expected: All tests passed

- [ ] **Step 6: Commit**

```bash
cd D:/claude/triodos
git add -A
git commit -m "feat: add transaction matcher with amount and reference matching"
```

---

### Task 6: Notification Message Builder

**Files:**
- Create: `src/Triodos.KruispostMonitor/Notifications/NotificationMessageBuilder.cs`
- Create: `tests/Triodos.KruispostMonitor.Tests/Notifications/NotificationMessageBuilderTests.cs`

- [ ] **Step 1: Write failing tests**

Write to `tests/Triodos.KruispostMonitor.Tests/Notifications/NotificationMessageBuilderTests.cs`:

```csharp
using FluentAssertions;
using Triodos.KruispostMonitor.Matching;
using Triodos.KruispostMonitor.Notifications;

namespace Triodos.KruispostMonitor.Tests.Notifications;

public class NotificationMessageBuilderTests
{
    [Fact]
    public void Build_WithUnmatchedDebits_IncludesThemInMessage()
    {
        var result = new MatchResult
        {
            UnmatchedDebits =
            [
                new TransactionRecord("d1", -35.00m, "Albert Heijn", "Boodschappen", DateTimeOffset.Parse("2026-03-20"))
            ]
        };

        var message = NotificationMessageBuilder.Build(result, currentBalance: 265.00m, targetBalance: 300.00m);

        message.Should().Contain("1 unmatched expense");
        message.Should().Contain("Albert Heijn");
        message.Should().Contain("35.00");
        message.Should().Contain("265.00");
        message.Should().Contain("300.00");
    }

    [Fact]
    public void Build_WithPossibleMatches_IncludesThemInMessage()
    {
        var result = new MatchResult
        {
            UnmatchedDebits =
            [
                new TransactionRecord("d1", -17.50m, "Bol.com", "Bestelling 123", DateTimeOffset.Parse("2026-03-22"))
            ],
            PossibleMatches =
            [
                new PossibleMatch(
                    new TransactionRecord("d1", -17.50m, "Bol.com", "Bestelling 123", DateTimeOffset.Parse("2026-03-22")),
                    new TransactionRecord("c1", 17.50m, "Bol.com", "Terugbetaling", DateTimeOffset.Parse("2026-03-24")),
                    0.5)
            ]
        };

        var message = NotificationMessageBuilder.Build(result, currentBalance: 282.50m, targetBalance: 300.00m);

        message.Should().Contain("Possible match");
        message.Should().Contain("Terugbetaling");
    }

    [Fact]
    public void Build_BalanceMatchesTarget_DoesNotShowDelta()
    {
        var result = new MatchResult();

        var message = NotificationMessageBuilder.Build(result, currentBalance: 300.00m, targetBalance: 300.00m);

        message.Should().BeNull();
    }

    [Fact]
    public void Build_BalanceDeviates_ShowsDelta()
    {
        var result = new MatchResult();

        var message = NotificationMessageBuilder.Build(result, currentBalance: 250.00m, targetBalance: 300.00m);

        message.Should().Contain("-50.00");
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `cd D:/claude/triodos && dotnet test tests/Triodos.KruispostMonitor.Tests --filter "NotificationMessageBuilderTests"`
Expected: FAIL — class does not exist

- [ ] **Step 3: Implement NotificationMessageBuilder**

Write to `src/Triodos.KruispostMonitor/Notifications/NotificationMessageBuilder.cs`:

```csharp
using System.Globalization;
using System.Text;
using Triodos.KruispostMonitor.Matching;

namespace Triodos.KruispostMonitor.Notifications;

public static class NotificationMessageBuilder
{
    public static string? Build(MatchResult result, decimal currentBalance, decimal targetBalance)
    {
        var hasUnmatched = result.UnmatchedDebits.Count > 0;
        var balanceDeviation = currentBalance - targetBalance;
        var hasBalanceIssue = balanceDeviation != 0;

        if (!hasUnmatched && !hasBalanceIssue)
            return null;

        var sb = new StringBuilder();
        var culture = CultureInfo.InvariantCulture;

        if (hasUnmatched)
        {
            var count = result.UnmatchedDebits.Count;
            sb.AppendLine($"Kruispost Monitor — {count} unmatched expense{(count != 1 ? "s" : "")} found");
        }
        else
        {
            sb.AppendLine("Kruispost Monitor — balance deviation detected");
        }

        sb.AppendLine();
        sb.AppendLine(string.Format(culture,
            "Balance: EUR {0:F2} (expected: EUR {1:F2}, delta: {2:F2})",
            currentBalance, targetBalance, balanceDeviation));

        if (hasUnmatched)
        {
            sb.AppendLine();
            sb.AppendLine("Unmatched expenses:");
            for (var i = 0; i < result.UnmatchedDebits.Count; i++)
            {
                var d = result.UnmatchedDebits[i];
                sb.AppendLine(string.Format(culture,
                    "  {0}. {1:yyyy-MM-dd}  -EUR {2:F2}  {3}  \"{4}\"",
                    i + 1, d.ExecutionDate, d.AbsoluteAmount, d.CounterpartName, d.RemittanceInformation));
            }
        }

        if (result.PossibleMatches.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Possible matches (low confidence):");
            for (var i = 0; i < result.PossibleMatches.Count; i++)
            {
                var pm = result.PossibleMatches[i];
                sb.AppendLine(string.Format(culture,
                    "  {0}. {1:yyyy-MM-dd}  -EUR {2:F2}  {3} <-> {4:yyyy-MM-dd}  +EUR {5:F2}  \"{6}\"",
                    i + 1, pm.Debit.ExecutionDate, pm.Debit.AbsoluteAmount, pm.Debit.CounterpartName,
                    pm.Credit.ExecutionDate, pm.Credit.AbsoluteAmount, pm.Credit.RemittanceInformation));
            }
        }

        return sb.ToString().TrimEnd();
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `cd D:/claude/triodos && dotnet test tests/Triodos.KruispostMonitor.Tests --filter "NotificationMessageBuilderTests"`
Expected: All tests passed

- [ ] **Step 5: Commit**

```bash
cd D:/claude/triodos
git add -A
git commit -m "feat: add notification message builder"
```

---

### Task 7: Notification Senders

**Files:**
- Create: `src/Triodos.KruispostMonitor/Notifications/INotificationSender.cs`
- Create: `src/Triodos.KruispostMonitor/Notifications/SlackNotificationSender.cs`
- Create: `src/Triodos.KruispostMonitor/Notifications/EmailNotificationSender.cs`

- [ ] **Step 1: Create INotificationSender interface**

Write to `src/Triodos.KruispostMonitor/Notifications/INotificationSender.cs`:

```csharp
namespace Triodos.KruispostMonitor.Notifications;

public interface INotificationSender
{
    Task SendAsync(string message, CancellationToken cancellationToken = default);
    bool IsEnabled { get; }
}
```

- [ ] **Step 2: Implement SlackNotificationSender**

Write to `src/Triodos.KruispostMonitor/Notifications/SlackNotificationSender.cs`:

```csharp
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Triodos.KruispostMonitor.Configuration;

namespace Triodos.KruispostMonitor.Notifications;

public class SlackNotificationSender : INotificationSender
{
    private readonly HttpClient _httpClient;
    private readonly SlackSettings _settings;
    private readonly ILogger<SlackNotificationSender> _logger;

    public bool IsEnabled => _settings.Enabled && !string.IsNullOrEmpty(_settings.WebhookUrl);

    public SlackNotificationSender(
        HttpClient httpClient,
        IOptions<NotificationSettings> settings,
        ILogger<SlackNotificationSender> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value.Slack;
        _logger = logger;
    }

    public async Task SendAsync(string message, CancellationToken cancellationToken = default)
    {
        if (!IsEnabled) return;

        var payload = JsonSerializer.Serialize(new { text = message });
        var content = new StringContent(payload, Encoding.UTF8, "application/json");

        var response = await _httpClient.PostAsync(_settings.WebhookUrl, content, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Slack notification failed with status {StatusCode}", response.StatusCode);
        }
    }
}
```

- [ ] **Step 3: Implement EmailNotificationSender**

Write to `src/Triodos.KruispostMonitor/Notifications/EmailNotificationSender.cs`:

```csharp
using MailKit.Net.Smtp;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using Triodos.KruispostMonitor.Configuration;

namespace Triodos.KruispostMonitor.Notifications;

public class EmailNotificationSender : INotificationSender
{
    private readonly EmailSettings _settings;
    private readonly ILogger<EmailNotificationSender> _logger;

    public bool IsEnabled => _settings.Enabled && !string.IsNullOrEmpty(_settings.SmtpHost);

    public EmailNotificationSender(
        IOptions<NotificationSettings> settings,
        ILogger<EmailNotificationSender> logger)
    {
        _settings = settings.Value.Email;
        _logger = logger;
    }

    public async Task SendAsync(string message, CancellationToken cancellationToken = default)
    {
        if (!IsEnabled) return;

        var email = new MimeMessage();
        email.From.Add(MailboxAddress.Parse(_settings.FromAddress));
        foreach (var to in _settings.ToAddresses)
            email.To.Add(MailboxAddress.Parse(to));

        email.Subject = message.Split('\n', 2)[0];
        email.Body = new TextPart("plain") { Text = message };

        using var smtp = new SmtpClient();
        await smtp.ConnectAsync(_settings.SmtpHost, _settings.SmtpPort, _settings.UseSsl, cancellationToken);

        if (!string.IsNullOrEmpty(_settings.Username))
            await smtp.AuthenticateAsync(_settings.Username, _settings.Password, cancellationToken);

        await smtp.SendAsync(email, cancellationToken);
        await smtp.DisconnectAsync(true, cancellationToken);

        _logger.LogInformation("Email notification sent to {Recipients}", string.Join(", ", _settings.ToAddresses));
    }
}
```

- [ ] **Step 4: Verify build**

Run: `cd D:/claude/triodos && dotnet build`
Expected: Build succeeded with 0 errors

- [ ] **Step 5: Commit**

```bash
cd D:/claude/triodos
git add -A
git commit -m "feat: add Slack and email notification senders"
```

---

### Task 8: Ponto Service

**Files:**
- Create: `src/Triodos.KruispostMonitor/Ponto/IPontoService.cs`
- Create: `src/Triodos.KruispostMonitor/Ponto/PontoService.cs`

- [ ] **Step 1: Create IPontoService interface**

Write to `src/Triodos.KruispostMonitor/Ponto/IPontoService.cs`:

```csharp
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
```

- [ ] **Step 2: Implement PontoService using Ibanity SDK**

Write to `src/Triodos.KruispostMonitor/Ponto/PontoService.cs`:

```csharp
using Ibanity.Apis.Client;
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

    public string? LatestRefreshToken => _token?.RefreshToken;

    public PontoService(IOptions<PontoSettings> settings, ILogger<PontoService> logger)
    {
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<string> InitializeAsync(string refreshToken)
    {
        _ibanityService = new IbanityServiceBuilder()
            .SetEndpoint(_settings.ApiEndpoint)
            .AddClientCertificate(_settings.CertificatePath, _settings.CertificatePassword)
            .AddPontoConnectOAuth2Authentication(_settings.ClientId, _settings.ClientSecret)
            .Build();

        _token = await _ibanityService.PontoConnect.TokenService.GetToken(refreshToken);
        _logger.LogInformation("Ponto authentication successful");
        return _token.RefreshToken;
    }

    public async Task<AccountInfo?> GetAccountByIbanAsync(string iban)
    {
        EnsureInitialized();
        var accounts = await _ibanityService!.PontoConnect.Accounts.List(_token!);

        foreach (var page in new[] { accounts })
        {
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
        }

        _logger.LogWarning("Account with IBAN {Iban} not found", iban);
        return null;
    }

    public async Task TriggerSynchronizationAsync(string accountId)
    {
        EnsureInitialized();
        var sync = new Synchronization
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

            if (!page.ContinuationToken.HasValue)
                break;

            page = await _ibanityService.PontoConnect.Transactions.List(_token!, accountGuid, page.ContinuationToken.Value);
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
```

- [ ] **Step 3: Verify build**

Run: `cd D:/claude/triodos && dotnet build`
Expected: Build succeeded with 0 errors. Note: some Ibanity SDK types may differ — adjust property/method names based on build errors.

- [ ] **Step 4: Commit**

```bash
cd D:/claude/triodos
git add -A
git commit -m "feat: add Ponto service for bank account and transaction access"
```

---

### Task 9: Program.cs — Orchestration

**Files:**
- Modify: `src/Triodos.KruispostMonitor/Program.cs`

- [ ] **Step 1: Implement Program.cs**

Write to `src/Triodos.KruispostMonitor/Program.cs`:

```csharp
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
```

- [ ] **Step 2: Verify build**

Run: `cd D:/claude/triodos && dotnet build`
Expected: Build succeeded with 0 errors

- [ ] **Step 3: Commit**

```bash
cd D:/claude/triodos
git add -A
git commit -m "feat: add Program.cs with full orchestration pipeline"
```

---

### Task 10: Add .gitignore and finalize

**Files:**
- Create: `.gitignore`

- [ ] **Step 1: Create .gitignore**

Write to `D:/claude/triodos/.gitignore`:

```
bin/
obj/
*.user
*.suo
.vs/
*.DotSettings.user
state.json
appsettings.Development.json
appsettings.*.json
!appsettings.json
```

- [ ] **Step 2: Verify full test suite passes**

Run: `cd D:/claude/triodos && dotnet test`
Expected: All tests pass

- [ ] **Step 3: Verify the application builds and runs (shows help/error due to missing config)**

Run: `cd D:/claude/triodos && dotnet run --project src/Triodos.KruispostMonitor`
Expected: Exits with error about missing Ponto credentials (which is expected — proves the app runs)

- [ ] **Step 4: Commit**

```bash
cd D:/claude/triodos
git add -A
git commit -m "feat: add .gitignore and finalize project"
```

---

## Post-Implementation Checklist

- [ ] All tests pass
- [ ] App builds without errors
- [ ] Configuration is documented in appsettings.json with sensible defaults
- [ ] Sensitive values (secrets, tokens) are empty in committed config
- [ ] state.json is git-ignored

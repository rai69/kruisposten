# Web App Conversion Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Convert the Kruispost Monitor from a console app to an always-running ASP.NET Core web app with a dashboard, file watcher for automatic MT940 processing, and Docker deployment.

**Architecture:** Single-process ASP.NET Core web app. `FileWatcherService` (BackgroundService) monitors a folder for MT940 files. `ProcessingService` encapsulates the parse → match → notify flow. `MonitorState` singleton shares state between the dashboard API and background processing. Dashboard UI is always available at `/`.

**Tech Stack:** .NET 9, ASP.NET Core (Kestrel), vanilla HTML/JS/CSS, Docker

---

## File Structure

```
src/Triodos.KruispostMonitor/
├── Triodos.KruispostMonitor.csproj    # SDK → Microsoft.NET.Sdk.Web
├── Program.cs                          # WebApplication setup + API endpoints
├── appsettings.json                    # Add FileWatcher section
├── Services/
│   ├── MonitorState.cs                 # Singleton shared state
│   ├── ProcessingService.cs            # Parse → match → notify → save
│   └── FileWatcherService.cs           # BackgroundService watching folder
├── Configuration/
│   └── AppSettings.cs                  # Add FileWatcherSettings
├── Interactive/
│   └── InteractivePage.cs              # Dashboard HTML (evolved from interactive page)
├── (all other folders unchanged)

Dockerfile
docker-compose.yml
```

---

### Task 1: Add FileWatcherSettings and update appsettings.json

**Files:**
- Modify: `src/Triodos.KruispostMonitor/Configuration/AppSettings.cs`
- Modify: `src/Triodos.KruispostMonitor/appsettings.json`

- [ ] **Step 1: Add FileWatcherSettings class**

Add to the end of `src/Triodos.KruispostMonitor/Configuration/AppSettings.cs`:

```csharp
public class FileWatcherSettings
{
    public const string SectionName = "FileWatcher";
    public string WatchPath { get; set; } = "/data/import";
    public string ProcessedPath { get; set; } = "/data/processed";
}
```

- [ ] **Step 2: Update appsettings.json**

Add the FileWatcher section and update the State path. The full `appsettings.json` should be:

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
    "SimilarityThreshold": 0.5,
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
    "FilePath": "/data/state/state.json"
  },
  "TransactionSource": {
    "Mode": "Mt940",
    "Mt940": {
      "FilePath": ""
    }
  },
  "FileWatcher": {
    "WatchPath": "/data/import",
    "ProcessedPath": "/data/processed"
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
git commit -m "feat: add FileWatcherSettings and update appsettings.json for web app"
```

---

### Task 2: Create MonitorState singleton

**Files:**
- Create: `src/Triodos.KruispostMonitor/Services/MonitorState.cs`

- [ ] **Step 1: Create MonitorState.cs**

Write to `src/Triodos.KruispostMonitor/Services/MonitorState.cs`:

```csharp
using Triodos.KruispostMonitor.Matching;
using Triodos.KruispostMonitor.State;

namespace Triodos.KruispostMonitor.Services;

public record ProcessingRun(
    DateTimeOffset Timestamp,
    string FileName,
    int TransactionCount,
    int AutoMatched,
    int UnmatchedDebits,
    int UnmatchedCredits);

public class MonitorState
{
    private readonly object _lock = new();

    public MatchResult? CurrentMatchResult { get; private set; }
    public List<TransactionRecord> AllTransactions { get; private set; } = [];
    public decimal CurrentBalance { get; private set; }
    public string Currency { get; private set; } = "EUR";
    public string AccountIdentifier { get; private set; } = "";
    public RunState State { get; private set; } = new();
    public List<ProcessingRun> History { get; private set; } = [];
    public string? LastProcessedFile { get; private set; }
    public bool IsWatching { get; set; }

    // Pending manual matches (not yet saved to state)
    public List<ManualMatch> PendingManualMatches { get; private set; } = [];
    public List<TransactionRecord> UnmatchedDebits { get; private set; } = [];
    public List<TransactionRecord> UnmatchedCredits { get; private set; } = [];

    public void UpdateFromProcessing(
        MatchResult matchResult,
        List<TransactionRecord> allTransactions,
        decimal currentBalance,
        string currency,
        string accountIdentifier,
        RunState state,
        string fileName)
    {
        lock (_lock)
        {
            CurrentMatchResult = matchResult;
            AllTransactions = allTransactions;
            CurrentBalance = currentBalance;
            Currency = currency;
            AccountIdentifier = accountIdentifier;
            State = state;
            LastProcessedFile = fileName;
            PendingManualMatches = [];
            UnmatchedDebits = new List<TransactionRecord>(matchResult.UnmatchedDebits);
            UnmatchedCredits = new List<TransactionRecord>(matchResult.UnmatchedCredits);

            History.Insert(0, new ProcessingRun(
                DateTimeOffset.UtcNow,
                Path.GetFileName(fileName),
                allTransactions.Count,
                matchResult.Matched.Count,
                matchResult.UnmatchedDebits.Count,
                matchResult.UnmatchedCredits.Count));

            // Keep last 50 runs
            if (History.Count > 50)
                History.RemoveRange(50, History.Count - 50);
        }
    }

    public bool TryAddManualMatch(List<string> debitIds, List<string> creditIds, out string? error)
    {
        lock (_lock)
        {
            var debits = UnmatchedDebits.Where(t => debitIds.Contains(t.Id)).ToList();
            var credits = UnmatchedCredits.Where(t => creditIds.Contains(t.Id)).ToList();

            if (debits.Count == 0 || credits.Count == 0)
            {
                error = "Must select at least one debit and one credit";
                return false;
            }

            var net = debits.Sum(t => t.Amount) + credits.Sum(t => t.Amount);
            if (Math.Abs(net) >= 0.005m)
            {
                error = $"Selection does not balance: {net:F2}";
                return false;
            }

            PendingManualMatches.Add(new ManualMatch(debitIds, creditIds));
            UnmatchedDebits.RemoveAll(t => debitIds.Contains(t.Id));
            UnmatchedCredits.RemoveAll(t => creditIds.Contains(t.Id));
            error = null;
            return true;
        }
    }

    public bool TryUndoManualMatch(int index, out string? error)
    {
        lock (_lock)
        {
            if (index < 0 || index >= PendingManualMatches.Count)
            {
                error = "Invalid match index";
                return false;
            }

            var mm = PendingManualMatches[index];
            var debits = mm.DebitIds.Select(id => AllTransactions.First(t => t.Id == id));
            var credits = mm.CreditIds.Select(id => AllTransactions.First(t => t.Id == id));

            UnmatchedDebits.AddRange(debits);
            UnmatchedCredits.AddRange(credits);
            PendingManualMatches.RemoveAt(index);
            error = null;
            return true;
        }
    }

    public void SaveManualMatches()
    {
        lock (_lock)
        {
            State.ManualMatches.AddRange(PendingManualMatches);
            foreach (var mm in PendingManualMatches)
            {
                foreach (var id in mm.DebitIds) State.MatchedTransactionIds.Add(id);
                foreach (var id in mm.CreditIds) State.MatchedTransactionIds.Add(id);
            }
            PendingManualMatches = [];
        }
    }
}
```

- [ ] **Step 2: Verify build**

Run: `cd D:/claude/triodos && dotnet build`
Expected: Build succeeded with 0 errors

- [ ] **Step 3: Commit**

```bash
cd D:/claude/triodos
git add src/Triodos.KruispostMonitor/Services/MonitorState.cs
git commit -m "feat: add MonitorState singleton for shared dashboard state"
```

---

### Task 3: Create ProcessingService

**Files:**
- Create: `src/Triodos.KruispostMonitor/Services/ProcessingService.cs`

- [ ] **Step 1: Create ProcessingService.cs**

Write to `src/Triodos.KruispostMonitor/Services/ProcessingService.cs`:

```csharp
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

        var content = await File.ReadAllTextAsync(filePath);
        var statement = Mt940Parser.Parse(content);

        var sourceResult = new TransactionSourceResult(
            statement.Transactions,
            statement.ClosingBalance,
            statement.Currency,
            statement.AccountIdentification);

        await ProcessAsync(sourceResult, Path.GetFileName(filePath));
    }

    public async Task ProcessAsync(TransactionSourceResult sourceResult, string sourceName)
    {
        _logger.LogInformation("Account {Account}, balance {Balance} {Currency}, {Count} transactions",
            sourceResult.AccountIdentifier, sourceResult.CurrentBalance, sourceResult.Currency, sourceResult.Transactions.Count);

        // Load state
        var state = await _stateStore.LoadAsync();

        // Build exclusion set (auto-matched + manual matches)
        var excludedIds = new HashSet<string>(state.MatchedTransactionIds);
        foreach (var mm in state.ManualMatches)
        {
            foreach (var id in mm.DebitIds) excludedIds.Add(id);
            foreach (var id in mm.CreditIds) excludedIds.Add(id);
        }

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
```

- [ ] **Step 2: Verify build**

Run: `cd D:/claude/triodos && dotnet build`
Expected: Build succeeded with 0 errors

- [ ] **Step 3: Commit**

```bash
cd D:/claude/triodos
git add src/Triodos.KruispostMonitor/Services/ProcessingService.cs
git commit -m "feat: add ProcessingService encapsulating the processing pipeline"
```

---

### Task 4: Create FileWatcherService

**Files:**
- Create: `src/Triodos.KruispostMonitor/Services/FileWatcherService.cs`

- [ ] **Step 1: Create FileWatcherService.cs**

Write to `src/Triodos.KruispostMonitor/Services/FileWatcherService.cs`:

```csharp
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Triodos.KruispostMonitor.Configuration;

namespace Triodos.KruispostMonitor.Services;

public class FileWatcherService : BackgroundService
{
    private readonly FileWatcherSettings _settings;
    private readonly ProcessingService _processingService;
    private readonly MonitorState _monitorState;
    private readonly ILogger<FileWatcherService> _logger;

    public FileWatcherService(
        IOptions<FileWatcherSettings> settings,
        ProcessingService processingService,
        MonitorState monitorState,
        ILogger<FileWatcherService> logger)
    {
        _settings = settings.Value;
        _processingService = processingService;
        _monitorState = monitorState;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Ensure directories exist
        Directory.CreateDirectory(_settings.WatchPath);
        Directory.CreateDirectory(_settings.ProcessedPath);

        // Process any existing files on startup
        await ProcessExistingFilesAsync();

        _logger.LogInformation("Watching for MT940 files in {Path}", _settings.WatchPath);
        _monitorState.IsWatching = true;

        using var watcher = new FileSystemWatcher(_settings.WatchPath);
        watcher.Filter = "*.*";
        watcher.NotifyFilter = NotifyFilters.FileName | NotifyFilters.CreationTime;
        watcher.EnableRaisingEvents = true;

        var fileQueue = new Queue<string>();
        watcher.Created += (_, e) =>
        {
            if (IsMatchingFile(e.FullPath))
            {
                lock (fileQueue) { fileQueue.Enqueue(e.FullPath); }
            }
        };

        while (!stoppingToken.IsCancellationRequested)
        {
            string? filePath = null;
            lock (fileQueue)
            {
                if (fileQueue.Count > 0) filePath = fileQueue.Dequeue();
            }

            if (filePath is not null)
            {
                // Wait for file to finish writing
                await Task.Delay(2000, stoppingToken);
                await ProcessAndMoveFileAsync(filePath);
            }
            else
            {
                await Task.Delay(1000, stoppingToken);
            }
        }

        _monitorState.IsWatching = false;
    }

    private async Task ProcessExistingFilesAsync()
    {
        var files = Directory.GetFiles(_settings.WatchPath)
            .Where(IsMatchingFile)
            .OrderBy(f => File.GetCreationTimeUtc(f))
            .ToList();

        foreach (var file in files)
        {
            await ProcessAndMoveFileAsync(file);
        }
    }

    private async Task ProcessAndMoveFileAsync(string filePath)
    {
        try
        {
            await _processingService.ProcessFileAsync(filePath);

            // Move to processed folder
            var destPath = Path.Combine(_settings.ProcessedPath, Path.GetFileName(filePath));
            if (File.Exists(destPath))
                destPath = Path.Combine(_settings.ProcessedPath,
                    $"{Path.GetFileNameWithoutExtension(filePath)}_{DateTimeOffset.UtcNow:yyyyMMddHHmmss}{Path.GetExtension(filePath)}");

            File.Move(filePath, destPath);
            _logger.LogInformation("Processed and moved file to {Dest}", destPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process file {FilePath}", filePath);
        }
    }

    private static bool IsMatchingFile(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is ".mt940" or ".sta";
    }
}
```

- [ ] **Step 2: Verify build**

Run: `cd D:/claude/triodos && dotnet build`
Expected: Build succeeded with 0 errors

- [ ] **Step 3: Commit**

```bash
cd D:/claude/triodos
git add src/Triodos.KruispostMonitor/Services/FileWatcherService.cs
git commit -m "feat: add FileWatcherService for automatic MT940 processing"
```

---

### Task 5: Evolve InteractivePage into Dashboard

**Files:**
- Modify: `src/Triodos.KruispostMonitor/Interactive/InteractivePage.cs`

- [ ] **Step 1: Rewrite InteractivePage.cs as dashboard**

The page evolves from the interactive matching page. Key changes:
- Add header with status info (last processed file, watching indicator)
- Add processing history table
- Remove "Save & finish" → replace with "Save matches" (saves but keeps app running)
- Add "Reprocess" button
- Add auto-refresh polling (every 5 seconds)
- Remove the finished overlay

Write the complete updated file to `src/Triodos.KruispostMonitor/Interactive/InteractivePage.cs`. The file is large (HTML/JS/CSS in a raw string literal). The key differences from the current version:

1. The title changes to "Kruispost Monitor — Dashboard"
2. A status bar is added showing last processed file, watch status, and last run time
3. A processing history section is added between summary and auto-matched
4. The `loadData()` function polls every 5 seconds: `setTimeout(loadData, 5000)` at the end
5. The `/api/data` response now includes `history`, `lastProcessedFile`, `isWatching`, `lastRunUtc` fields
6. "Save & finish" button becomes "Save matches" that calls `POST /api/save-matches` (saves but doesn't stop)
7. A "Reprocess" button calls `POST /api/process`
8. The finished overlay is removed

The implementer should read the current `InteractivePage.cs` and make these modifications while keeping all existing matching UI logic intact.

- [ ] **Step 2: Verify build**

Run: `cd D:/claude/triodos && dotnet build`
Expected: Build succeeded with 0 errors

- [ ] **Step 3: Commit**

```bash
cd D:/claude/triodos
git add src/Triodos.KruispostMonitor/Interactive/InteractivePage.cs
git commit -m "feat: evolve InteractivePage into always-on dashboard"
```

---

### Task 6: Change SDK and rewrite Program.cs

**Files:**
- Modify: `src/Triodos.KruispostMonitor/Triodos.KruispostMonitor.csproj`
- Modify: `src/Triodos.KruispostMonitor/Program.cs`
- Delete: `src/Triodos.KruispostMonitor/Interactive/InteractiveServer.cs`

- [ ] **Step 1: Update csproj to Web SDK**

Replace the content of `src/Triodos.KruispostMonitor/Triodos.KruispostMonitor.csproj` with:

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">

  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <UserSecretsId>e97bfc13-c795-4a5e-87a2-416dbedd4857</UserSecretsId>
  </PropertyGroup>

  <ItemGroup>
    <Content Include="appsettings.json" CopyToOutputDirectory="PreserveNewest" />
    <Content Include="appsettings.*.json" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Ibanity" Version="0.6.7" />
    <PackageReference Include="MailKit" Version="4.15.1" />
    <PackageReference Include="Microsoft.Extensions.Http" Version="10.0.5" />
  </ItemGroup>

</Project>
```

Note: `Microsoft.NET.Sdk.Web` includes ASP.NET Core and Hosting — the separate `FrameworkReference` and `Microsoft.Extensions.Hosting` package are no longer needed.

- [ ] **Step 2: Delete InteractiveServer.cs**

Delete `src/Triodos.KruispostMonitor/Interactive/InteractiveServer.cs` — its endpoints are now in Program.cs.

- [ ] **Step 3: Rewrite Program.cs**

Write to `src/Triodos.KruispostMonitor/Program.cs`:

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using Triodos.KruispostMonitor.Configuration;
using Triodos.KruispostMonitor.Interactive;
using Triodos.KruispostMonitor.Matching;
using Triodos.KruispostMonitor.Notifications;
using Triodos.KruispostMonitor.Ponto;
using Triodos.KruispostMonitor.Services;
using Triodos.KruispostMonitor.State;
using Triodos.KruispostMonitor.TransactionSource;

var builder = WebApplication.CreateBuilder(args);

// Configuration
builder.Services.Configure<PontoSettings>(builder.Configuration.GetSection(PontoSettings.SectionName));
builder.Services.Configure<MatchingSettings>(builder.Configuration.GetSection(MatchingSettings.SectionName));
builder.Services.Configure<NotificationSettings>(builder.Configuration.GetSection(NotificationSettings.SectionName));
builder.Services.Configure<StateSettings>(builder.Configuration.GetSection(StateSettings.SectionName));
builder.Services.Configure<TransactionSourceSettings>(builder.Configuration.GetSection(TransactionSourceSettings.SectionName));
builder.Services.Configure<FileWatcherSettings>(builder.Configuration.GetSection(FileWatcherSettings.SectionName));

// Services
builder.Services.AddSingleton<IPontoService, PontoService>();
builder.Services.AddSingleton<IStateStore>(sp =>
    new StateStore(sp.GetRequiredService<IOptions<StateSettings>>().Value.FilePath));
builder.Services.AddHttpClient<SlackNotificationSender>();
builder.Services.AddSingleton<INotificationSender, SlackNotificationSender>(sp => sp.GetRequiredService<SlackNotificationSender>());
builder.Services.AddSingleton<INotificationSender, EmailNotificationSender>();
builder.Services.AddSingleton<MonitorState>();
builder.Services.AddSingleton<ProcessingService>();
builder.Services.AddHostedService<FileWatcherService>();

var app = builder.Build();

var jsonOptions = new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
};

// Dashboard
app.MapGet("/", () => Results.Content(InteractivePage.GetHtml(), "text/html"));

// API — data for dashboard
app.MapGet("/api/data", (MonitorState monitor) =>
{
    if (monitor.CurrentMatchResult is null)
        return Results.Json(new { ready = false, isWatching = monitor.IsWatching }, jsonOptions);

    return Results.Json(new
    {
        ready = true,
        accountIdentifier = monitor.AccountIdentifier,
        currency = monitor.Currency,
        currentBalance = monitor.CurrentBalance,
        transactionCount = monitor.AllTransactions.Count,
        lastProcessedFile = monitor.LastProcessedFile,
        lastRunUtc = monitor.State.LastRunUtc?.ToString("o"),
        isWatching = monitor.IsWatching,
        history = monitor.History.Take(20),
        autoMatched = monitor.CurrentMatchResult.Matched.Select(m => new
        {
            debit = TxDto(m.Debit),
            credit = TxDto(m.Credit)
        }),
        manualMatches = monitor.PendingManualMatches.Select((mm, i) =>
        {
            var debits = mm.DebitIds.Select(id => monitor.AllTransactions.First(t => t.Id == id)).ToList();
            var credits = mm.CreditIds.Select(id => monitor.AllTransactions.First(t => t.Id == id)).ToList();
            return new { debits = debits.Select(TxDto), credits = credits.Select(TxDto) };
        }),
        unmatchedDebits = monitor.UnmatchedDebits.Select(TxDto),
        unmatchedCredits = monitor.UnmatchedCredits.Select(TxDto)
    }, jsonOptions);
});

// API — manual match
app.MapPost("/api/match", async (HttpContext ctx, MonitorState monitor) =>
{
    var body = await JsonSerializer.DeserializeAsync<MatchRequest>(ctx.Request.Body, jsonOptions);
    if (body is null) return Results.BadRequest("Invalid request");

    if (monitor.TryAddManualMatch(body.DebitIds, body.CreditIds, out var error))
        return Results.Ok();
    return Results.BadRequest(error);
});

// API — undo manual match
app.MapPost("/api/unmatch", async (HttpContext ctx, MonitorState monitor) =>
{
    var body = await JsonSerializer.DeserializeAsync<UnmatchRequest>(ctx.Request.Body, jsonOptions);
    if (body is null) return Results.BadRequest("Invalid request");

    if (monitor.TryUndoManualMatch(body.Index, out var error))
        return Results.Ok();
    return Results.BadRequest(error);
});

// API — save manual matches
app.MapPost("/api/save-matches", async (MonitorState monitor, IStateStore stateStore) =>
{
    monitor.SaveManualMatches();
    await stateStore.SaveAsync(monitor.State);
    return Results.Ok();
});

// API — manually trigger reprocessing
app.MapPost("/api/process", async (MonitorState monitor, ProcessingService processing, IOptions<FileWatcherSettings> settings) =>
{
    if (monitor.LastProcessedFile is null)
        return Results.BadRequest("No file has been processed yet");

    // Reprocess from the processed folder
    var processedPath = Path.Combine(settings.Value.ProcessedPath, monitor.LastProcessedFile);
    if (!File.Exists(processedPath))
        return Results.BadRequest($"File not found: {monitor.LastProcessedFile}");

    await processing.ProcessFileAsync(processedPath);
    return Results.Ok();
});

app.Run("http://0.0.0.0:8080");

static object TxDto(TransactionRecord t) => new
{
    id = t.Id,
    amount = t.Amount,
    absoluteAmount = t.AbsoluteAmount,
    counterpartName = t.CounterpartName,
    remittanceInformation = t.RemittanceInformation,
    executionDate = t.ExecutionDate.ToString("yyyy-MM-dd")
};

record MatchRequest(List<string> DebitIds, List<string> CreditIds);
record UnmatchRequest(int Index);
```

- [ ] **Step 4: Verify build**

Run: `cd D:/claude/triodos && dotnet build`
Expected: Build succeeded with 0 errors

- [ ] **Step 5: Run all tests**

Run: `cd D:/claude/triodos && dotnet test`
Expected: All tests pass

- [ ] **Step 6: Commit**

```bash
cd D:/claude/triodos
git add -A
git commit -m "feat: convert to ASP.NET Core web app with API endpoints"
```

---

### Task 7: Create Dockerfile and docker-compose.yml

**Files:**
- Create: `Dockerfile`
- Create: `docker-compose.yml`

- [ ] **Step 1: Create Dockerfile**

Write to `D:/claude/triodos/Dockerfile`:

```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY Triodos.KruispostMonitor.slnx .
COPY src/Triodos.KruispostMonitor/Triodos.KruispostMonitor.csproj src/Triodos.KruispostMonitor/
COPY tests/Triodos.KruispostMonitor.Tests/Triodos.KruispostMonitor.Tests.csproj tests/Triodos.KruispostMonitor.Tests/
RUN dotnet restore

COPY . .
RUN dotnet test --no-restore
RUN dotnet publish src/Triodos.KruispostMonitor -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app
COPY --from=build /app .

EXPOSE 8080

ENTRYPOINT ["dotnet", "Triodos.KruispostMonitor.dll"]
```

- [ ] **Step 2: Create docker-compose.yml**

Write to `D:/claude/triodos/docker-compose.yml`:

```yaml
services:
  kruispost-monitor:
    build: .
    ports:
      - "8080:8080"
    volumes:
      - ./import:/data/import
      - ./processed:/data/processed
      - ./state:/data/state
    environment:
      - Matching__SimilarityThreshold=0.5
      - Matching__TargetBalance=300
      - Notifications__Slack__WebhookUrl=
      - Notifications__Slack__Enabled=false
      - Notifications__Email__Enabled=false
    restart: unless-stopped
```

- [ ] **Step 3: Create import/processed/state directories**

```bash
cd D:/claude/triodos
mkdir -p import processed state
```

- [ ] **Step 4: Update .gitignore**

Add the following to `D:/claude/triodos/.gitignore`:

```
import/
processed/
```

- [ ] **Step 5: Commit**

```bash
cd D:/claude/triodos
git add Dockerfile docker-compose.yml .gitignore
git commit -m "feat: add Dockerfile and docker-compose.yml"
```

---

## Post-Implementation Checklist

- [ ] All existing tests pass
- [ ] App builds without errors
- [ ] `dotnet run --project src/Triodos.KruispostMonitor` starts web server on port 8080
- [ ] Dashboard loads at http://localhost:8080
- [ ] Dropping an MT940 file in the import folder triggers processing
- [ ] Processed file is moved to processed folder
- [ ] Dashboard auto-refreshes to show new data
- [ ] Manual matching works (select, match, undo, save)
- [ ] Notifications are sent after processing
- [ ] `docker compose build` succeeds
- [ ] `docker compose up` starts the container and serves the dashboard

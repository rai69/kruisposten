# Web App Conversion — Design Spec

## Goal

Convert the Kruispost Monitor from a console app to an always-running ASP.NET Core web application with a dashboard UI, file watcher for automatic MT940 processing, and Docker deployment.

## Architecture

Single-process ASP.NET Core web app running in a Docker container:
- **Dashboard UI** at `/` — evolved from the interactive matching page, always available
- **FileWatcherService** (BackgroundService) — monitors a mounted folder for new MT940 files
- **ProcessingService** — encapsulates the parse → match → notify → save state flow
- **MonitorState** — singleton holding current state + match result, shared between dashboard and processing

State persists as JSON on a mounted volume. Notifications (Slack/email) fire immediately on processing. The dashboard always shows current unmatched transactions for manual matching.

## Hosting Model

The project SDK changes from `Microsoft.NET.Sdk` to `Microsoft.NET.Sdk.Web`. Program.cs becomes a `WebApplication` that:
- Serves the dashboard at `/`
- Hosts API endpoints (`/api/data`, `/api/match`, `/api/unmatch`, `/api/process`)
- Registers `FileWatcherService` as a hosted BackgroundService
- Registers `ProcessingService` as a singleton service

The current console flow is fully replaced. The `--interactive` flag is no longer needed — the dashboard is always interactive.

## File Watcher

`FileWatcherService` extends `BackgroundService` and uses `FileSystemWatcher` to monitor a configured folder.

When a new `*.mt940` or `*.sta` file appears:
1. Wait 2 seconds for the file to finish writing
2. Call `ProcessingService.ProcessFileAsync(filePath)`
3. Move the processed file to a `processed/` subfolder
4. Log the result

Configuration:
```json
"FileWatcher": {
  "WatchPath": "/data/import",
  "ProcessedPath": "/data/processed"
}
```

## ProcessingService

Encapsulates the full processing flow as a reusable service:

```
ProcessFileAsync(string filePath):
  1. Read and parse MT940 file
  2. Load state (including manual match IDs for exclusion)
  3. Auto-match transactions
  4. Send notifications (Slack/email) with match result
  5. Update and save state
  6. Update MonitorState singleton for dashboard
```

Also supports `ProcessAsync(ITransactionSource)` for Ponto mode in the future.

## MonitorState (singleton)

Thread-safe singleton shared between the dashboard API and ProcessingService:

```csharp
public class MonitorState
{
    public MatchResult? CurrentMatchResult { get; set; }
    public List<TransactionRecord> AllTransactions { get; set; }
    public decimal CurrentBalance { get; set; }
    public string Currency { get; set; }
    public string AccountIdentifier { get; set; }
    public RunState State { get; set; }
    public List<ProcessingRun> History { get; set; }
}

public record ProcessingRun(
    DateTimeOffset Timestamp,
    string FileName,
    int TransactionCount,
    int AutoMatched,
    int UnmatchedDebits,
    int UnmatchedCredits);
```

## Dashboard UI

The interactive matching page evolves into a permanent dashboard:

### Header
- App status: last processed file, last run time
- Watch folder indicator (watching / idle)

### Processing history
- Table of processed files with timestamp, transaction count, matched/unmatched counts

### Current unmatched transactions
- Same two-column matching UI (debits left, credits right)
- Checkboxes, running balance, "Match selected" button
- Manual matches section with undo

### Auto-matched summary
- Collapsible section showing auto-matched pairs

### Manual trigger
- "Reprocess" button to re-run the last file

Auto-refreshes via periodic polling (every 5 seconds) to pick up file watcher results.

## API Endpoints

- `GET /` — dashboard HTML page
- `GET /api/data` — current state (match result, transactions, history, balance)
- `POST /api/match` — create manual match (debit IDs + credit IDs)
- `POST /api/unmatch` — undo manual match by index
- `POST /api/process` — manually trigger reprocessing

## Docker Setup

### Dockerfile

Multi-stage build: restore → build → publish → runtime. Listens on port 8080.

### docker-compose.yml

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
```

State.json lives in `/data/state/` (volume-mounted). Secrets via environment variables. No auth needed — household tool on local network.

## File Structure

```
src/Triodos.KruispostMonitor/
├── Triodos.KruispostMonitor.csproj    # SDK → Microsoft.NET.Sdk.Web
├── Program.cs                          # WebApplication setup
├── Services/
│   ├── ProcessingService.cs            # Parse → match → notify → save
│   ├── FileWatcherService.cs           # BackgroundService watching folder
│   └── MonitorState.cs                 # Singleton shared state
├── Configuration/
│   ├── AppSettings.cs                  # Existing + FileWatcherSettings
│   └── (existing settings unchanged)
├── Interactive/
│   ├── InteractivePage.cs              # Dashboard HTML (evolved)
│   └── InteractiveServer.cs            # Removed (endpoints in Program.cs)
├── Matching/                           # Unchanged
├── Mt940/                              # Unchanged
├── Notifications/                      # Unchanged
├── Ponto/                              # Unchanged (kept for future use)
├── State/                              # Unchanged
├── TransactionSource/                  # Unchanged
└── appsettings.json                    # Add FileWatcher section

Dockerfile
docker-compose.yml
```

## What Stays Unchanged

- `TransactionMatcher`, `StringSimilarity`, `MatchResult`, `TransactionRecord`
- `Mt940Parser`, `Mt940Statement`
- `Mt940TransactionSource` (used by ProcessingService)
- `NotificationMessageBuilder`, `SlackNotificationSender`, `EmailNotificationSender`
- `INotificationSender`, `NotificationMessage`
- `StateStore`, `RunState`, `ManualMatch`
- `IPontoService`, `PontoService`, `PontoTransactionSource`
- `ITransactionSource`, `TransactionSourceResult`
- All configuration classes (PontoSettings, MatchingSettings, NotificationSettings, etc.)

## What Changes

- `Triodos.KruispostMonitor.csproj` — SDK change, remove FrameworkReference (included in Web SDK)
- `Program.cs` — complete rewrite
- `InteractivePage.cs` — evolve into dashboard
- `appsettings.json` — add FileWatcher section, update State path

## What's Removed

- `InteractiveServer.cs` — endpoints move into Program.cs
- `--interactive` flag — dashboard is always available

## What's New

- `Services/ProcessingService.cs`
- `Services/FileWatcherService.cs`
- `Services/MonitorState.cs`
- `Configuration/FileWatcherSettings.cs` (or added to AppSettings.cs)
- `Dockerfile`
- `docker-compose.yml`

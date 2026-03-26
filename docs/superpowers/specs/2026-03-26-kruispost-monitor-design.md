# Triodos Kruispost Monitor — Design Spec

## Summary

A .NET 9 console application that monitors a Triodos bank kruispost (cross-posting) current account via the Ponto Connect API. It matches expenses (debits) with chargebacks (credits) by amount and description/reference similarity, and sends Slack and email notifications when unmatched expenses are found or the balance deviates from the configured target (default: EUR 300).

## Context

A kruispost account is a pass-through account where every debit should eventually have a matching credit. The balance should always return to a fixed target amount. This tool automates the monitoring of that invariant.

## Architecture

- **Type:** .NET 9 console application
- **Scheduling:** Windows Task Scheduler for daily runs; can also be triggered manually on demand
- **Persistence:** Local JSON file (`state.json`) — no database
- **Configuration:** `appsettings.json` with environment variable overrides
- **Notifications:** Slack (incoming webhook) and email (SMTP via MailKit)

### Program Flow

1. Authenticate with Ponto Connect API (OAuth2 client credentials)
2. Fetch transactions since last run (or all history on first run)
3. Match debits to credits by amount + reference similarity
4. Identify unmatched debits
5. Check if current balance equals expected target (EUR 300)
6. Send Slack + email notifications if issues are found
7. Persist last-run state to `state.json`

## Ponto Connect Integration

### Prerequisites

1. Create a Ponto account at myponto.com
2. Link the Triodos current account (by IBAN)
3. Generate API client ID and secret in the Ponto dashboard

### API Flow

1. **Get access token** — `POST /oauth2/token` with client credentials
2. **List accounts** — `GET /accounts` to find the kruispost account (matched by configurable IBAN)
3. **Fetch transactions** — `GET /accounts/{id}/transactions` with pagination
4. Ponto syncs with the bank 4x/day automatically; the app can also trigger a manual sync via `POST /accounts/{id}/synchronizations`

### Transaction Data Extracted

- Amount (positive = credit, negative = debit)
- Execution date
- Counterparty name
- Remittance information (description/reference)

### State Tracking

- Store last successful sync timestamp in `state.json`
- On each run, fetch transactions since that timestamp
- On first run, fetch all available history

## Matching Logic

### Algorithm

1. Collect all unmatched debits and unmatched credits
2. For each debit, find credits with the same exact amount
3. Among those, score by reference similarity (counterparty name + remittance info)
4. If a credit scores above the configurable similarity threshold, mark as matched pair
5. Debits with no qualifying credit remain "unmatched"

### Edge Cases

- **Multiple credits with same amount:** Pick the one with highest reference similarity, then closest in date
- **Partial match (amount OK, reference unclear):** Flag as "possible match" with lower confidence
- **Credits without matching debit:** Flag as "unexpected credit"

### Configuration

```json
{
  "Ponto": {
    "ClientId": "...",
    "ClientSecret": "...",
    "AccountIban": "NLxxTRIO0123456789"
  },
  "Matching": {
    "SimilarityThreshold": 0.7,
    "TargetBalance": 300.00
  }
}
```

## Notifications

### When Sent

- Unmatched debits exist (no corresponding chargeback found)
- Balance deviates from the target (EUR 300)
- Both Slack and email are sent simultaneously
- Silent on success (configurable via `NotifyOnSuccess`)

### Notification Content

```
Kruispost Monitor — 2 unmatched expenses found

Balance: EUR 247.50 (expected: EUR 300.00, delta: -EUR 52.50)

Unmatched expenses:
  1. 2026-03-20  -EUR 35.00  Albert Heijn  "Boodschappen week 12"
  2. 2026-03-22  -EUR 17.50  Bol.com       "Bestelling 123456"

Possible matches (low confidence):
  1. 2026-03-22  -EUR 17.50  Bol.com  <->  2026-03-24  +EUR 17.50  "Terugbetaling"
```

### Configuration

```json
{
  "Notifications": {
    "NotifyOnSuccess": false,
    "Slack": {
      "WebhookUrl": "https://hooks.slack.com/services/...",
      "Enabled": true
    },
    "Email": {
      "SmtpHost": "smtp.example.com",
      "SmtpPort": 587,
      "UseSsl": true,
      "Username": "...",
      "Password": "...",
      "FromAddress": "monitor@example.com",
      "ToAddresses": ["you@example.com"],
      "Enabled": true
    }
  }
}
```

## Project Structure

```
Triodos.KruispostMonitor/
├── Triodos.KruispostMonitor.sln
├── src/
│   └── Triodos.KruispostMonitor/
│       ├── Triodos.KruispostMonitor.csproj
│       ├── Program.cs                      # Entry point, DI setup, orchestration
│       ├── appsettings.json                # Configuration
│       ├── Configuration/
│       │   ├── AppSettings.cs              # Strongly-typed config classes
│       │   └── MatchingSettings.cs
│       ├── Ponto/
│       │   ├── PontoClient.cs              # HTTP client for Ponto Connect API
│       │   ├── PontoAuthHandler.cs         # OAuth2 token management
│       │   └── Models/                     # API response DTOs
│       ├── Matching/
│       │   ├── TransactionMatcher.cs       # Core matching logic
│       │   └── MatchResult.cs              # Matched/unmatched result model
│       ├── Notifications/
│       │   ├── INotificationSender.cs      # Interface
│       │   ├── SlackNotificationSender.cs
│       │   └── EmailNotificationSender.cs
│       └── State/
│           ├── RunState.cs                 # Last-run timestamp, matched pairs
│           └── StateStore.cs               # Read/write state.json
└── tests/
    └── Triodos.KruispostMonitor.Tests/
        ├── TransactionMatcherTests.cs
        └── ...
```

## Dependencies (NuGet)

- `Microsoft.Extensions.Hosting` — DI, configuration, logging
- `Microsoft.Extensions.Http` — typed HTTP client for Ponto
- `System.Net.Http.Json` — JSON serialization
- `MailKit` — SMTP email sending

## Testing

- Unit tests for matching logic (core business value)
- Integration tests using Ponto sandbox environment

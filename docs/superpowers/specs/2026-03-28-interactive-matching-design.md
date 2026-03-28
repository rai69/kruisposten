# Interactive Matching UI — Design Spec

## Goal

Add a browser-based interactive mode to the Kruispost Monitor so users can manually match unmatched transactions — including many-to-many groupings (multiple debits to multiple credits). Manual matches are persisted permanently in state.json.

## Trigger

Run with `--interactive` flag:

```bash
dotnet run --project src/Triodos.KruispostMonitor -- --interactive
```

The app runs the normal flow (parse transactions, auto-match), then starts a local web server instead of sending notifications. After the user finishes manual matching, the app saves state and sends notifications with the updated match result.

## UI Layout

### Summary bar

Four cards at the top:
- Auto-matched pairs count (green)
- Unmatched debits count (red)
- Unmatched credits count (blue)
- Current balance vs target

### Auto-matched pairs section

Collapsible, read-only. Shows each auto-matched pair as a single row: debit description + amount on the left, credit description + amount on the right.

### Unmatched transactions — two-column layout

Left column: unmatched debits (checkboxes, date, amount, description).
Right column: unmatched credits (same format).

Users check transactions from both sides. Multiple selections allowed on both sides (many-to-many).

### Sticky bottom panel

Always visible at the bottom of the viewport:
- **Selected count**: "2 debits, 1 credit"
- **Net balance**: sum of selected amounts. Shows green with checkmark when exactly 0 (balanced). Shows red when not balanced.
- **"Match selected" button**: enabled only when net balance is 0. Moves selected transactions from unmatched to a "Manual matches" section.
- **"Clear" button**: deselects all.
- **"Save & finish" button**: persists manual matches, shuts down the server, app continues to notification step.

### Manual matches section

Appears between auto-matched and unmatched sections after the user creates manual matches. Shows grouped transactions with an "Undo" button per group to move them back to unmatched.

## Architecture

### InteractiveServer

A minimal Kestrel web server embedded in the app. Serves:
- `GET /` — the single-page HTML/JS/CSS app (embedded as a string or resource)
- `GET /api/data` — returns JSON with auto-matched pairs, unmatched debits, unmatched credits, manual matches, balance info
- `POST /api/match` — accepts a JSON body with debit IDs and credit IDs, validates they balance, moves them to manual matches
- `POST /api/unmatch` — accepts a manual match group index, moves transactions back to unmatched
- `POST /api/finish` — saves state and signals the server to shut down

The server binds to `http://localhost:0` (random free port) and prints the URL to the console. Attempts to open the browser automatically via `Process.Start`.

### Client-side (single HTML page)

All UI logic is client-side JavaScript:
- Fetches data from `/api/data` on load and after each match/unmatch
- Handles checkbox selection, calculates net balance in real-time
- Sends match/unmatch requests to the API
- Finish button calls `/api/finish` and shows a "saved" confirmation

No frontend build toolchain — plain HTML, CSS, and vanilla JS embedded in a single file.

### State changes

`RunState` gets a new property:

```csharp
public List<ManualMatch> ManualMatches { get; set; } = [];
```

Where:

```csharp
public record ManualMatch(List<string> DebitIds, List<string> CreditIds);
```

When loading state on future runs, manual match IDs are added to the `alreadyMatchedIds` set so they're excluded from both auto-matching and the unmatched lists.

### Program.cs flow

```
1. Load state
2. Fetch transactions (Ponto or MT940)
3. Auto-match
4. If --interactive:
   a. Start InteractiveServer with match result + transactions
   b. Wait for finish signal
   c. Update match result with manual matches
5. Build notification message (with updated match result)
6. Send notifications
7. Save state
```

## File structure

```
src/Triodos.KruispostMonitor/
  Interactive/
    InteractiveServer.cs       # Kestrel server + API endpoints
    InteractivePage.cs         # Static HTML/JS/CSS as embedded string
  State/
    RunState.cs                # Add ManualMatches property
    ManualMatch.cs             # ManualMatch record
```

## What stays unchanged

- `ITransactionSource`, `PontoTransactionSource`, `Mt940TransactionSource`
- `Mt940Parser`, `Mt940Statement`
- `TransactionMatcher`, `StringSimilarity`
- `NotificationMessageBuilder`, `INotificationSender`, `SlackNotificationSender`, `EmailNotificationSender`
- `IPontoService`, `PontoService`
- `IStateStore`, `StateStore`

## Edge cases

- **Net balance not zero**: "Match selected" button is disabled. User must adjust selection.
- **No unmatched transactions**: Interactive mode shows only the auto-matched summary with a "Nothing to match — Save & finish" message.
- **Browser not available**: URL is printed to console; user can open manually.
- **Ctrl+C during interactive mode**: Graceful shutdown, no state saved (user didn't click finish).

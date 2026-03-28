# Interactive Matching UI Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a browser-based interactive mode where users can manually group unmatched transactions into many-to-many matches, persisted permanently in state.json.

**Architecture:** When `--interactive` flag is passed, the app auto-matches as usual, then starts an embedded Kestrel web server serving a single-page app. The SPA shows unmatched transactions in two columns (debits/credits) with checkboxes. Users select items, see a running balance, and click "Match" when balanced. Manual matches are saved to state.json via a REST API. After "Save & finish", the app continues with notifications.

**Tech Stack:** .NET 9, ASP.NET Core (Kestrel), vanilla HTML/JS/CSS (no build toolchain)

---

## File Structure

```
src/Triodos.KruispostMonitor/
├── Triodos.KruispostMonitor.csproj   # Add ASP.NET Core framework reference
├── Program.cs                         # Add --interactive flag handling
├── State/
│   ├── RunState.cs                    # Add ManualMatches property
│   └── ManualMatch.cs                 # New: ManualMatch record
└── Interactive/
    ├── InteractiveServer.cs           # New: Kestrel server + REST API
    └── InteractivePage.cs             # New: HTML/JS/CSS as static string
```

---

### Task 1: Add ManualMatch record and update RunState

**Files:**
- Create: `src/Triodos.KruispostMonitor/State/ManualMatch.cs`
- Modify: `src/Triodos.KruispostMonitor/State/RunState.cs`

- [ ] **Step 1: Create ManualMatch record**

Write to `src/Triodos.KruispostMonitor/State/ManualMatch.cs`:

```csharp
namespace Triodos.KruispostMonitor.State;

public record ManualMatch(List<string> DebitIds, List<string> CreditIds);
```

- [ ] **Step 2: Add ManualMatches to RunState**

Add the following property to the `RunState` class in `src/Triodos.KruispostMonitor/State/RunState.cs`:

```csharp
public List<ManualMatch> ManualMatches { get; set; } = [];
```

The full file should be:

```csharp
using Triodos.KruispostMonitor.State;

namespace Triodos.KruispostMonitor.State;

public class RunState
{
    public DateTimeOffset? LastRunUtc { get; set; }
    public HashSet<string> MatchedTransactionIds { get; set; } = [];
    public string? RefreshToken { get; set; }
    public List<ManualMatch> ManualMatches { get; set; } = [];
}
```

- [ ] **Step 3: Verify build**

Run: `cd D:/claude/triodos && dotnet build`
Expected: Build succeeded with 0 errors

- [ ] **Step 4: Commit**

```bash
cd D:/claude/triodos
git add src/Triodos.KruispostMonitor/State/ManualMatch.cs src/Triodos.KruispostMonitor/State/RunState.cs
git commit -m "feat: add ManualMatch record and ManualMatches to RunState"
```

---

### Task 2: Add ASP.NET Core framework reference

**Files:**
- Modify: `src/Triodos.KruispostMonitor/Triodos.KruispostMonitor.csproj`

- [ ] **Step 1: Add framework reference**

Add the following `<ItemGroup>` to the csproj, after the existing `<PackageReference>` item group:

```xml
<ItemGroup>
  <FrameworkReference Include="Microsoft.AspNetCore.App" />
</ItemGroup>
```

- [ ] **Step 2: Verify build**

Run: `cd D:/claude/triodos && dotnet build`
Expected: Build succeeded with 0 errors

- [ ] **Step 3: Commit**

```bash
cd D:/claude/triodos
git add src/Triodos.KruispostMonitor/Triodos.KruispostMonitor.csproj
git commit -m "feat: add ASP.NET Core framework reference for Kestrel"
```

---

### Task 3: Create InteractivePage (HTML/JS/CSS)

**Files:**
- Create: `src/Triodos.KruispostMonitor/Interactive/InteractivePage.cs`

This is the largest task. The HTML page is a complete single-page app embedded as a C# static string.

- [ ] **Step 1: Create InteractivePage.cs**

Write to `src/Triodos.KruispostMonitor/Interactive/InteractivePage.cs`:

```csharp
namespace Triodos.KruispostMonitor.Interactive;

public static class InteractivePage
{
    public static string GetHtml() => """
<!DOCTYPE html>
<html>
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>Kruispost Monitor — Interactive Matching</title>
<style>
  * { box-sizing: border-box; margin: 0; padding: 0; }
  body { font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif; background: #f5f5f5; color: #333; padding: 20px; padding-bottom: 100px; }
  h1 { font-size: 22px; margin-bottom: 4px; }
  .subtitle { color: #666; margin-bottom: 20px; font-size: 14px; }
  .section { background: white; border-radius: 8px; padding: 16px 20px; margin-bottom: 16px; box-shadow: 0 1px 3px rgba(0,0,0,0.1); }
  .section h2 { font-size: 16px; margin-bottom: 12px; display: flex; align-items: center; gap: 8px; }
  .badge { background: #e0e0e0; border-radius: 12px; padding: 2px 10px; font-size: 13px; font-weight: normal; }
  .badge.green { background: #c8e6c9; color: #2e7d32; }
  .badge.red { background: #ffcdd2; color: #c62828; }
  .badge.blue { background: #bbdefb; color: #1565c0; }
  .badge.purple { background: #e1bee7; color: #6a1b9a; }
  .summary-row { display: flex; gap: 16px; margin-bottom: 16px; flex-wrap: wrap; }
  .summary-card { flex: 1; min-width: 140px; background: white; border-radius: 8px; padding: 14px 18px; box-shadow: 0 1px 3px rgba(0,0,0,0.1); text-align: center; }
  .summary-card .value { font-size: 24px; font-weight: bold; }
  .summary-card .label { font-size: 12px; color: #666; margin-top: 4px; }
  .summary-card.green .value { color: #2e7d32; }
  .summary-card.red .value { color: #c62828; }
  .summary-card.blue .value { color: #1565c0; }
  table { width: 100%; border-collapse: collapse; font-size: 13px; }
  th { text-align: left; padding: 6px 10px; border-bottom: 2px solid #e0e0e0; font-size: 12px; color: #666; text-transform: uppercase; }
  td { padding: 8px 10px; border-bottom: 1px solid #f0f0f0; }
  tr.selected { background: #e3f2fd; }
  .amount { font-family: 'SF Mono', Consolas, monospace; text-align: right; white-space: nowrap; }
  .amount.debit { color: #c62828; }
  .amount.credit { color: #2e7d32; }
  .check { width: 18px; height: 18px; border: 2px solid #ccc; border-radius: 4px; cursor: pointer; display: inline-flex; align-items: center; justify-content: center; user-select: none; }
  .check.checked { background: #1976d2; border-color: #1976d2; color: white; font-size: 12px; }
  .two-col { display: grid; grid-template-columns: 1fr 1fr; gap: 16px; }
  @media (max-width: 800px) { .two-col { grid-template-columns: 1fr; } }
  .match-panel { position: fixed; bottom: 0; left: 0; right: 0; background: white; border-top: 2px solid #1976d2; padding: 12px 20px; display: flex; align-items: center; gap: 16px; box-shadow: 0 -2px 8px rgba(0,0,0,0.1); z-index: 100; }
  .match-panel .sel-info { flex: 1; }
  .match-panel .sel-label { font-size: 12px; color: #666; }
  .match-panel .sel-detail { font-size: 14px; font-weight: 500; }
  .match-panel .balance { text-align: center; padding: 0 16px; }
  .match-panel .bal-label { font-size: 12px; color: #666; }
  .match-panel .bal-value { font-size: 20px; font-weight: bold; }
  .match-panel .bal-value.balanced { color: #2e7d32; }
  .match-panel .bal-value.unbalanced { color: #c62828; }
  .btn { padding: 8px 20px; border: none; border-radius: 6px; font-size: 14px; font-weight: 600; cursor: pointer; }
  .btn-primary { background: #1976d2; color: white; }
  .btn-primary:hover { background: #1565c0; }
  .btn-primary:disabled { background: #bbb; cursor: not-allowed; }
  .btn-secondary { background: #e0e0e0; color: #333; }
  .btn-success { background: #2e7d32; color: white; }
  .btn-success:hover { background: #1b5e20; }
  .btn-danger { background: #e0e0e0; color: #c62828; font-size: 12px; padding: 4px 12px; }
  .btn-danger:hover { background: #ffcdd2; }
  .auto-row { display: flex; align-items: center; padding: 5px 0; border-bottom: 1px solid #f0f0f0; font-size: 13px; }
  .auto-row .side { flex: 1; }
  .auto-row .arrow { color: #999; margin: 0 8px; font-size: 16px; }
  .toggle { cursor: pointer; color: #1976d2; font-size: 13px; margin-top: 8px; }
  .manual-group { border: 1px solid #e1bee7; border-radius: 6px; padding: 10px 14px; margin-bottom: 8px; }
  .manual-group .group-header { display: flex; justify-content: space-between; align-items: center; margin-bottom: 6px; }
  .manual-group .group-header span { font-size: 13px; font-weight: 600; color: #6a1b9a; }
  .manual-group .group-row { font-size: 13px; padding: 2px 0; }
  .finished-overlay { position: fixed; inset: 0; background: rgba(0,0,0,0.5); display: flex; align-items: center; justify-content: center; z-index: 200; }
  .finished-box { background: white; border-radius: 12px; padding: 40px; text-align: center; box-shadow: 0 4px 24px rgba(0,0,0,0.2); }
  .finished-box h2 { color: #2e7d32; margin-bottom: 8px; }
  .hidden { display: none; }
</style>
</head>
<body>

<h1>Kruispost Monitor — Interactive Matching</h1>
<p class="subtitle" id="subtitle"></p>

<div class="summary-row" id="summary"></div>

<div id="auto-section" class="section hidden">
  <h2>Auto-matched pairs <span class="badge green" id="auto-count"></span></h2>
  <div id="auto-list"></div>
  <div class="toggle" id="auto-toggle" onclick="toggleAuto()"></div>
</div>

<div id="manual-section" class="section hidden">
  <h2>Manual matches <span class="badge purple" id="manual-count"></span></h2>
  <div id="manual-list"></div>
</div>

<div class="two-col" id="unmatched-area">
  <div class="section">
    <h2>Unmatched debits <span class="badge red" id="debit-count"></span></h2>
    <table><thead><tr><th style="width:30px"></th><th>Date</th><th>Amount</th><th>Description</th></tr></thead>
    <tbody id="debit-list"></tbody></table>
  </div>
  <div class="section">
    <h2>Unmatched credits <span class="badge blue" id="credit-count"></span></h2>
    <table><thead><tr><th style="width:30px"></th><th>Date</th><th>Amount</th><th>Description</th></tr></thead>
    <tbody id="credit-list"></tbody></table>
  </div>
</div>

<div class="match-panel" id="panel">
  <div class="sel-info">
    <div class="sel-label">Selected</div>
    <div class="sel-detail" id="sel-detail">None</div>
  </div>
  <div class="balance">
    <div class="bal-label">Net balance</div>
    <div class="bal-value" id="bal-value">—</div>
  </div>
  <button class="btn btn-primary" id="btn-match" disabled onclick="doMatch()">Match selected</button>
  <button class="btn btn-secondary" onclick="clearSelection()">Clear</button>
  <div style="flex:1"></div>
  <button class="btn btn-success" onclick="doFinish()">Save &amp; finish</button>
</div>

<div class="finished-overlay hidden" id="finished">
  <div class="finished-box">
    <h2>Saved!</h2>
    <p>Manual matches saved. You can close this window.</p>
  </div>
</div>

<script>
let data = null;
let selectedDebits = new Set();
let selectedCredits = new Set();
let autoExpanded = false;

async function loadData() {
  const resp = await fetch('/api/data');
  data = await resp.json();
  render();
}

function render() {
  document.getElementById('subtitle').textContent =
    `${data.accountIdentifier} \u2022 ${data.transactionCount} transactions \u2022 Balance: ${data.currency} ${data.currentBalance.toFixed(2)}`;

  // Summary
  document.getElementById('summary').innerHTML = `
    <div class="summary-card green"><div class="value">${data.autoMatched.length}</div><div class="label">Auto-matched</div></div>
    <div class="summary-card ${data.manualMatches.length > 0 ? 'purple' : ''}"><div class="value">${data.manualMatches.length}</div><div class="label">Manual matches</div></div>
    <div class="summary-card red"><div class="value">${data.unmatchedDebits.length}</div><div class="label">Unmatched debits</div></div>
    <div class="summary-card blue"><div class="value">${data.unmatchedCredits.length}</div><div class="label">Unmatched credits</div></div>
  `;

  // Auto-matched
  const autoSec = document.getElementById('auto-section');
  if (data.autoMatched.length > 0) {
    autoSec.classList.remove('hidden');
    document.getElementById('auto-count').textContent = data.autoMatched.length;
    const show = autoExpanded ? data.autoMatched : data.autoMatched.slice(0, 3);
    document.getElementById('auto-list').innerHTML = show.map(m => `
      <div class="auto-row">
        <div class="side"><span class="amount debit">${fmt(-m.debit.absoluteAmount)}</span> ${esc(m.debit.counterpartName)}</div>
        <span class="arrow">\u27F7</span>
        <div class="side"><span class="amount credit">${fmt(m.credit.absoluteAmount)}</span> ${esc(m.credit.counterpartName)}</div>
      </div>`).join('');
    const toggle = document.getElementById('auto-toggle');
    if (data.autoMatched.length > 3) {
      toggle.classList.remove('hidden');
      toggle.textContent = autoExpanded ? '\u25BE Hide' : `\u25B8 Show ${data.autoMatched.length - 3} more...`;
    } else { toggle.classList.add('hidden'); }
  } else { autoSec.classList.add('hidden'); }

  // Manual matches
  const manSec = document.getElementById('manual-section');
  if (data.manualMatches.length > 0) {
    manSec.classList.remove('hidden');
    document.getElementById('manual-count').textContent = data.manualMatches.length;
    document.getElementById('manual-list').innerHTML = data.manualMatches.map((g, i) => {
      const debits = g.debits.map(t => `<div class="group-row"><span class="amount debit">${fmt(-t.absoluteAmount)}</span> ${esc(t.counterpartName)}</div>`).join('');
      const credits = g.credits.map(t => `<div class="group-row"><span class="amount credit">${fmt(t.absoluteAmount)}</span> ${esc(t.counterpartName)}</div>`).join('');
      return `<div class="manual-group"><div class="group-header"><span>Group ${i+1}</span><button class="btn btn-danger" onclick="doUnmatch(${i})">Undo</button></div>${debits}${credits}</div>`;
    }).join('');
  } else { manSec.classList.add('hidden'); }

  // Unmatched
  document.getElementById('debit-count').textContent = data.unmatchedDebits.length;
  document.getElementById('debit-list').innerHTML = data.unmatchedDebits.map(t => txRow(t, 'debit')).join('');
  document.getElementById('credit-count').textContent = data.unmatchedCredits.length;
  document.getElementById('credit-list').innerHTML = data.unmatchedCredits.map(t => txRow(t, 'credit')).join('');

  updatePanel();
}

function txRow(t, type) {
  const sel = type === 'debit' ? selectedDebits : selectedCredits;
  const checked = sel.has(t.id);
  const cls = type === 'debit' ? 'debit' : 'credit';
  const sign = type === 'debit' ? '-' : '+';
  return `<tr class="${checked ? 'selected' : ''}" onclick="toggleTx('${t.id}','${type}')">
    <td><div class="check ${checked ? 'checked' : ''}">${checked ? '\u2713' : ''}</div></td>
    <td>${t.executionDate.substring(5,10)}</td>
    <td class="amount ${cls}">${sign}${t.absoluteAmount.toFixed(2)}</td>
    <td>${esc(t.counterpartName)}</td></tr>`;
}

function toggleTx(id, type) {
  const sel = type === 'debit' ? selectedDebits : selectedCredits;
  if (sel.has(id)) sel.delete(id); else sel.add(id);
  render();
}

function updatePanel() {
  const nd = selectedDebits.size, nc = selectedCredits.size;
  document.getElementById('sel-detail').textContent = nd + nc === 0 ? 'None' : `${nd} debit${nd!==1?'s':''}, ${nc} credit${nc!==1?'s':''}`;

  let net = 0;
  for (const t of data.unmatchedDebits) if (selectedDebits.has(t.id)) net += t.amount;
  for (const t of data.unmatchedCredits) if (selectedCredits.has(t.id)) net += t.amount;

  const balEl = document.getElementById('bal-value');
  const btnMatch = document.getElementById('btn-match');
  if (nd + nc === 0) {
    balEl.textContent = '\u2014';
    balEl.className = 'bal-value';
    btnMatch.disabled = true;
  } else {
    const balanced = Math.abs(net) < 0.005;
    balEl.textContent = balanced ? 'EUR 0.00 \u2713' : `EUR ${net.toFixed(2)}`;
    balEl.className = 'bal-value ' + (balanced ? 'balanced' : 'unbalanced');
    btnMatch.disabled = !balanced || nd === 0 || nc === 0;
  }
}

function clearSelection() { selectedDebits.clear(); selectedCredits.clear(); render(); }
function toggleAuto() { autoExpanded = !autoExpanded; render(); }

async function doMatch() {
  const body = { debitIds: [...selectedDebits], creditIds: [...selectedCredits] };
  const resp = await fetch('/api/match', { method: 'POST', headers: {'Content-Type':'application/json'}, body: JSON.stringify(body) });
  if (resp.ok) { selectedDebits.clear(); selectedCredits.clear(); await loadData(); }
  else { alert('Match failed: ' + await resp.text()); }
}

async function doUnmatch(index) {
  const resp = await fetch('/api/unmatch', { method: 'POST', headers: {'Content-Type':'application/json'}, body: JSON.stringify({ index }) });
  if (resp.ok) { await loadData(); }
  else { alert('Unmatch failed: ' + await resp.text()); }
}

async function doFinish() {
  const resp = await fetch('/api/finish', { method: 'POST' });
  if (resp.ok) { document.getElementById('finished').classList.remove('hidden'); }
  else { alert('Save failed: ' + await resp.text()); }
}

function fmt(n) { return n.toFixed(2); }
function esc(s) { const d = document.createElement('div'); d.textContent = s; return d.innerHTML; }

loadData();
</script>
</body>
</html>
""";
}
```

- [ ] **Step 2: Verify build**

Run: `cd D:/claude/triodos && dotnet build`
Expected: Build succeeded with 0 errors

- [ ] **Step 3: Commit**

```bash
cd D:/claude/triodos
git add src/Triodos.KruispostMonitor/Interactive/InteractivePage.cs
git commit -m "feat: add interactive matching HTML page"
```

---

### Task 4: Create InteractiveServer

**Files:**
- Create: `src/Triodos.KruispostMonitor/Interactive/InteractiveServer.cs`

- [ ] **Step 1: Implement InteractiveServer**

Write to `src/Triodos.KruispostMonitor/Interactive/InteractiveServer.cs`:

```csharp
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Triodos.KruispostMonitor.Matching;
using Triodos.KruispostMonitor.State;

namespace Triodos.KruispostMonitor.Interactive;

public class InteractiveServer
{
    private readonly MatchResult _matchResult;
    private readonly List<TransactionRecord> _allTransactions;
    private readonly decimal _currentBalance;
    private readonly string _currency;
    private readonly string _accountIdentifier;
    private readonly RunState _state;
    private readonly ILogger _logger;
    private readonly TaskCompletionSource _finished = new();

    private List<ManualMatch> _manualMatches = [];
    private List<TransactionRecord> _unmatchedDebits;
    private List<TransactionRecord> _unmatchedCredits;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public InteractiveServer(
        MatchResult matchResult,
        List<TransactionRecord> allTransactions,
        decimal currentBalance,
        string currency,
        string accountIdentifier,
        RunState state,
        ILogger logger)
    {
        _matchResult = matchResult;
        _allTransactions = allTransactions;
        _currentBalance = currentBalance;
        _currency = currency;
        _accountIdentifier = accountIdentifier;
        _state = state;
        _logger = logger;

        _unmatchedDebits = new List<TransactionRecord>(matchResult.UnmatchedDebits);
        _unmatchedCredits = new List<TransactionRecord>(matchResult.UnmatchedCredits);
    }

    public async Task RunAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://localhost:0");
        builder.Logging.ClearProviders();

        var app = builder.Build();

        app.MapGet("/", () => Results.Content(InteractivePage.GetHtml(), "text/html"));
        app.MapGet("/api/data", () => Results.Json(GetData(), JsonOptions));
        app.MapPost("/api/match", async (HttpContext ctx) => await HandleMatch(ctx));
        app.MapPost("/api/unmatch", async (HttpContext ctx) => await HandleUnmatch(ctx));
        app.MapPost("/api/finish", (HttpContext ctx) => HandleFinish(ctx));

        await app.StartAsync();

        var address = app.Urls.First();
        _logger.LogInformation("Interactive matching UI available at {Url}", address);

        // Try to open browser
        try { Process.Start(new ProcessStartInfo(address) { UseShellExecute = true }); }
        catch { _logger.LogInformation("Could not open browser automatically. Please open {Url} manually.", address); }

        // Wait for user to click "Save & finish"
        await _finished.Task;

        await app.StopAsync();
    }

    public List<ManualMatch> GetManualMatches() => _manualMatches;

    private object GetData() => new
    {
        AccountIdentifier = _accountIdentifier,
        Currency = _currency,
        CurrentBalance = _currentBalance,
        TransactionCount = _allTransactions.Count,
        AutoMatched = _matchResult.Matched.Select(m => new
        {
            Debit = TxDto(m.Debit),
            Credit = TxDto(m.Credit)
        }),
        ManualMatches = _manualMatches.Select((mm, i) =>
        {
            var debits = mm.DebitIds.Select(id => _allTransactions.First(t => t.Id == id)).ToList();
            var credits = mm.CreditIds.Select(id => _allTransactions.First(t => t.Id == id)).ToList();
            return new { Debits = debits.Select(TxDto), Credits = credits.Select(TxDto) };
        }),
        UnmatchedDebits = _unmatchedDebits.Select(TxDto),
        UnmatchedCredits = _unmatchedCredits.Select(TxDto)
    };

    private static object TxDto(TransactionRecord t) => new
    {
        t.Id,
        t.Amount,
        t.AbsoluteAmount,
        t.CounterpartName,
        t.RemittanceInformation,
        ExecutionDate = t.ExecutionDate.ToString("yyyy-MM-dd")
    };

    private async Task<IResult> HandleMatch(HttpContext ctx)
    {
        var body = await JsonSerializer.DeserializeAsync<MatchRequest>(ctx.Request.Body, JsonOptions);
        if (body is null || body.DebitIds.Count == 0 || body.CreditIds.Count == 0)
            return Results.BadRequest("Must select at least one debit and one credit");

        var debits = _unmatchedDebits.Where(t => body.DebitIds.Contains(t.Id)).ToList();
        var credits = _unmatchedCredits.Where(t => body.CreditIds.Contains(t.Id)).ToList();

        var net = debits.Sum(t => t.Amount) + credits.Sum(t => t.Amount);
        if (Math.Abs(net) >= 0.005m)
            return Results.BadRequest($"Selection does not balance: {net:F2}");

        _manualMatches.Add(new ManualMatch(body.DebitIds, body.CreditIds));
        _unmatchedDebits.RemoveAll(t => body.DebitIds.Contains(t.Id));
        _unmatchedCredits.RemoveAll(t => body.CreditIds.Contains(t.Id));

        _logger.LogInformation("Manual match created: {Debits} debits, {Credits} credits",
            body.DebitIds.Count, body.CreditIds.Count);

        return Results.Ok();
    }

    private async Task<IResult> HandleUnmatch(HttpContext ctx)
    {
        var body = await JsonSerializer.DeserializeAsync<UnmatchRequest>(ctx.Request.Body, JsonOptions);
        if (body is null || body.Index < 0 || body.Index >= _manualMatches.Count)
            return Results.BadRequest("Invalid match index");

        var mm = _manualMatches[body.Index];
        var debits = mm.DebitIds.Select(id => _allTransactions.First(t => t.Id == id));
        var credits = mm.CreditIds.Select(id => _allTransactions.First(t => t.Id == id));

        _unmatchedDebits.AddRange(debits);
        _unmatchedCredits.AddRange(credits);
        _manualMatches.RemoveAt(body.Index);

        _logger.LogInformation("Manual match undone at index {Index}", body.Index);
        return Results.Ok();
    }

    private IResult HandleFinish(HttpContext ctx)
    {
        // Persist manual matches to state
        _state.ManualMatches.AddRange(_manualMatches);
        foreach (var mm in _manualMatches)
        {
            foreach (var id in mm.DebitIds) _state.MatchedTransactionIds.Add(id);
            foreach (var id in mm.CreditIds) _state.MatchedTransactionIds.Add(id);
        }

        _logger.LogInformation("Saved {Count} manual matches", _manualMatches.Count);
        _finished.TrySetResult();
        return Results.Ok();
    }

    private record MatchRequest(List<string> DebitIds, List<string> CreditIds);
    private record UnmatchRequest(int Index);
}
```

- [ ] **Step 2: Verify build**

Run: `cd D:/claude/triodos && dotnet build`
Expected: Build succeeded with 0 errors

- [ ] **Step 3: Commit**

```bash
cd D:/claude/triodos
git add src/Triodos.KruispostMonitor/Interactive/InteractiveServer.cs
git commit -m "feat: add InteractiveServer with REST API for manual matching"
```

---

### Task 5: Update Program.cs for --interactive flag

**Files:**
- Modify: `src/Triodos.KruispostMonitor/Program.cs`

- [ ] **Step 1: Add interactive mode to Program.cs**

Modify `src/Triodos.KruispostMonitor/Program.cs`. Add the following using at the top:

```csharp
using Triodos.KruispostMonitor.Interactive;
```

After the auto-match logging (after line 78 `logger.LogInformation("Matched: ...`), add the interactive mode block. Replace everything from the comment `// Build and send notifications` (line 80) through the state save section with:

```csharp
    // Interactive mode
    var isInteractive = args.Contains("--interactive");
    if (isInteractive)
    {
        var server = new InteractiveServer(
            matchResult,
            sourceResult.Transactions,
            sourceResult.CurrentBalance,
            sourceResult.Currency,
            sourceResult.AccountIdentifier,
            state,
            logger);

        await server.RunAsync();

        // Update match result with manual matches removed from unmatched
        var manualMatchedIds = server.GetManualMatches()
            .SelectMany(m => m.DebitIds.Concat(m.CreditIds))
            .ToHashSet();

        matchResult = new MatchResult
        {
            Matched = matchResult.Matched,
            UnmatchedDebits = matchResult.UnmatchedDebits.Where(t => !manualMatchedIds.Contains(t.Id)).ToList(),
            UnmatchedCredits = matchResult.UnmatchedCredits.Where(t => !manualMatchedIds.Contains(t.Id)).ToList(),
            PossibleMatches = matchResult.PossibleMatches
        };

        logger.LogInformation("After manual matching: {Unmatched} unmatched debits remaining",
            matchResult.UnmatchedDebits.Count);
    }

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
            var successMsg = NotificationMessageBuilder.BuildSuccess(sourceResult.CurrentBalance, sourceResult.Currency);
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
```

Also need to include manual match IDs in the already-matched set when auto-matching. Before the auto-match call (line 74-75), add:

```csharp
    // Include previously saved manual match IDs in exclusion set
    var excludedIds = new HashSet<string>(state.MatchedTransactionIds);
    foreach (var mm in state.ManualMatches)
    {
        foreach (var id in mm.DebitIds) excludedIds.Add(id);
        foreach (var id in mm.CreditIds) excludedIds.Add(id);
    }
```

And change the matcher call from:
```csharp
    var matchResult = matcher.Match(sourceResult.Transactions, state.MatchedTransactionIds);
```
to:
```csharp
    var matchResult = matcher.Match(sourceResult.Transactions, excludedIds);
```

- [ ] **Step 2: Verify build**

Run: `cd D:/claude/triodos && dotnet build`
Expected: Build succeeded with 0 errors

- [ ] **Step 3: Run all tests**

Run: `cd D:/claude/triodos && dotnet test`
Expected: All tests pass

- [ ] **Step 4: Test interactive mode manually**

Run: `cd D:/claude/triodos && dotnet run --project src/Triodos.KruispostMonitor -- --interactive`
Expected: Browser opens with the interactive matching UI showing unmatched transactions

- [ ] **Step 5: Commit**

```bash
cd D:/claude/triodos
git add src/Triodos.KruispostMonitor/Program.cs
git commit -m "feat: add --interactive flag for browser-based manual matching"
```

---

## Post-Implementation Checklist

- [ ] All existing tests pass
- [ ] App builds without errors
- [ ] `dotnet run -- --interactive` opens browser with matching UI
- [ ] Selecting debits + credits shows running balance
- [ ] "Match selected" works when balance is 0
- [ ] "Undo" moves manual match back to unmatched
- [ ] "Save & finish" persists matches and continues to notifications
- [ ] Next run excludes manually matched transaction IDs

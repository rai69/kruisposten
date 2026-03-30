namespace Triodos.KruispostMonitor.Interactive;

public static class InteractivePage
{
    public static string GetHtml() => """
<!DOCTYPE html>
<html>
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>Kruispost Monitor — Dashboard</title>
<style>
  * { box-sizing: border-box; margin: 0; padding: 0; }
  body { font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif; background: #f5f5f5; color: #333; padding: 20px; padding-bottom: 100px; }
  h1 { font-size: 22px; margin-bottom: 4px; }
  .subtitle { color: #666; margin-bottom: 8px; font-size: 14px; }
  .status-bar { display: flex; align-items: center; gap: 16px; margin-bottom: 20px; font-size: 13px; color: #555; flex-wrap: wrap; }
  .status-bar .file-info { color: #555; }
  .watch-indicator { display: flex; align-items: center; gap: 6px; }
  .watch-dot { width: 10px; height: 10px; border-radius: 50%; background: #ccc; }
  .watch-dot.active { background: #4caf50; box-shadow: 0 0 4px #4caf50; }
  .watch-label { font-size: 13px; }
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
  .btn-warning { background: #f57c00; color: white; }
  .btn-warning:hover { background: #e65100; }
  .btn-danger { background: #e0e0e0; color: #c62828; font-size: 12px; padding: 4px 12px; }
  .btn-danger:hover { background: #ffcdd2; }
  .btn-exclude { background: none; border: 1px solid #ccc; color: #999; border-radius: 4px; cursor: pointer; font-size: 11px; padding: 2px 6px; }
  .btn-exclude:hover { background: #ffcdd2; color: #c62828; border-color: #c62828; }
  .auto-row { display: flex; align-items: center; padding: 5px 0; border-bottom: 1px solid #f0f0f0; font-size: 13px; }
  .auto-row .side { flex: 1; }
  .auto-row .arrow { color: #999; margin: 0 8px; font-size: 16px; }
  .toggle { cursor: pointer; color: #1976d2; font-size: 13px; margin-top: 8px; }
  .manual-group { border: 1px solid #e1bee7; border-radius: 6px; padding: 10px 14px; margin-bottom: 8px; }
  .manual-group .group-header { display: flex; justify-content: space-between; align-items: center; margin-bottom: 6px; }
  .manual-group .group-header span { font-size: 13px; font-weight: 600; color: #6a1b9a; }
  .manual-group .group-row { font-size: 13px; padding: 2px 0; }
  .toast { position: fixed; top: 20px; right: 20px; background: #323232; color: white; padding: 10px 20px; border-radius: 6px; font-size: 14px; z-index: 300; opacity: 1; transition: opacity 0.5s; }
  .toast.fade { opacity: 0; }
  .waiting-msg { text-align: center; padding: 40px 20px; color: #666; }
  .waiting-msg p { font-size: 16px; margin-bottom: 8px; }
  .waiting-msg .sub { font-size: 13px; color: #999; }
  .history-table td { font-size: 12px; }
  .collapsible-toggle { cursor: pointer; color: #1976d2; font-size: 13px; margin-top: 8px; display: inline-block; }
  .hidden { display: none !important; }
  .score-badge { font-size: 11px; padding: 2px 6px; border-radius: 8px; font-weight: 600; white-space: nowrap; margin: 0 4px; }
  .score-badge.score-high { background: #c8e6c9; color: #2e7d32; }
  .score-badge.score-med { background: #fff3e0; color: #e65100; }
  .score-badge.score-low { background: #ffcdd2; color: #c62828; }
  .auto-row.score-low { background: #fff8f8; }
  .auto-row.score-med { background: #fffcf5; }
  .db-actions { display: flex; gap: 10px; align-items: center; }
  .tx-table { max-height: 500px; overflow-y: auto; }
  .tx-table table { font-size: 12px; }
  .tx-filter { padding: 6px 10px; border: 1px solid #ddd; border-radius: 4px; font-size: 13px; width: 300px; margin-bottom: 10px; }
  .modal-overlay { position: fixed; inset: 0; background: rgba(0,0,0,0.5); z-index: 200; display: flex; align-items: center; justify-content: center; }
  .modal { background: white; border-radius: 8px; padding: 20px; max-width: 600px; width: 90%; text-align: center; }
  .modal h3 { margin-bottom: 12px; }
  .modal p { margin-bottom: 16px; color: #666; font-size: 14px; }
  .modal .btn { margin: 0 6px; }
</style>
</head>
<body>

<h1>Kruispost Monitor — Dashboard</h1>
<p class="subtitle" id="subtitle"></p>
<div class="status-bar" id="status-bar">
  <span class="file-info" id="file-info"></span>
  <div class="watch-indicator">
    <div class="watch-dot" id="watch-dot"></div>
    <span class="watch-label" id="watch-label">Not watching</span>
  </div>
</div>

<div id="waiting-area" class="section hidden">
  <div class="waiting-msg">
    <p>Waiting for MT940 file...</p>
    <p class="sub">Drop a file in the import folder.</p>
    <p style="margin-top:20px"><button class="btn btn-danger" style="font-size:13px;padding:6px 14px" onclick="confirmReinitDb()">Re-init database</button></p>
  </div>
</div>

<div id="main-content" class="hidden">

<div class="summary-row" id="summary"></div>

<div id="history-section" class="section hidden">
  <h2>Processing history <span class="badge" id="history-count"></span></h2>
  <table class="history-table">
    <thead><tr><th>Time</th><th>File</th><th>Transactions</th><th>Matched</th><th>Unmatched</th></tr></thead>
    <tbody id="history-list"></tbody>
  </table>
  <div class="collapsible-toggle hidden" id="history-toggle" onclick="toggleHistory()"></div>
</div>

<div id="auto-section" class="section hidden">
  <h2>Auto-matched pairs <span class="badge green" id="auto-count"></span></h2>
  <div id="auto-list"></div>
  <div class="toggle" id="auto-toggle" onclick="toggleAuto()"></div>
</div>

<div id="split-section" class="section hidden">
  <h2>Split matches <span class="badge green" id="split-count"></span></h2>
  <div id="split-list"></div>
</div>

<div id="manual-section" class="section hidden">
  <h2>Manual matches <span class="badge purple" id="manual-count"></span></h2>
  <div id="manual-list"></div>
</div>

<div class="two-col" id="unmatched-area">
  <div class="section">
    <h2>Unmatched debits <span class="badge red" id="debit-count"></span> <span class="amount debit" style="font-size:13px;font-weight:normal;margin-left:8px">-<span id="debit-total">0.00</span></span></h2>
    <input type="text" class="tx-filter" id="debit-filter" placeholder="Filter debits..." oninput="render()">
    <table><thead><tr><th style="width:30px"></th><th>Date</th><th>Amount</th><th>Description</th><th style="width:40px"></th></tr></thead>
    <tbody id="debit-list"></tbody></table>
  </div>
  <div class="section">
    <h2>Unmatched credits <span class="badge blue" id="credit-count"></span> <span class="amount credit" style="font-size:13px;font-weight:normal;margin-left:8px">+<span id="credit-total">0.00</span></span></h2>
    <input type="text" class="tx-filter" id="credit-filter" placeholder="Filter credits..." oninput="render()">
    <table><thead><tr><th style="width:30px"></th><th>Date</th><th>Amount</th><th>Description</th><th style="width:40px"></th></tr></thead>
    <tbody id="credit-list"></tbody></table>
  </div>
</div>

<div id="matched-history-section" class="section">
  <h2>Matched history <span class="badge green" id="matched-history-count"></span>
    <div style="flex:1"></div>
    <button class="btn btn-secondary" onclick="loadMatchedHistory()">Load matched pairs</button>
  </h2>
  <div id="matched-history-content" class="hidden">
    <input type="text" class="tx-filter" id="matched-history-filter" placeholder="Filter matched pairs..." oninput="filterMatchedHistory()">
    <div id="matched-history-list"></div>
  </div>
</div>

<div id="saved-manual-section" class="section">
  <h2>Saved manual matches <span class="badge purple" id="saved-manual-count"></span>
    <div style="flex:1"></div>
    <button class="btn btn-secondary" onclick="loadSavedManualMatches()">Load manual matches</button>
  </h2>
  <div id="saved-manual-content" class="hidden">
    <div id="saved-manual-list"></div>
  </div>
</div>

<div id="excluded-section" class="section">
  <h2>Excluded transactions <span class="badge" id="excluded-count"></span>
    <div style="flex:1"></div>
    <button class="btn btn-secondary" onclick="loadExcluded()">Load excluded</button>
  </h2>
  <div id="excluded-content" class="hidden">
    <div class="tx-table">
      <table>
        <thead><tr><th>Date</th><th>Amount</th><th>Description</th><th style="width:40px"></th></tr></thead>
        <tbody id="excluded-list"></tbody>
      </table>
    </div>
  </div>
</div>

<div id="all-tx-section" class="section">
  <h2>All transactions <span class="badge" id="all-tx-count"></span>
    <div style="flex:1"></div>
    <div class="db-actions">
      <button class="btn btn-secondary" onclick="loadAllTransactions()">Load transactions</button>
      <button class="btn btn-danger" style="font-size:13px;padding:6px 14px" onclick="confirmReinitDb()">Re-init database</button>
    </div>
  </h2>
  <div id="all-tx-content" class="hidden">
    <input type="text" class="tx-filter" id="tx-filter" placeholder="Filter transactions..." oninput="filterTransactions()">
    <div class="tx-table">
      <table>
        <thead><tr><th>Date</th><th>Amount</th><th>Type</th><th>Counterpart</th><th>Remittance info</th><th style="width:40px"></th></tr></thead>
        <tbody id="all-tx-list"></tbody>
      </table>
    </div>
  </div>
</div>

</div><!-- #main-content -->

<div id="reinit-modal" class="modal-overlay hidden" onclick="if(event.target===this)closeReinitModal()">
  <div class="modal">
    <h3>Re-initialize database?</h3>
    <p>This will delete all transactions, matches, history and state. This cannot be undone.</p>
    <button class="btn btn-danger" style="font-size:14px;padding:8px 20px;background:#c62828;color:white" onclick="doReinitDb()">Yes, reset everything</button>
    <button class="btn btn-secondary" onclick="closeReinitModal()">Cancel</button>
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
  <button class="btn btn-warning" onclick="doReprocess()">Reprocess</button>
  <button class="btn btn-success" onclick="doSave()">Save matches</button>
</div>

<script>
let data = null;
let selectedDebits = new Set();
let selectedCredits = new Set();
let autoExpanded = false;
let historyExpanded = false;
let isLoading = false;

async function loadData() {
  if (isLoading) return;
  const resp = await fetch('/api/data');
  const json = await resp.json();

  if (json.ready === false) {
    document.getElementById('waiting-area').classList.remove('hidden');
    document.getElementById('main-content').classList.add('hidden');
    document.getElementById('panel').classList.add('hidden');
    document.getElementById('subtitle').textContent = '';
    updateStatusBar(json);
    setTimeout(loadData, 5000);
    return;
  }

  document.getElementById('waiting-area').classList.add('hidden');
  document.getElementById('main-content').classList.remove('hidden');
  document.getElementById('panel').classList.remove('hidden');

  data = json;
  updateStatusBar(data);
  render();
  setTimeout(loadData, 5000);
}

function updateStatusBar(d) {
  const dot = document.getElementById('watch-dot');
  const label = document.getElementById('watch-label');
  if (d.isWatching) {
    dot.classList.add('active');
    label.textContent = 'Watching';
  } else {
    dot.classList.remove('active');
    label.textContent = 'Not watching';
  }

  const fileInfo = document.getElementById('file-info');
  if (d.lastProcessedFile) {
    const timeStr = d.lastRunUtc ? new Date(d.lastRunUtc).toLocaleString() : '';
    fileInfo.textContent = `Last file: ${d.lastProcessedFile}${timeStr ? ' \u2022 ' + timeStr : ''}`;
  } else {
    fileInfo.textContent = '';
  }
}

function render() {
  document.getElementById('subtitle').textContent =
    `${data.accountIdentifier} \u2022 ${data.transactionCount} transactions \u2022 Balance: ${data.currency} ${data.currentBalance.toFixed(2)}`;

  // Summary
  document.getElementById('summary').innerHTML = `
    <div class="summary-card green"><div class="value">${data.autoMatched.length}</div><div class="label">Auto-matched</div></div>
    <div class="summary-card ${(data.splitMatches||[]).length > 0 ? 'green' : ''}"><div class="value">${(data.splitMatches||[]).length}</div><div class="label">Split matches</div></div>
    <div class="summary-card ${data.manualMatches.length > 0 ? 'purple' : ''}"><div class="value">${data.manualMatches.length}</div><div class="label">Manual matches</div></div>
    <div class="summary-card red"><div class="value">${data.unmatchedDebits.length}</div><div class="label">Unmatched debits</div></div>
    <div class="summary-card blue"><div class="value">${data.unmatchedCredits.length}</div><div class="label">Unmatched credits</div></div>
  `;

  // History
  const historySec = document.getElementById('history-section');
  if (data.history && data.history.length > 0) {
    historySec.classList.remove('hidden');
    const rows = historyExpanded ? data.history : data.history.slice(0, 10);
    document.getElementById('history-count').textContent = data.history.length;
    document.getElementById('history-list').innerHTML = rows.map(h => {
      const t = new Date(h.timestamp).toLocaleString();
      const unmatched = (h.unmatchedDebits || 0) + (h.unmatchedCredits || 0);
      return `<tr>
        <td>${t}</td>
        <td>${esc(h.fileName)}</td>
        <td>${h.transactionCount}</td>
        <td>${h.autoMatched}</td>
        <td>${unmatched}</td>
      </tr>`;
    }).join('');
    const toggle = document.getElementById('history-toggle');
    if (data.history.length > 10) {
      toggle.classList.remove('hidden');
      toggle.textContent = historyExpanded ? '\u25BE Show less' : `\u25B8 Show all ${data.history.length} runs...`;
    } else { toggle.classList.add('hidden'); }
  } else { historySec.classList.add('hidden'); }

  // Auto-matched
  const autoSec = document.getElementById('auto-section');
  if (data.autoMatched.length > 0) {
    autoSec.classList.remove('hidden');
    document.getElementById('auto-count').textContent = data.autoMatched.length;
    const show = autoExpanded ? data.autoMatched : data.autoMatched.slice(0, 3);
    document.getElementById('auto-list').innerHTML = show.map((m, i) => {
      const scoreClass = m.score >= 90 ? 'score-high' : m.score >= 75 ? 'score-med' : 'score-low';
      return `<div class="auto-row ${scoreClass}">
        <div class="side"><span class="amount debit">${fmt(-m.debit.absoluteAmount)}</span> ${esc(txLabel(m.debit))}</div>
        <span class="arrow">\u27F7</span>
        <div class="side"><span class="amount credit">${fmt(m.credit.absoluteAmount)}</span> ${esc(txLabel(m.credit))}</div>
        <span class="score-badge ${scoreClass}">${m.score}%</span>
        <button class="btn-exclude" onclick="doUnmatchAuto(${i})" title="Unmatch this pair">\u21C4</button>
      </div>`;
    }).join('');
    const toggle = document.getElementById('auto-toggle');
    if (data.autoMatched.length > 3) {
      toggle.classList.remove('hidden');
      toggle.textContent = autoExpanded ? '\u25BE Hide' : `\u25B8 Show ${data.autoMatched.length - 3} more...`;
    } else { toggle.classList.add('hidden'); }
  } else { autoSec.classList.add('hidden'); }

  // Split matches
  const splitSec = document.getElementById('split-section');
  if (data.splitMatches && data.splitMatches.length > 0) {
    splitSec.classList.remove('hidden');
    document.getElementById('split-count').textContent = data.splitMatches.length;
    document.getElementById('split-list').innerHTML = data.splitMatches.map(s => {
      const debits = s.debits.map(t => `<div class="group-row"><span class="amount debit">${fmt(-t.absoluteAmount)}</span> ${esc(txLabel(t))}</div>`).join('');
      return `<div class="manual-group" style="border-color:#c8e6c9"><div class="group-header"><span style="color:#2e7d32">Split: <span class="amount credit">${fmt(s.credit.absoluteAmount)}</span> ${esc(txLabel(s.credit))}</span></div>${debits}</div>`;
    }).join('');
  } else { splitSec.classList.add('hidden'); }

  // Manual matches
  const manSec = document.getElementById('manual-section');
  if (data.manualMatches.length > 0) {
    manSec.classList.remove('hidden');
    document.getElementById('manual-count').textContent = data.manualMatches.length;
    document.getElementById('manual-list').innerHTML = data.manualMatches.map((g, i) => {
      const debits = g.debits.map(t => `<div class="group-row"><span class="amount debit">${fmt(-t.absoluteAmount)}</span> ${esc(txLabel(t))}</div>`).join('');
      const credits = g.credits.map(t => `<div class="group-row"><span class="amount credit">${fmt(t.absoluteAmount)}</span> ${esc(txLabel(t))}</div>`).join('');
      return `<div class="manual-group"><div class="group-header"><span>Group ${i+1}</span><button class="btn btn-danger" onclick="doUnmatch(${i})">Undo</button></div>${debits}${credits}</div>`;
    }).join('');
  } else { manSec.classList.add('hidden'); }

  // Unmatched
  const debitFilter = (document.getElementById('debit-filter').value || '').toLowerCase();
  const creditFilter = (document.getElementById('credit-filter').value || '').toLowerCase();
  const filteredDebits = debitFilter
    ? data.unmatchedDebits.filter(t => matchesFilter(t, debitFilter))
    : data.unmatchedDebits;
  const filteredCredits = creditFilter
    ? data.unmatchedCredits.filter(t => matchesFilter(t, creditFilter))
    : data.unmatchedCredits;
  const debitTotal = data.unmatchedDebits.reduce((s, t) => s + t.absoluteAmount, 0);
  const creditTotal = data.unmatchedCredits.reduce((s, t) => s + t.absoluteAmount, 0);
  document.getElementById('debit-count').textContent = data.unmatchedDebits.length;
  document.getElementById('debit-total').textContent = fmt(debitTotal);
  document.getElementById('debit-list').innerHTML = filteredDebits.map(t => txRow(t, 'debit')).join('');
  document.getElementById('credit-count').textContent = data.unmatchedCredits.length;
  document.getElementById('credit-total').textContent = fmt(creditTotal);
  document.getElementById('credit-list').innerHTML = filteredCredits.map(t => txRow(t, 'credit')).join('');

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
    <td>${esc(txLabel(t))}</td>
    <td><button class="btn-exclude" onclick="event.stopPropagation(); doExclude('${t.id}')" title="Exclude — already settled">\u2716</button></td></tr>`;
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
function toggleHistory() { historyExpanded = !historyExpanded; render(); }

function showToast(msg, duration) {
  const existing = document.querySelector('.toast');
  if (existing) existing.remove();
  const el = document.createElement('div');
  el.className = 'toast';
  el.textContent = msg;
  document.body.appendChild(el);
  setTimeout(() => {
    el.classList.add('fade');
    setTimeout(() => el.remove(), 500);
  }, duration || 2000);
}

async function doMatch() {
  isLoading = true;
  const body = { debitIds: [...selectedDebits], creditIds: [...selectedCredits] };
  const resp = await fetch('/api/match', { method: 'POST', headers: {'Content-Type':'application/json'}, body: JSON.stringify(body) });
  isLoading = false;
  if (resp.ok) { selectedDebits.clear(); selectedCredits.clear(); await loadData(); }
  else { alert('Match failed: ' + await resp.text()); }
}

async function doUnmatch(index) {
  isLoading = true;
  const resp = await fetch('/api/unmatch', { method: 'POST', headers: {'Content-Type':'application/json'}, body: JSON.stringify({ index }) });
  isLoading = false;
  if (resp.ok) { await loadData(); }
  else { alert('Unmatch failed: ' + await resp.text()); }
}

async function doUnmatchAuto(index) {
  isLoading = true;
  const resp = await fetch('/api/unmatch-auto', { method: 'POST', headers: {'Content-Type':'application/json'}, body: JSON.stringify({ index }) });
  isLoading = false;
  if (resp.ok) { await loadData(); }
  else { alert('Unmatch failed: ' + await resp.text()); }
}

async function doExclude(id) {
  isLoading = true;
  const resp = await fetch('/api/exclude', { method: 'POST', headers: {'Content-Type':'application/json'}, body: JSON.stringify({ id }) });
  isLoading = false;
  if (resp.ok) { selectedDebits.delete(id); selectedCredits.delete(id); await loadData(); }
  else { alert('Exclude failed: ' + await resp.text()); }
}

async function doSave() {
  isLoading = true;
  const resp = await fetch('/api/save-matches', { method: 'POST' });
  isLoading = false;
  if (resp.ok) { showToast('Saved!', 2000); }
  else { alert('Save failed: ' + await resp.text()); }
}

async function doReprocess() {
  isLoading = true;
  showToast('Reprocessing...', 3000);
  const resp = await fetch('/api/process', { method: 'POST' });
  isLoading = false;
  if (resp.ok) { await loadData(); }
  else { alert('Reprocess failed: ' + await resp.text()); }
}

let allTransactions = null;

async function loadAllTransactions() {
  const resp = await fetch('/api/transactions');
  allTransactions = await resp.json();
  document.getElementById('all-tx-count').textContent = allTransactions.length;
  document.getElementById('all-tx-content').classList.remove('hidden');
  document.getElementById('tx-filter').value = '';
  renderTransactions(allTransactions);
}

function filterTransactions() {
  if (!allTransactions) return;
  const q = document.getElementById('tx-filter').value.toLowerCase();
  const filtered = q ? allTransactions.filter(t =>
    t.counterpartName.toLowerCase().includes(q) ||
    t.remittanceInformation.toLowerCase().includes(q) ||
    t.executionDate.includes(q) ||
    t.amount.toFixed(2).includes(q)
  ) : allTransactions;
  renderTransactions(filtered);
}

function renderTransactions(txs) {
  document.getElementById('all-tx-list').innerHTML = txs.map(t => {
    const cls = t.amount < 0 ? 'debit' : 'credit';
    const sign = t.amount < 0 ? '' : '+';
    return `<tr>
      <td>${t.executionDate}</td>
      <td class="amount ${cls}">${sign}${t.amount.toFixed(2)}</td>
      <td>${esc(t.transactionType)}</td>
      <td>${esc(t.counterpartName)}</td>
      <td>${esc(t.remittanceInformation)}</td>
      <td><button class="btn-exclude" onclick="event.stopPropagation(); doDeleteTransaction('${t.id}')" title="Delete transaction">\u2716</button></td>
    </tr>`;
  }).join('');
}

async function loadExcluded() {
  const resp = await fetch('/api/excluded');
  const excluded = await resp.json();
  document.getElementById('excluded-count').textContent = excluded.length;
  document.getElementById('excluded-content').classList.remove('hidden');
  document.getElementById('excluded-list').innerHTML = excluded.map(t => {
    const cls = t.amount < 0 ? 'debit' : 'credit';
    const sign = t.amount < 0 ? '' : '+';
    return `<tr>
      <td>${t.executionDate}</td>
      <td class="amount ${cls}">${sign}${t.absoluteAmount.toFixed(2)}</td>
      <td>${esc(txLabel(t))}</td>
      <td><button class="btn-exclude" onclick="doUnexclude('${t.id}')" title="Remove exclusion">\u21A9</button></td>
    </tr>`;
  }).join('');
}

async function doUnexclude(id) {
  isLoading = true;
  const resp = await fetch('/api/unexclude', { method: 'POST', headers: {'Content-Type':'application/json'}, body: JSON.stringify({ id }) });
  isLoading = false;
  if (resp.ok) { await loadExcluded(); await loadData(); }
  else { alert('Un-exclude failed: ' + await resp.text()); }
}

async function doDeleteTransaction(id) {
  if (!confirm('Delete this transaction from the database?')) return;
  isLoading = true;
  const resp = await fetch('/api/delete-transaction', { method: 'POST', headers: {'Content-Type':'application/json'}, body: JSON.stringify({ id }) });
  isLoading = false;
  if (resp.ok) { showToast('Transaction deleted', 2000); await loadAllTransactions(); await loadData(); }
  else { alert('Delete failed: ' + await resp.text()); }
}

let matchedHistoryPairs = null;

async function loadMatchedHistory() {
  const resp = await fetch('/api/matched-history');
  matchedHistoryPairs = await resp.json();
  document.getElementById('matched-history-count').textContent = matchedHistoryPairs.length;
  document.getElementById('matched-history-content').classList.remove('hidden');
  document.getElementById('matched-history-filter').value = '';
  renderMatchedHistory(matchedHistoryPairs);
}

function filterMatchedHistory() {
  if (!matchedHistoryPairs) return;
  const q = document.getElementById('matched-history-filter').value.toLowerCase();
  const filtered = q ? matchedHistoryPairs.filter(p =>
    matchesFilter(p.debit, q) || matchesFilter(p.credit, q)
  ) : matchedHistoryPairs;
  renderMatchedHistory(filtered);
}

function renderMatchedHistory(pairs) {
  document.getElementById('matched-history-list').innerHTML = pairs.map(m => `
    <div class="auto-row">
      <div class="side"><span class="amount debit">${fmt(-m.debit.absoluteAmount)}</span> ${esc(txLabel(m.debit))} <small style="color:#999">${m.debit.executionDate}</small></div>
      <span class="arrow">\u27F7</span>
      <div class="side"><span class="amount credit">${fmt(m.credit.absoluteAmount)}</span> ${esc(txLabel(m.credit))} <small style="color:#999">${m.credit.executionDate}</small></div>
    </div>`).join('');
}

async function loadSavedManualMatches() {
  const resp = await fetch('/api/manual-matches');
  const groups = await resp.json();
  document.getElementById('saved-manual-count').textContent = groups.length;
  document.getElementById('saved-manual-content').classList.remove('hidden');
  document.getElementById('saved-manual-list').innerHTML = groups.length === 0
    ? '<p style="color:#999;font-size:13px">No saved manual matches</p>'
    : groups.map((g, i) => {
      const debits = g.debits.map(t => `<div class="group-row"><span class="amount debit">${fmt(-t.absoluteAmount)}</span> ${esc(txLabel(t))} <small style="color:#999">${t.executionDate}</small></div>`).join('');
      const credits = g.credits.map(t => `<div class="group-row"><span class="amount credit">${fmt(t.absoluteAmount)}</span> ${esc(txLabel(t))} <small style="color:#999">${t.executionDate}</small></div>`).join('');
      return `<div class="manual-group"><div class="group-header"><span>Group ${i+1}</span></div>${debits}${credits}</div>`;
    }).join('');
}

function confirmReinitDb() {
  document.getElementById('reinit-modal').classList.remove('hidden');
}

function closeReinitModal() {
  document.getElementById('reinit-modal').classList.add('hidden');
}

async function doReinitDb() {
  closeReinitModal();
  isLoading = true;
  const resp = await fetch('/api/reinit-db', { method: 'POST' });
  isLoading = false;
  if (resp.ok) {
    allTransactions = null;
    matchedHistoryPairs = null;
    document.getElementById('all-tx-content').classList.add('hidden');
    document.getElementById('matched-history-content').classList.add('hidden');
    document.getElementById('saved-manual-content').classList.add('hidden');
    document.getElementById('excluded-content').classList.add('hidden');
    showToast('Database re-initialized', 3000);
    await loadData();
  } else {
    alert('Re-init failed: ' + await resp.text());
  }
}

function matchesFilter(t, q) {
  return (t.counterpartName || '').toLowerCase().includes(q)
    || (t.remittanceInformation || '').toLowerCase().includes(q)
    || t.executionDate.includes(q)
    || t.absoluteAmount.toFixed(2).includes(q);
}

function fmt(n) { return n.toFixed(2); }
function esc(s) { const d = document.createElement('div'); d.textContent = s; return d.innerHTML; }
function txLabel(t) { return t.remittanceInformation || t.counterpartName; }

loadData();
</script>
</body>
</html>
""";
}

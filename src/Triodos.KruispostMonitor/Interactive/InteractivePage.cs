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

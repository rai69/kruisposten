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

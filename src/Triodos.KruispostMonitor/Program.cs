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

// API — exclude transaction (already settled)
app.MapPost("/api/exclude", async (HttpContext ctx, MonitorState monitor, IStateStore stateStore) =>
{
    var body = await JsonSerializer.DeserializeAsync<ExcludeRequest>(ctx.Request.Body, jsonOptions);
    if (body is null || string.IsNullOrEmpty(body.Id)) return Results.BadRequest("Invalid request");

    if (!monitor.TryExcludeTransaction(body.Id))
        return Results.BadRequest("Transaction not found in unmatched lists");

    await stateStore.SaveAsync(monitor.State);
    return Results.Ok();
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
record ExcludeRequest(string Id);

using FluentAssertions;
using Triodos.KruispostMonitor.Matching;
using Triodos.KruispostMonitor.State;

namespace Triodos.KruispostMonitor.Tests.State;

public class StateStoreTests : IAsyncLifetime
{
    private readonly string _tempPath;
    private readonly SqliteStateStore _store;

    public StateStoreTests()
    {
        _tempPath = Path.Combine(Path.GetTempPath(), $"kruispost-test-{Guid.NewGuid()}.db");
        _store = new SqliteStateStore(_tempPath);
    }

    public async Task InitializeAsync()
    {
        await _store.InitializeAsync();
    }

    public Task DisposeAsync()
    {
        foreach (var ext in new[] { "", "-wal", "-shm" })
        {
            var path = _tempPath + ext;
            if (File.Exists(path)) File.Delete(path);
        }
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Load_WhenEmpty_ReturnsDefaultState()
    {
        var state = await _store.LoadAsync();

        state.Should().NotBeNull();
        state.LastRunUtc.Should().BeNull();
        state.MatchedTransactionIds.Should().BeEmpty();
    }

    [Fact]
    public async Task SaveAndLoad_RoundTrips()
    {
        var state = new RunState
        {
            LastRunUtc = new DateTimeOffset(2026, 3, 26, 10, 0, 0, TimeSpan.Zero),
            MatchedTransactionIds = ["tx-1", "tx-2"],
            RefreshToken = "new-refresh-token"
        };

        await _store.SaveAsync(state);
        var loaded = await _store.LoadAsync();

        loaded.LastRunUtc.Should().Be(state.LastRunUtc);
        loaded.MatchedTransactionIds.Should().BeEquivalentTo(["tx-1", "tx-2"]);
        loaded.RefreshToken.Should().Be("new-refresh-token");
    }

    [Fact]
    public async Task Save_OverwritesPreviousState()
    {
        await _store.SaveAsync(new RunState { MatchedTransactionIds = ["tx-1"] });
        await _store.SaveAsync(new RunState { MatchedTransactionIds = ["tx-2", "tx-3"] });
        var loaded = await _store.LoadAsync();

        loaded.MatchedTransactionIds.Should().BeEquivalentTo(["tx-2", "tx-3"]);
    }

    [Fact]
    public async Task SaveAndLoad_ManualMatches_RoundTrips()
    {
        var state = new RunState
        {
            ManualMatches =
            [
                new ManualMatch(["d1", "d2"], ["c1"]),
                new ManualMatch(["d3"], ["c2", "c3"])
            ]
        };

        await _store.SaveAsync(state);
        var loaded = await _store.LoadAsync();

        loaded.ManualMatches.Should().HaveCount(2);
        loaded.ManualMatches[0].DebitIds.Should().BeEquivalentTo(["d1", "d2"]);
        loaded.ManualMatches[0].CreditIds.Should().BeEquivalentTo(["c1"]);
        loaded.ManualMatches[1].DebitIds.Should().BeEquivalentTo(["d3"]);
        loaded.ManualMatches[1].CreditIds.Should().BeEquivalentTo(["c2", "c3"]);
    }

    [Fact]
    public async Task SaveAndLoad_ExcludedIds_RoundTrips()
    {
        var state = new RunState
        {
            ExcludedTransactionIds = ["ex-1", "ex-2"]
        };

        await _store.SaveAsync(state);
        var loaded = await _store.LoadAsync();

        loaded.ExcludedTransactionIds.Should().BeEquivalentTo(["ex-1", "ex-2"]);
    }

    [Fact]
    public async Task SaveTransactions_And_GetAll_RoundTrips()
    {
        var transactions = new List<TransactionRecord>
        {
            new("id-1", -100.00m, "Acme Corp", "Invoice 123", new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero), "NET"),
            new("id-2", 100.00m, "Acme Corp", "Invoice 123", new DateTimeOffset(2026, 3, 2, 0, 0, 0, TimeSpan.Zero), "NET")
        };

        await _store.SaveTransactionsAsync(transactions, "file1.mt940");
        var loaded = await _store.GetAllTransactionsAsync();

        loaded.Should().HaveCount(2);
        loaded.Should().Contain(t => t.Id == "id-1" && t.Amount == -100.00m && t.CounterpartName == "Acme Corp");
        loaded.Should().Contain(t => t.Id == "id-2" && t.Amount == 100.00m);
    }

    [Fact]
    public async Task SaveTransactions_DuplicateIds_AreIgnored()
    {
        var tx = new TransactionRecord("id-1", -50.00m, "Test", "Remi", new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

        await _store.SaveTransactionsAsync([tx], "file1.mt940");
        await _store.SaveTransactionsAsync([tx], "file2.mt940");
        var loaded = await _store.GetAllTransactionsAsync();

        loaded.Should().HaveCount(1);
    }

    [Fact]
    public async Task GetAllTransactions_ReturnsFromMultipleFiles()
    {
        var tx1 = new TransactionRecord("id-1", -50.00m, "A", "R1", new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var tx2 = new TransactionRecord("id-2", 50.00m, "B", "R2", new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero));

        await _store.SaveTransactionsAsync([tx1], "file1.mt940");
        await _store.SaveTransactionsAsync([tx2], "file2.mt940");
        var loaded = await _store.GetAllTransactionsAsync();

        loaded.Should().HaveCount(2);
        loaded.Should().Contain(t => t.Id == "id-1");
        loaded.Should().Contain(t => t.Id == "id-2");
    }
}

using FluentAssertions;
using Triodos.KruispostMonitor.State;

namespace Triodos.KruispostMonitor.Tests.State;

public class StateStoreTests : IDisposable
{
    private readonly string _tempPath;

    public StateStoreTests()
    {
        _tempPath = Path.Combine(Path.GetTempPath(), $"kruispost-test-{Guid.NewGuid()}.json");
    }

    public void Dispose()
    {
        if (File.Exists(_tempPath))
            File.Delete(_tempPath);
    }

    [Fact]
    public async Task Load_WhenFileDoesNotExist_ReturnsDefaultState()
    {
        var store = new StateStore(_tempPath);

        var state = await store.LoadAsync();

        state.Should().NotBeNull();
        state.LastRunUtc.Should().BeNull();
        state.MatchedTransactionIds.Should().BeEmpty();
    }

    [Fact]
    public async Task SaveAndLoad_RoundTrips()
    {
        var store = new StateStore(_tempPath);
        var state = new RunState
        {
            LastRunUtc = new DateTimeOffset(2026, 3, 26, 10, 0, 0, TimeSpan.Zero),
            MatchedTransactionIds = ["tx-1", "tx-2"],
            RefreshToken = "new-refresh-token"
        };

        await store.SaveAsync(state);
        var loaded = await store.LoadAsync();

        loaded.LastRunUtc.Should().Be(state.LastRunUtc);
        loaded.MatchedTransactionIds.Should().BeEquivalentTo(["tx-1", "tx-2"]);
        loaded.RefreshToken.Should().Be("new-refresh-token");
    }

    [Fact]
    public async Task Save_OverwritesPreviousState()
    {
        var store = new StateStore(_tempPath);

        await store.SaveAsync(new RunState { MatchedTransactionIds = ["tx-1"] });
        await store.SaveAsync(new RunState { MatchedTransactionIds = ["tx-2", "tx-3"] });
        var loaded = await store.LoadAsync();

        loaded.MatchedTransactionIds.Should().BeEquivalentTo(["tx-2", "tx-3"]);
    }
}

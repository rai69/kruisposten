namespace Triodos.KruispostMonitor.State;

public class RunState
{
    public DateTimeOffset? LastRunUtc { get; set; }
    public HashSet<string> MatchedTransactionIds { get; set; } = [];
    public string? RefreshToken { get; set; }
    public List<ManualMatch> ManualMatches { get; set; } = [];
}

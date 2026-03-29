using Triodos.KruispostMonitor.Services;

namespace Triodos.KruispostMonitor.State;

public class RunState
{
    public DateTimeOffset? LastRunUtc { get; set; }
    public HashSet<string> MatchedTransactionIds { get; set; } = [];
    public string? RefreshToken { get; set; }
    public List<ManualMatch> ManualMatches { get; set; } = [];
    public HashSet<string> ExcludedTransactionIds { get; set; } = [];
    public string? LastProcessedFile { get; set; }
    public List<ProcessingRun> History { get; set; } = [];
}

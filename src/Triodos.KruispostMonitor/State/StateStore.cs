using System.Text.Json;

namespace Triodos.KruispostMonitor.State;

public interface IStateStore
{
    Task<RunState> LoadAsync();
    Task SaveAsync(RunState state);
}

public class StateStore : IStateStore
{
    private readonly string _filePath;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public StateStore(string filePath)
    {
        _filePath = filePath;
    }

    public async Task<RunState> LoadAsync()
    {
        if (!File.Exists(_filePath))
            return new RunState();

        await using var stream = File.OpenRead(_filePath);
        return await JsonSerializer.DeserializeAsync<RunState>(stream, JsonOptions) ?? new RunState();
    }

    public async Task SaveAsync(RunState state)
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        await using var stream = File.Create(_filePath);
        await JsonSerializer.SerializeAsync(stream, state, JsonOptions);
    }
}

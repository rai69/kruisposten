using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Triodos.KruispostMonitor.Configuration;
using Triodos.KruispostMonitor.State;

namespace Triodos.KruispostMonitor.Services;

public class FileWatcherService : BackgroundService
{
    private readonly FileWatcherSettings _settings;
    private readonly ProcessingService _processingService;
    private readonly MonitorState _monitorState;
    private readonly IStateStore _stateStore;
    private readonly ILogger<FileWatcherService> _logger;

    public FileWatcherService(
        IOptions<FileWatcherSettings> settings,
        ProcessingService processingService,
        MonitorState monitorState,
        IStateStore stateStore,
        ILogger<FileWatcherService> logger)
    {
        _settings = settings.Value;
        _processingService = processingService;
        _monitorState = monitorState;
        _stateStore = stateStore;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Ensure directories exist
        Directory.CreateDirectory(_settings.WatchPath);
        Directory.CreateDirectory(_settings.ProcessedPath);

        // Restore dashboard state from last processed file
        await RestoreLastProcessedFileAsync();

        // Process any new files in the watch folder
        await ProcessExistingFilesAsync();

        _logger.LogInformation("Watching for MT940 files in {Path}", _settings.WatchPath);
        _monitorState.IsWatching = true;

        using var watcher = new FileSystemWatcher(_settings.WatchPath);
        watcher.Filter = "*.*";
        watcher.NotifyFilter = NotifyFilters.FileName | NotifyFilters.CreationTime;
        watcher.EnableRaisingEvents = true;

        var fileQueue = new Queue<string>();
        watcher.Created += (_, e) =>
        {
            if (IsMatchingFile(e.FullPath))
            {
                lock (fileQueue) { fileQueue.Enqueue(e.FullPath); }
            }
        };

        while (!stoppingToken.IsCancellationRequested)
        {
            string? filePath = null;
            lock (fileQueue)
            {
                if (fileQueue.Count > 0) filePath = fileQueue.Dequeue();
            }

            if (filePath is not null)
            {
                // Wait for file to finish writing
                await Task.Delay(2000, stoppingToken);
                await ProcessAndMoveFileAsync(filePath);
            }
            else
            {
                await Task.Delay(1000, stoppingToken);
            }
        }

        _monitorState.IsWatching = false;
    }

    private async Task RestoreLastProcessedFileAsync()
    {
        try
        {
            var state = await _stateStore.LoadAsync();
            _monitorState.RestoreHistory(state);

            if (state.LastProcessedFile is not null)
            {
                var filePath = Path.Combine(_settings.ProcessedPath, state.LastProcessedFile);
                if (File.Exists(filePath))
                {
                    await _processingService.ReloadFileAsync(filePath);
                    _logger.LogInformation("Restored dashboard state from {File}", state.LastProcessedFile);
                }
                else
                {
                    _logger.LogWarning("Last processed file {File} not found in processed folder", state.LastProcessedFile);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not restore previous state, starting fresh");
        }
    }

    private async Task ProcessExistingFilesAsync()
    {
        var files = Directory.GetFiles(_settings.WatchPath)
            .Where(IsMatchingFile)
            .OrderBy(f => File.GetCreationTimeUtc(f))
            .ToList();

        foreach (var file in files)
        {
            await ProcessAndMoveFileAsync(file);
        }
    }

    private async Task ProcessAndMoveFileAsync(string filePath)
    {
        try
        {
            await _processingService.ProcessFileAsync(filePath);

            // Move to processed folder
            var destPath = Path.Combine(_settings.ProcessedPath, Path.GetFileName(filePath));
            if (File.Exists(destPath))
                destPath = Path.Combine(_settings.ProcessedPath,
                    $"{Path.GetFileNameWithoutExtension(filePath)}_{DateTimeOffset.UtcNow:yyyyMMddHHmmss}{Path.GetExtension(filePath)}");

            File.Move(filePath, destPath);
            _logger.LogInformation("Processed and moved file to {Dest}", destPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process file {FilePath}", filePath);
        }
    }

    private static bool IsMatchingFile(string path)
    {
        var name = Path.GetFileName(path).ToLowerInvariant();
        return name.EndsWith(".mt940") || name.EndsWith(".mt940structured") || name.EndsWith(".sta");
    }
}

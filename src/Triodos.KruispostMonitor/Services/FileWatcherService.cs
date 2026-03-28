using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Triodos.KruispostMonitor.Configuration;

namespace Triodos.KruispostMonitor.Services;

public class FileWatcherService : BackgroundService
{
    private readonly FileWatcherSettings _settings;
    private readonly ProcessingService _processingService;
    private readonly MonitorState _monitorState;
    private readonly ILogger<FileWatcherService> _logger;

    public FileWatcherService(
        IOptions<FileWatcherSettings> settings,
        ProcessingService processingService,
        MonitorState monitorState,
        ILogger<FileWatcherService> logger)
    {
        _settings = settings.Value;
        _processingService = processingService;
        _monitorState = monitorState;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Ensure directories exist
        Directory.CreateDirectory(_settings.WatchPath);
        Directory.CreateDirectory(_settings.ProcessedPath);

        // Process any existing files on startup
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
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is ".mt940" or ".sta";
    }
}

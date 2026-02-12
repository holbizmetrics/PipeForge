using Microsoft.Extensions.Logging;
using PipeForge.Core.Engine;
using PipeForge.Core.Models;

namespace PipeForge.Core.Watcher;

/// <summary>
/// Watches file system for changes and triggers pipeline execution.
/// Hardened against: buffer overflow, duplicate events, rapid changes.
/// </summary>
public class PipelineWatcher : IDisposable
{
    private readonly ILogger<PipelineWatcher> _logger;
    private readonly List<FileSystemWatcher> _watchers = new();
    private readonly Dictionary<string, Timer> _debounceTimers = new();
    private readonly Dictionary<string, DateTime> _recentTriggers = new();
    private readonly object _lock = new();
    private bool _disposed;

    /// <summary>
    /// Internal buffer size for FileSystemWatcher. Default 8KB is too small
    /// for rapid changes (git checkout, npm install). 64KB handles ~4000 events.
    /// </summary>
    public int BufferSize { get; set; } = 65536;

    /// <summary>
    /// Minimum interval between triggers for the same watch key.
    /// Prevents re-triggering while a pipeline is still running.
    /// </summary>
    public TimeSpan MinTriggerInterval { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Fires when a file change is detected (after debounce).
    /// The string parameter is the file path that triggered it.
    /// </summary>
    public event Func<string, WatchTrigger, Task>? OnTriggered;

    public PipelineWatcher(ILogger<PipelineWatcher> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Start watching based on pipeline trigger definitions.
    /// </summary>
    public void Start(IEnumerable<WatchTrigger> triggers)
    {
        foreach (var trigger in triggers)
        {
            var normalizedPath = PathNormalizer.NormalizeSeparators(trigger.Path);

            if (!Directory.Exists(normalizedPath))
            {
                _logger.LogWarning("Watch path does not exist, skipping: {Path}", normalizedPath);
                continue;
            }

            var watcher = new FileSystemWatcher(normalizedPath, trigger.Filter)
            {
                IncludeSubdirectories = trigger.IncludeSubdirectories,
                InternalBufferSize = BufferSize,
                NotifyFilter = NotifyFilters.LastWrite
                             | NotifyFilters.FileName
                             | NotifyFilters.CreationTime,
                EnableRaisingEvents = true
            };

            var localTrigger = trigger;

            watcher.Changed += (_, e) => HandleChange(e.FullPath, localTrigger);
            watcher.Created += (_, e) => HandleChange(e.FullPath, localTrigger);
            watcher.Renamed += (_, e) => HandleChange(e.FullPath, localTrigger);

            // Handle buffer overflow — log and continue (events are lost, not fatal)
            watcher.Error += (_, e) =>
            {
                var ex = e.GetException();
                _logger.LogWarning("FileSystemWatcher error (events may have been lost): {Message}", ex.Message);

                // Attempt recovery: restart the watcher
                try
                {
                    watcher.EnableRaisingEvents = false;
                    watcher.EnableRaisingEvents = true;
                    _logger.LogInformation("FileSystemWatcher recovered for {Path}/{Filter}", normalizedPath, trigger.Filter);
                }
                catch (Exception restartEx)
                {
                    _logger.LogError(restartEx, "Failed to restart FileSystemWatcher for {Path}", normalizedPath);
                }
            };

            _watchers.Add(watcher);
            _logger.LogInformation("Watching: {Path}/{Filter} (debounce: {Ms}ms, buffer: {KB}KB)",
                normalizedPath, trigger.Filter, trigger.DebounceMs, BufferSize / 1024);
        }
    }

    private void HandleChange(string filePath, WatchTrigger trigger)
    {
        lock (_lock)
        {
            var key = $"{trigger.Path}:{trigger.Filter}";

            // Suppress duplicate triggers within MinTriggerInterval
            if (_recentTriggers.TryGetValue(key, out var lastTrigger) &&
                DateTime.UtcNow - lastTrigger < MinTriggerInterval)
            {
                _logger.LogDebug("Suppressed duplicate trigger for {Key} (within {Ms}ms window)",
                    key, MinTriggerInterval.TotalMilliseconds);
                return;
            }

            // Debounce: reset timer on each change
            if (_debounceTimers.TryGetValue(key, out var existing))
            {
                existing.Dispose();
            }

            _debounceTimers[key] = new Timer(
                async _ =>
                {
                    lock (_lock)
                    {
                        _recentTriggers[key] = DateTime.UtcNow;
                    }

                    _logger.LogInformation("File changed: {File} → triggering pipeline", filePath);

                    try
                    {
                        if (OnTriggered != null)
                            await OnTriggered.Invoke(filePath, trigger);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error in pipeline trigger handler");
                    }
                },
                null,
                trigger.DebounceMs,
                Timeout.Infinite);
        }
    }

    public void Stop()
    {
        foreach (var watcher in _watchers)
        {
            watcher.EnableRaisingEvents = false;
        }
        _logger.LogInformation("All watchers stopped");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        foreach (var watcher in _watchers)
            watcher.Dispose();

        lock (_lock)
        {
            foreach (var timer in _debounceTimers.Values)
                timer.Dispose();
        }

        GC.SuppressFinalize(this);
    }
}

using Microsoft.Extensions.Logging;
using PipeForge.Core.Models;

namespace PipeForge.Core.Watcher;

/// <summary>
/// Watches file system for changes and triggers pipeline execution.
/// Supports debouncing (so a save-all doesn't fire 50 times),
/// filter patterns, and targeted stage execution.
/// </summary>
public class PipelineWatcher : IDisposable
{
    private readonly ILogger<PipelineWatcher> _logger;
    private readonly List<FileSystemWatcher> _watchers = new();
    private readonly Dictionary<string, Timer> _debounceTimers = new();
    private readonly object _lock = new();
    private bool _disposed;

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
            if (!Directory.Exists(trigger.Path))
            {
                _logger.LogWarning("Watch path does not exist, skipping: {Path}", trigger.Path);
                continue;
            }

            var watcher = new FileSystemWatcher(trigger.Path, trigger.Filter)
            {
                IncludeSubdirectories = trigger.IncludeSubdirectories,
                NotifyFilter = NotifyFilters.LastWrite 
                             | NotifyFilters.FileName 
                             | NotifyFilters.CreationTime,
                EnableRaisingEvents = true
            };

            var localTrigger = trigger; // Capture for closure

            watcher.Changed += (_, e) => HandleChange(e.FullPath, localTrigger);
            watcher.Created += (_, e) => HandleChange(e.FullPath, localTrigger);
            watcher.Renamed += (_, e) => HandleChange(e.FullPath, localTrigger);

            _watchers.Add(watcher);
            _logger.LogInformation("Watching: {Path}/{Filter} (debounce: {Ms}ms)", 
                trigger.Path, trigger.Filter, trigger.DebounceMs);
        }
    }

    private void HandleChange(string filePath, WatchTrigger trigger)
    {
        lock (_lock)
        {
            // Debounce: reset timer on each change
            var key = $"{trigger.Path}:{trigger.Filter}";
            
            if (_debounceTimers.TryGetValue(key, out var existing))
            {
                existing.Dispose();
            }

            _debounceTimers[key] = new Timer(
                async _ =>
                {
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

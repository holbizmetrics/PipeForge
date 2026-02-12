using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using PipeForge.Core.Engine;
using PipeForge.Core.Models;

namespace PipeForge.GUI.ViewModels;

public partial class LiveRunViewModel : ObservableObject
{
    private PipelineEngine? _engine;
    private CancellationTokenSource? _cts;
    private TaskCompletionSource<DebugAction>? _breakpointTcs;
    private ILoggerFactory? _loggerFactory;
    private bool _stepNextRequested;

    [ObservableProperty]
    private ObservableCollection<OutputLineItem> _outputLines = new();

    [ObservableProperty]
    private ObservableCollection<StepProgressItem> _stepProgress = new();

    [ObservableProperty]
    private bool _isRunning;

    [ObservableProperty]
    private bool _isPaused;

    [ObservableProperty]
    private string _statusText = "Ready";

    [ObservableProperty]
    private string? _pipelineName;

    [ObservableProperty]
    private string _elapsedText = "";

    [ObservableProperty]
    private string _stepProgressText = "";

    [ObservableProperty]
    private string? _breakpointStepName;

    [ObservableProperty]
    private string? _breakpointStageName;

    [ObservableProperty]
    private string? _breakpointInfo;

    [ObservableProperty]
    private string? _loadedFilePath;

    [RelayCommand]
    private async Task RunPipelineAsync()
    {
        var topLevel = Avalonia.Application.Current?.ApplicationLifetime is
            Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow : null;

        if (topLevel == null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(
            new Avalonia.Platform.Storage.FilePickerOpenOptions
            {
                Title = "Select Pipeline YAML to Run",
                FileTypeFilter = new[]
                {
                    new Avalonia.Platform.Storage.FilePickerFileType("YAML files")
                    {
                        Patterns = new[] { "*.yml", "*.yaml" }
                    }
                },
                AllowMultiple = false
            });

        if (files.Count == 0) return;

        var path = files[0].Path.LocalPath;
        LoadedFilePath = path;

        await RunFromPathAsync(path);
    }

    [RelayCommand]
    private async Task RestartAsync()
    {
        if (LoadedFilePath != null)
            await RunFromPathAsync(LoadedFilePath);
    }

    [RelayCommand]
    private void ToggleBreakpoint(StepProgressItem item)
    {
        item.HasBreakpoint = !item.HasBreakpoint;
    }

    private async Task RunFromPathAsync(string path)
    {
        // Reset state
        OutputLines.Clear();
        StepProgress.Clear();
        IsPaused = false;
        IsRunning = true;
        StatusText = "Loading...";
        StepProgressText = "";

        PipelineDefinition pipeline;
        try
        {
            pipeline = PipelineLoader.LoadFromFile(path);
            PipelineName = pipeline.Name;
        }
        catch (Exception ex)
        {
            StatusText = $"Failed to load: {ex.Message}";
            IsRunning = false;
            return;
        }

        // Populate step progress from pipeline definition
        int stepNumber = 0;
        foreach (var stage in pipeline.Stages)
        {
            foreach (var step in stage.Steps)
            {
                stepNumber++;
                StepProgress.Add(new StepProgressItem
                {
                    StageName = stage.Name,
                    StepName = step.Name,
                    Command = $"{step.Command} {step.Arguments ?? ""}".Trim(),
                    StepNumber = stepNumber,
                    HasBreakpoint = step.Breakpoint == BreakpointMode.Always
                });
            }
        }

        int totalSteps = stepNumber;
        StepProgressText = $"0/{totalSteps}";

        // Trust check
        try
        {
            var trustStore = new TrustStore();
            var check = trustStore.Check(path);
            if (check.Status != TrustStatus.Trusted)
            {
                OutputLines.Add(new OutputLineItem(
                    DateTime.Now, $"[Trust] {check.Status}: {path} (SHA-256: {check.CurrentHash[..16]}...)", false));
                trustStore.Trust(path, check.CurrentHash);
            }
        }
        catch { /* advisory */ }

        // Create engine
        _loggerFactory = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Debug));
        _engine = new PipelineEngine(_loggerFactory.CreateLogger<PipelineEngine>());
        _cts = new CancellationTokenSource();

        // Wire real-time output
        _engine.OnOutput += (_, line) =>
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                OutputLines.Add(new OutputLineItem(
                    line.Timestamp, line.Text, line.Source == OutputSource.StdErr));
                if (OutputLines.Count > 10_000)
                    OutputLines.RemoveAt(0);
            });
        };

        // Wire breakpoints — smart: only pause at GUI-breakpointed or YAML-breakpointed steps
        _engine.OnBeforeStep += (_, e) =>
        {
            var tcs = new TaskCompletionSource<DebugAction>();
            _breakpointTcs = tcs;

            // Find matching step progress item
            var progressItem = e.StepIndex > 0 && e.StepIndex <= StepProgress.Count
                ? StepProgress[e.StepIndex - 1]
                : null;

            bool shouldPause = progressItem?.HasBreakpoint == true
                || e.Step.Breakpoint == BreakpointMode.Always
                || _stepNextRequested;

            _stepNextRequested = false;

            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                // Update step progress: mark as running
                if (progressItem != null)
                {
                    // Clear previous current indicator
                    foreach (var item in StepProgress)
                        item.IsCurrent = false;

                    progressItem.IsCurrent = true;
                    progressItem.Status = StepProgressStatus.Running;
                }

                StepProgressText = $"{e.StepIndex}/{e.TotalSteps}";

                if (shouldPause)
                {
                    IsPaused = true;
                    BreakpointStepName = e.Step.Name;
                    BreakpointStageName = e.StageName;
                    BreakpointInfo = $"Step {e.StepIndex}/{e.TotalSteps}: {e.Step.Command} {e.Step.Arguments ?? ""}";
                    StatusText = $"Paused at {e.StageName}/{e.Step.Name}";
                }
                else
                {
                    // Auto-continue: no breakpoint set for this step
                    tcs.TrySetResult(DebugAction.Continue);
                }
            });

            // Block engine thread until user decides (or auto-continue resolves)
            e.Action = tcs.Task.Result;

            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                IsPaused = false;
            });
        };

        // Wire after-step to update step progress
        _engine.OnAfterStep += (_, e) =>
        {
            var last = e.Run.StepResults.LastOrDefault();
            if (last != null)
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    // Find matching progress item
                    var progressItem = StepProgress.FirstOrDefault(p =>
                        p.StepName == last.StepName && p.StageName == last.StageName);

                    if (progressItem != null)
                    {
                        progressItem.Status = last.Status switch
                        {
                            StepStatus.Success => StepProgressStatus.Success,
                            StepStatus.Failed => StepProgressStatus.Failed,
                            StepStatus.Skipped => StepProgressStatus.Skipped,
                            _ => StepProgressStatus.Pending
                        };
                        progressItem.Duration = $"{last.Elapsed.TotalSeconds:F1}s";
                        progressItem.ExitCode = last.ExitCode;
                        progressItem.IsCurrent = false;
                    }

                    int completed = StepProgress.Count(s =>
                        s.Status is StepProgressStatus.Success or StepProgressStatus.Failed or StepProgressStatus.Skipped);
                    StepProgressText = $"{completed}/{StepProgress.Count}";
                });
            }
        };

        // Run on background thread
        StatusText = $"Running: {pipeline.Name}";
        try
        {
            var run = await Task.Run(() => _engine.RunAsync(pipeline, true, _cts.Token));

            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                StatusText = $"{run.Status} — {run.SuccessCount}/{run.StepResults.Count} steps, {run.Elapsed.TotalSeconds:F1}s";
                ElapsedText = $"{run.Elapsed.TotalSeconds:F1}s";

                // Clear current indicator
                foreach (var item in StepProgress)
                    item.IsCurrent = false;
            });
        }
        catch (OperationCanceledException)
        {
            StatusText = "Cancelled";
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
        }
        finally
        {
            IsRunning = false;
            _engine = null;
            _loggerFactory?.Dispose();
            _loggerFactory = null;
        }
    }

    [RelayCommand]
    private void ContinueStep() => ResolveBreakpoint(DebugAction.Continue);

    [RelayCommand]
    private void StepNext()
    {
        // Continue this step, but force pause at the next one (like F10)
        _stepNextRequested = true;
        ResolveBreakpoint(DebugAction.Continue);
    }

    [RelayCommand]
    private void SkipStep() => ResolveBreakpoint(DebugAction.Skip);

    [RelayCommand]
    private void RetryStep() => ResolveBreakpoint(DebugAction.Retry);

    [RelayCommand]
    private void AbortPipeline()
    {
        ResolveBreakpoint(DebugAction.Abort);
        _cts?.Cancel();
    }

    [RelayCommand]
    private void CancelRun()
    {
        _cts?.Cancel();
        _breakpointTcs?.TrySetResult(DebugAction.Abort);
        StatusText = "Cancelling...";
    }

    private void ResolveBreakpoint(DebugAction action)
    {
        _breakpointTcs?.TrySetResult(action);
    }
}

public record OutputLineItem(DateTime Timestamp, string Text, bool IsError);

public partial class StepProgressItem : ObservableObject
{
    public required string StageName { get; init; }
    public required string StepName { get; init; }
    public required string Command { get; init; }
    public required int StepNumber { get; init; }

    [ObservableProperty]
    private StepProgressStatus _status = StepProgressStatus.Pending;

    [ObservableProperty]
    private bool _hasBreakpoint;

    [ObservableProperty]
    private bool _isCurrent;

    [ObservableProperty]
    private string? _duration;

    [ObservableProperty]
    private int? _exitCode;
}

public enum StepProgressStatus { Pending, Running, Success, Failed, Skipped }

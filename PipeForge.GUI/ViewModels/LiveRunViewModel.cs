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

    [ObservableProperty]
    private ObservableCollection<OutputLineItem> _outputLines = new();

    [ObservableProperty]
    private ObservableCollection<StepResultItem> _stepResults = new();

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

    private async Task RunFromPathAsync(string path)
    {
        // Reset state
        OutputLines.Clear();
        StepResults.Clear();
        IsPaused = false;
        IsRunning = true;
        StatusText = "Loading...";

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
        using var loggerFactory = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Debug));
        _engine = new PipelineEngine(loggerFactory.CreateLogger<PipelineEngine>());
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

        // Wire breakpoints
        _engine.OnBeforeStep += (_, e) =>
        {
            var tcs = new TaskCompletionSource<DebugAction>();
            _breakpointTcs = tcs;

            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                IsPaused = true;
                BreakpointStepName = e.Step.Name;
                BreakpointStageName = e.StageName;
                BreakpointInfo = $"Step {e.StepIndex}/{e.TotalSteps}: {e.Step.Command} {e.Step.Arguments ?? ""}";
                StatusText = $"Paused at {e.StageName}/{e.Step.Name}";
            });

            // Block engine thread until user decides
            e.Action = tcs.Task.Result;

            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                IsPaused = false;
            });
        };

        // Wire after-step for results table
        _engine.OnAfterStep += (_, e) =>
        {
            var last = e.Run.StepResults.LastOrDefault();
            if (last != null)
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    StepResults.Add(new StepResultItem(
                        last.StageName,
                        last.StepName,
                        last.Status.ToString(),
                        $"{last.Elapsed.TotalSeconds:F1}s",
                        last.ExitCode,
                        last.Status == StepStatus.Failed));
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
        }
    }

    [RelayCommand]
    private void ContinueStep() => ResolveBreakpoint(DebugAction.Continue);

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
public record StepResultItem(string Stage, string Step, string Status, string Duration, int ExitCode, bool IsFailed);

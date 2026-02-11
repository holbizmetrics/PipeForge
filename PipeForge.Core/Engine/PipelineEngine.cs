using System.Diagnostics;
using Microsoft.Extensions.Logging;
using PipeForge.Core.Models;

namespace PipeForge.Core.Engine;

/// <summary>
/// Event args for the step debugger. When a breakpoint fires,
/// this carries the full pipeline state for inspection.
/// </summary>
public class StepBreakpointEventArgs : EventArgs
{
    public required PipelineRun Run { get; init; }
    public required PipelineStep Step { get; init; }
    public required string StageName { get; init; }
    public required int StepIndex { get; init; }
    public required int TotalSteps { get; init; }
    
    /// <summary>
    /// Set this in your handler to control what happens next.
    /// Default: Continue.
    /// </summary>
    public DebugAction Action { get; set; } = DebugAction.Continue;
}

public enum DebugAction
{
    Continue,
    Skip,
    Retry,
    Abort
}

/// <summary>
/// Core pipeline execution engine.
/// 
/// KEY INSIGHT: Every step is a method call on this class.
/// Set a breakpoint on ExecuteStepAsync to step-debug ANY pipeline.
/// The PipelineRun object carries all state — hover over it in VS.
/// </summary>
public class PipelineEngine
{
    private readonly ILogger<PipelineEngine> _logger;
    private readonly ProcessWrapper _processWrapper;
    private CancellationTokenSource? _cts;

    /// <summary>
    /// Fires BEFORE each step executes. Attach a handler to implement
    /// interactive debugging, logging, or approval gates.
    /// </summary>
    public event EventHandler<StepBreakpointEventArgs>? OnBeforeStep;
    
    /// <summary>
    /// Fires AFTER each step completes. Useful for inspection,
    /// artifact collection, notifications.
    /// </summary>
    public event EventHandler<StepBreakpointEventArgs>? OnAfterStep;
    
    /// <summary>
    /// Fires when a step writes to stdout or stderr — real-time output.
    /// </summary>
    public event EventHandler<OutputLine>? OnOutput;

    public PipelineEngine(ILogger<PipelineEngine> logger, ProcessWrapper? processWrapper = null)
    {
        _logger = logger;
        _processWrapper = processWrapper ?? new ProcessWrapper();
    }

    /// <summary>
    /// Run the full pipeline. Set breakpoints on the marked lines below
    /// to step through execution in Visual Studio.
    /// </summary>
    public async Task<PipelineRun> RunAsync(
        PipelineDefinition pipeline, 
        bool interactiveMode = false,
        CancellationToken cancellationToken = default)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        
        var run = new PipelineRun
        {
            PipelineName = pipeline.Name,
            Status = PipelineRunStatus.Running,
            TriggerReason = "Manual",
            Variables = new Dictionary<string, string>(pipeline.Variables)
        };

        // Resolve working directory
        var workDir = pipeline.WorkingDirectory ?? Directory.GetCurrentDirectory();
        run.Variables["PIPEFORGE_WORK_DIR"] = workDir;
        run.Variables["PIPEFORGE_RUN_ID"] = run.RunId.ToString();
        run.Variables["PIPEFORGE_PIPELINE"] = pipeline.Name;

        _logger.LogInformation("Pipeline '{Name}' started (Run: {RunId})", pipeline.Name, run.RunId);

        var stepIndex = 0;
        var totalSteps = pipeline.Stages.SelectMany(s => s.Steps).Count();

        try
        {
            foreach (var stage in pipeline.Stages)
            {
                if (!EvaluateStageCondition(stage, run))
                {
                    _logger.LogInformation("Stage '{Stage}' skipped (condition not met)", stage.Name);
                    continue;
                }

                _logger.LogInformation("▸ Stage: {Stage}", stage.Name);

                foreach (var step in stage.Steps)
                {
                    stepIndex++;
                    _cts.Token.ThrowIfCancellationRequested();

                    // ══════════════════════════════════════════════════════════
                    // ► BREAKPOINT HERE — Set F9 on this line in Visual Studio
                    //   to pause BEFORE every step. Inspect 'run' for full state.
                    // ══════════════════════════════════════════════════════════
                    var action = await HandleBreakpoint(run, step, stage.Name, stepIndex, totalSteps, interactiveMode);

                    switch (action)
                    {
                        case DebugAction.Skip:
                            _logger.LogInformation("  ⏭ Step '{Step}' skipped by debugger", step.Name);
                            continue;
                        case DebugAction.Abort:
                            _logger.LogWarning("  ⛔ Pipeline aborted by debugger at step '{Step}'", step.Name);
                            run.Status = PipelineRunStatus.Cancelled;
                            run.CompletedAt = DateTime.UtcNow;
                            return run;
                    }

                    // Execute the step
                    var result = await ExecuteStepAsync(step, stage.Name, run, workDir);

                    // Fire post-step event
                    OnAfterStep?.Invoke(this, new StepBreakpointEventArgs
                    {
                        Run = run,
                        Step = step,
                        StageName = stage.Name,
                        StepIndex = stepIndex,
                        TotalSteps = totalSteps
                    });

                    // Handle failure
                    if (result.Status == StepStatus.Failed && !step.AllowFailure)
                    {
                        if (step.Breakpoint == BreakpointMode.OnFailure)
                        {
                            // ══════════════════════════════════════════════
                            // ► FAILURE BREAKPOINT — inspect result.ErrorMessage,
                            //   result.StandardError, result.FullOutput
                            // ══════════════════════════════════════════════
                            var failAction = await HandleBreakpoint(run, step, stage.Name, stepIndex, totalSteps, true);
                            
                            if (failAction == DebugAction.Retry)
                            {
                                _logger.LogInformation("  🔄 Retrying step '{Step}'", step.Name);
                                result = await ExecuteStepAsync(step, stage.Name, run, workDir);
                            }
                            else if (failAction == DebugAction.Skip)
                            {
                                continue;
                            }
                        }

                        if (result.Status == StepStatus.Failed && !stage.ContinueOnError)
                        {
                            _logger.LogError("Pipeline failed at step '{Step}'", step.Name);
                            run.Status = PipelineRunStatus.Failed;
                            run.CompletedAt = DateTime.UtcNow;
                            return run;
                        }
                    }
                }
            }

            run.Status = run.HasFailures ? PipelineRunStatus.Failed : PipelineRunStatus.Success;
        }
        catch (OperationCanceledException)
        {
            run.Status = PipelineRunStatus.Cancelled;
            _logger.LogWarning("Pipeline '{Name}' cancelled", pipeline.Name);
        }
        catch (Exception ex)
        {
            run.Status = PipelineRunStatus.Failed;
            _logger.LogError(ex, "Pipeline '{Name}' failed with exception", pipeline.Name);
        }
        finally
        {
            run.CompletedAt = DateTime.UtcNow;
        }

        _logger.LogInformation(
            "Pipeline '{Name}' completed: {Status} ({Success}/{Total} steps, {Elapsed:F1}s)",
            pipeline.Name, run.Status, run.SuccessCount, 
            run.StepResults.Count, run.Elapsed.TotalSeconds);

        return run;
    }

    /// <summary>
    /// Execute a single pipeline step. This is THE method to set breakpoints on.
    /// After this returns, result contains everything: output, exit code, timing, artifacts.
    /// </summary>
    private async Task<StepResult> ExecuteStepAsync(
        PipelineStep step, 
        string stageName, 
        PipelineRun run, 
        string defaultWorkDir)
    {
        var result = new StepResult
        {
            StepName = step.Name,
            StageName = stageName,
            Command = $"{step.Command} {step.Arguments}".Trim(),
            Status = StepStatus.Running,
            StartedAt = DateTime.UtcNow,
            Environment = new Dictionary<string, string>(run.Variables)
        };

        // Merge step-specific environment
        foreach (var (key, value) in step.Environment)
            result.Environment[key] = ResolveVariables(value, run.Variables);

        run.StepResults.Add(result);

        var stepWorkDir = step.WorkingDirectory != null 
            ? ResolveVariables(step.WorkingDirectory, run.Variables)
            : defaultWorkDir;

        var resolvedCommand = ResolveVariables(step.Command, run.Variables);
        var resolvedArgs = step.Arguments != null 
            ? ResolveVariables(step.Arguments, run.Variables) 
            : null;

        _logger.LogInformation("  ▸ {Step}: {Command} {Args}", step.Name, resolvedCommand, resolvedArgs ?? "");

        try
        {
            var exitCode = await _processWrapper.RunAsync(
                resolvedCommand,
                resolvedArgs,
                stepWorkDir,
                result.Environment,
                line =>
                {
                    var outputLine = new OutputLine { Text = line, Source = OutputSource.StdOut };
                    result.StandardOutput.Add(outputLine);
                    OnOutput?.Invoke(this, outputLine);
                },
                line =>
                {
                    var outputLine = new OutputLine { Text = line, Source = OutputSource.StdErr };
                    result.StandardError.Add(outputLine);
                    OnOutput?.Invoke(this, outputLine);
                },
                TimeSpan.FromSeconds(step.TimeoutSeconds),
                _cts?.Token ?? CancellationToken.None);

            result.ExitCode = exitCode;
            result.Status = exitCode == 0 ? StepStatus.Success : StepStatus.Failed;

            if (result.Status == StepStatus.Failed)
                result.ErrorMessage = $"Process exited with code {exitCode}";

            // Collect artifacts
            foreach (var pattern in step.Artifacts)
            {
                var resolvedPattern = ResolveVariables(pattern, run.Variables);
                var artifactDir = Path.GetDirectoryName(resolvedPattern) ?? stepWorkDir;
                var artifactFilter = Path.GetFileName(resolvedPattern);
                
                if (Directory.Exists(artifactDir))
                {
                    foreach (var file in Directory.GetFiles(artifactDir, artifactFilter))
                    {
                        var info = new FileInfo(file);
                        var artifact = new ArtifactInfo
                        {
                            Path = file,
                            StepName = step.Name,
                            SizeBytes = info.Length
                        };
                        run.Artifacts.Add(artifact);
                        result.ArtifactsProduced.Add(file);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            result.Status = StepStatus.Failed;
            result.ErrorMessage = ex.Message;
            _logger.LogError(ex, "Step '{Step}' threw an exception", step.Name);
        }
        finally
        {
            result.CompletedAt = DateTime.UtcNow;
        }

        var icon = result.Status == StepStatus.Success ? "✓" : "✗";
        _logger.LogInformation("  {Icon} {Step} ({Elapsed:F1}s)", icon, step.Name, result.Elapsed.TotalSeconds);

        // ═══════════════════════════════════════════════════════
        // ► INSPECT HERE — after return, 'result' has everything:
        //   result.FullOutput, result.ExitCode, result.ArtifactsProduced
        // ═══════════════════════════════════════════════════════
        return result;
    }

    private async Task<DebugAction> HandleBreakpoint(
        PipelineRun run, PipelineStep step, string stageName,
        int stepIndex, int totalSteps, bool interactive)
    {
        if (!interactive && step.Breakpoint == BreakpointMode.Never)
            return DebugAction.Continue;

        if (step.Breakpoint == BreakpointMode.Always || interactive)
        {
            run.Status = PipelineRunStatus.Paused;

            var args = new StepBreakpointEventArgs
            {
                Run = run,
                Step = step,
                StageName = stageName,
                StepIndex = stepIndex,
                TotalSteps = totalSteps
            };

            // ═══════════════════════════════════════════════════════
            // ► BREAKPOINT EVENT — if you're debugging in VS, you can
            //   also just set a breakpoint here and inspect 'args.Run'
            // ═══════════════════════════════════════════════════════
            OnBeforeStep?.Invoke(this, args);

            run.Status = PipelineRunStatus.Running;
            return args.Action;
        }

        return DebugAction.Continue;
    }

    private bool EvaluateStageCondition(PipelineStage stage, PipelineRun run)
    {
        if (stage.Condition == null) return true;

        if (stage.Condition.RequiresFiles.Count > 0)
        {
            foreach (var file in stage.Condition.RequiresFiles)
            {
                var resolved = ResolveVariables(file, run.Variables);
                if (!File.Exists(resolved))
                {
                    _logger.LogDebug("Stage '{Stage}' skipped: required file '{File}' not found", 
                        stage.Name, resolved);
                    return false;
                }
            }
        }

        if (stage.Condition.OnlyIf != null)
        {
            var key = stage.Condition.OnlyIf;
            if (!run.Variables.TryGetValue(key, out var val) || 
                string.IsNullOrEmpty(val) || val == "false")
                return false;
        }

        return true;
    }

    /// <summary>
    /// Simple ${VAR} variable resolution.
    /// </summary>
    private static string ResolveVariables(string input, Dictionary<string, string> variables)
    {
        var result = input;
        foreach (var (key, value) in variables)
        {
            result = result.Replace($"${{{key}}}", value);
        }
        return result;
    }

    public void Cancel() => _cts?.Cancel();
}

using System.Diagnostics;

namespace PipeForge.Core.Models;

/// <summary>
/// Complete pipeline execution state. This is what you see in the debugger
/// when you hover over it — the entire world at a glance.
/// </summary>
public class PipelineRun
{
    public Guid RunId { get; } = Guid.NewGuid();
    public string PipelineName { get; set; } = string.Empty;
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    public PipelineRunStatus Status { get; set; } = PipelineRunStatus.Pending;
    public string? TriggerReason { get; set; }
    
    /// <summary>
    /// All variables available to the pipeline, including computed ones.
    /// Inspect this at any breakpoint to see the full variable state.
    /// </summary>
    public Dictionary<string, string> Variables { get; set; } = new();
    
    /// <summary>
    /// Results of every step that has executed so far.
    /// The debugger lets you drill into any of these.
    /// </summary>
    public List<StepResult> StepResults { get; } = new();
    
    /// <summary>
    /// Artifacts produced so far, with their paths.
    /// </summary>
    public List<ArtifactInfo> Artifacts { get; } = new();

    public TimeSpan Elapsed => (CompletedAt ?? DateTime.UtcNow) - StartedAt;
    
    public StepResult? CurrentStep => StepResults.LastOrDefault(s => s.Status == StepStatus.Running);
    public StepResult? LastCompleted => StepResults.LastOrDefault(s => s.Status is StepStatus.Success or StepStatus.Failed);
    public int SuccessCount => StepResults.Count(s => s.Status == StepStatus.Success);
    public int FailedCount => StepResults.Count(s => s.Status == StepStatus.Failed);
    public bool HasFailures => FailedCount > 0;
}

/// <summary>
/// Result of a single step execution. Every field is inspectable in VS debugger.
/// </summary>
public class StepResult
{
    public string StepName { get; set; } = string.Empty;
    public string StageName { get; set; } = string.Empty;
    public string Command { get; set; } = string.Empty;
    public StepStatus Status { get; set; } = StepStatus.Pending;
    public int ExitCode { get; set; } = -1;
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public TimeSpan Elapsed => (CompletedAt ?? DateTime.UtcNow) - StartedAt;
    
    /// <summary>
    /// Captured stdout — every line, timestamped.
    /// </summary>
    public List<OutputLine> StandardOutput { get; } = new();
    
    /// <summary>
    /// Captured stderr — every line, timestamped.
    /// </summary>
    public List<OutputLine> StandardError { get; } = new();
    
    /// <summary>
    /// Combined output in chronological order.
    /// </summary>
    public string FullOutput => string.Join(Environment.NewLine, 
        StandardOutput.Concat(StandardError)
            .OrderBy(l => l.Timestamp)
            .Select(l => l.Text));
    
    /// <summary>
    /// Environment variables that were active for this step.
    /// </summary>
    public Dictionary<string, string> Environment { get; set; } = new();
    
    /// <summary>
    /// Files that were created or modified during this step.
    /// </summary>
    public List<string> ArtifactsProduced { get; } = new();
    
    public string? ErrorMessage { get; set; }
    
    public override string ToString() => 
        $"[{Status}] {StageName}/{StepName} (exit: {ExitCode}, {Elapsed.TotalSeconds:F1}s)";
}

public class OutputLine
{
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string Text { get; set; } = string.Empty;
    public OutputSource Source { get; set; }
    
    public override string ToString() => $"[{Timestamp:HH:mm:ss.fff}] [{Source}] {Text}";
}

public class ArtifactInfo
{
    public string Path { get; set; } = string.Empty;
    public string StepName { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public enum PipelineRunStatus
{
    Pending,
    Running,
    Paused,       // <-- Paused at a breakpoint
    Success,
    Failed,
    Cancelled
}

public enum StepStatus
{
    Pending,
    Skipped,
    Running,
    Success,
    Failed
}

public enum OutputSource
{
    StdOut,
    StdErr
}

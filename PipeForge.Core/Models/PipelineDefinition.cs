namespace PipeForge.Core.Models;

/// <summary>
/// Root pipeline definition. Maps directly to the YAML file.
/// </summary>
public class PipelineDefinition
{
    /// <summary>
    /// Current schema version. Bump when the YAML shape changes in breaking ways.
    /// </summary>
    public const int CurrentSchemaVersion = 1;

    public string Name { get; set; } = "Unnamed Pipeline";
    public string? Description { get; set; }
    public int Version { get; set; } = 0; // 0 = not specified
    public string? WorkingDirectory { get; set; }
    public Dictionary<string, string> Variables { get; set; } = new();
    public List<WatchTrigger> Watch { get; set; } = new();
    public List<PipelineStage> Stages { get; set; } = new();
}

/// <summary>
/// File watch trigger configuration.
/// </summary>
public class WatchTrigger
{
    public string Path { get; set; } = ".";
    public string Filter { get; set; } = "*.*";
    public bool IncludeSubdirectories { get; set; } = false;
    public int DebounceMs { get; set; } = 500;
    public string? OnlyStage { get; set; }
}

/// <summary>
/// A stage groups related steps (like GitLab CI stages).
/// </summary>
public class PipelineStage
{
    public string Name { get; set; } = "default";
    public List<PipelineStep> Steps { get; set; } = new();
    public StageCondition? Condition { get; set; }
    public bool ContinueOnError { get; set; } = false;
}

/// <summary>
/// A single executable step in the pipeline.
/// </summary>
public class PipelineStep
{
    public string Name { get; set; } = "Unnamed Step";
    public string? Description { get; set; }
    public string Command { get; set; } = string.Empty;
    public string? Arguments { get; set; }
    public string? WorkingDirectory { get; set; }
    public Dictionary<string, string> Environment { get; set; } = new();
    public int TimeoutSeconds { get; set; } = 300;
    public bool AllowFailure { get; set; } = false;
    public List<string> Artifacts { get; set; } = new();
    public StepCondition? Condition { get; set; }
    
    /// <summary>
    /// Breakpoint mode: Always, OnFailure, Never.
    /// When set to Always, the debugger pauses BEFORE this step executes.
    /// </summary>
    public BreakpointMode Breakpoint { get; set; } = BreakpointMode.Never;
}

public enum BreakpointMode
{
    Never,
    Always,
    OnFailure
}

public class StageCondition
{
    public string? OnlyIf { get; set; }
    public string? NotIf { get; set; }
    public List<string> RequiresFiles { get; set; } = new();
}

public class StepCondition
{
    public string? OnlyIf { get; set; }
    public string? NotIf { get; set; }
    public int? OnlyIfExitCode { get; set; }
}

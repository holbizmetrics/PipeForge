using System.Text.RegularExpressions;
using PipeForge.Core.Models;

namespace PipeForge.Core.Engine;

/// <summary>
/// Validates a pipeline definition without executing it.
/// Returns errors (must fix) and warnings (should investigate).
/// </summary>
public static partial class PipelineValidator
{
    public static ValidationResult Validate(PipelineDefinition pipeline)
    {
        var result = new ValidationResult();

        ValidatePipeline(pipeline, result);

        foreach (var stage in pipeline.Stages)
        {
            ValidateStage(stage, pipeline, result);

            foreach (var step in stage.Steps)
            {
                ValidateStep(step, stage.Name, pipeline, result);
            }
        }

        CheckDuplicateStageNames(pipeline, result);

        return result;
    }

    /// <summary>
    /// Parse YAML and validate in one call. Returns parse errors if YAML is invalid,
    /// or semantic validation results if YAML parses successfully.
    /// </summary>
    public static ValidationResult ValidateYaml(string yaml)
    {
        var result = new ValidationResult();

        PipelineDefinition pipeline;
        try
        {
            pipeline = PipelineLoader.LoadFromYaml(yaml);
        }
        catch (Exception ex)
        {
            result.AddError("YAML", $"Failed to parse YAML: {ex.Message}");
            return result;
        }

        return Validate(pipeline);
    }

    /// <summary>
    /// Parse YAML file and validate. Returns parse errors or semantic validation results.
    /// </summary>
    public static ValidationResult ValidateFile(string path)
    {
        var result = new ValidationResult();

        if (!File.Exists(path))
        {
            result.AddError("file", $"File not found: {path}");
            return result;
        }

        string yaml;
        try
        {
            yaml = File.ReadAllText(path);
        }
        catch (Exception ex)
        {
            result.AddError("file", $"Cannot read file: {ex.Message}");
            return result;
        }

        return ValidateYaml(yaml);
    }

    private static void ValidatePipeline(PipelineDefinition pipeline, ValidationResult result)
    {
        // Version check
        if (pipeline.Version == 0)
            result.AddWarning("version", "No 'version' field. Add 'version: 1' for compatibility tracking.");
        else if (pipeline.Version > PipelineDefinition.CurrentSchemaVersion)
            result.AddWarning("version",
                $"Pipeline version {pipeline.Version} is newer than this PipeForge (schema {PipelineDefinition.CurrentSchemaVersion}). Some features may not be supported.");
        else if (pipeline.Version < PipelineDefinition.CurrentSchemaVersion)
            result.AddWarning("version",
                $"Pipeline version {pipeline.Version} is older than current schema ({PipelineDefinition.CurrentSchemaVersion}). Consider updating.");

        if (string.IsNullOrWhiteSpace(pipeline.Name) || pipeline.Name == "Unnamed Pipeline")
            result.AddWarning("name", "Pipeline has no name. Set 'name:' to identify it.");

        if (pipeline.Stages.Count == 0)
            result.AddError("stages", "Pipeline has no stages. Add at least one stage with steps.");

        foreach (var watch in pipeline.Watch)
        {
            if (string.IsNullOrWhiteSpace(watch.Path))
                result.AddError("watch.path", "Watch trigger has an empty path.");

            if (watch.DebounceMs < 0)
                result.AddError("watch.debounce_ms", $"Debounce must be non-negative, got {watch.DebounceMs}.");
        }
    }

    private static void ValidateStage(PipelineStage stage, PipelineDefinition pipeline, ValidationResult result)
    {
        if (string.IsNullOrWhiteSpace(stage.Name) || stage.Name == "default")
            result.AddWarning($"stage", "A stage has no name. Set 'name:' to identify it.");

        if (stage.Steps.Count == 0)
            result.AddError($"stage '{stage.Name}'", "Stage has no steps.");

        if (stage.Condition?.OnlyIf != null)
        {
            var condVar = stage.Condition.OnlyIf;
            if (!pipeline.Variables.ContainsKey(condVar))
                result.AddWarning($"stage '{stage.Name}'.condition.only_if",
                    $"References variable '{condVar}' which is not defined in variables. " +
                    "This stage will be skipped unless the variable is set at runtime.");
        }

        // Check for duplicate step names within this stage
        var stepNames = stage.Steps
            .Where(s => !string.IsNullOrWhiteSpace(s.Name) && s.Name != "Unnamed Step")
            .GroupBy(s => s.Name)
            .Where(g => g.Count() > 1);

        foreach (var dup in stepNames)
            result.AddWarning($"stage '{stage.Name}'",
                $"Duplicate step name '{dup.Key}' ({dup.Count()} occurrences).");
    }

    private static void ValidateStep(PipelineStep step, string stageName, PipelineDefinition pipeline, ValidationResult result)
    {
        var location = $"stage '{stageName}' / step '{step.Name}'";

        if (string.IsNullOrWhiteSpace(step.Command))
            result.AddError(location, "Step has no command.");

        if (step.TimeoutSeconds <= 0)
            result.AddError(location, $"Timeout must be positive, got {step.TimeoutSeconds}s.");

        // Check variable references in command and arguments
        CheckVariableReferences(step.Command, location, pipeline.Variables, result);
        if (step.Arguments != null)
            CheckVariableReferences(step.Arguments, location, pipeline.Variables, result);
    }

    private static void CheckVariableReferences(string text, string location, Dictionary<string, string> variables, ValidationResult result)
    {
        foreach (Match match in VariablePattern().Matches(text))
        {
            var varName = match.Groups[1].Value;
            if (!variables.ContainsKey(varName))
            {
                // Built-in variables that PipelineEngine sets at runtime
                if (varName is "PIPEFORGE_WORK_DIR" or "PIPEFORGE_RUN_ID" or "PIPEFORGE_PIPELINE")
                    continue;

                result.AddWarning(location,
                    $"References undefined variable '${{{varName}}}'. " +
                    "Define it in 'variables:' or ensure it's set at runtime.");
            }
        }
    }

    private static void CheckDuplicateStageNames(PipelineDefinition pipeline, ValidationResult result)
    {
        var duplicates = pipeline.Stages
            .Where(s => !string.IsNullOrWhiteSpace(s.Name) && s.Name != "default")
            .GroupBy(s => s.Name)
            .Where(g => g.Count() > 1);

        foreach (var dup in duplicates)
            result.AddError("stages", $"Duplicate stage name '{dup.Key}' ({dup.Count()} occurrences).");
    }

    [GeneratedRegex(@"\$\{([^}]+)\}")]
    private static partial Regex VariablePattern();
}

public class ValidationResult
{
    public List<ValidationMessage> Messages { get; } = [];

    public bool HasErrors => Messages.Any(m => m.Severity == ValidationSeverity.Error);
    public bool HasWarnings => Messages.Any(m => m.Severity == ValidationSeverity.Warning);
    public bool IsValid => !HasErrors;

    public IEnumerable<ValidationMessage> Errors => Messages.Where(m => m.Severity == ValidationSeverity.Error);
    public IEnumerable<ValidationMessage> Warnings => Messages.Where(m => m.Severity == ValidationSeverity.Warning);

    public void AddError(string location, string message) =>
        Messages.Add(new ValidationMessage(ValidationSeverity.Error, location, message));

    public void AddWarning(string location, string message) =>
        Messages.Add(new ValidationMessage(ValidationSeverity.Warning, location, message));
}

public record ValidationMessage(ValidationSeverity Severity, string Location, string Message);

public enum ValidationSeverity
{
    Warning,
    Error
}

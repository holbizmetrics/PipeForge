using System.Text.RegularExpressions;
using PipeForge.Core.Models;

namespace PipeForge.Core.Engine;

/// <summary>
/// Pattern-matches stderr and error context to produce actionable suggestions.
/// Each hint is a (pattern, suggestion) pair — when the pattern matches
/// stderr or the error message, the suggestion is added to StepResult.Hints.
/// </summary>
public static partial class ErrorHints
{
    private static readonly List<(Regex Pattern, string Hint)> Patterns =
    [
        // Command not found
        (NotRecognizedPattern(), "Is the command installed and on PATH?"),
        (CommandNotFoundPattern(), "Is the command installed and on PATH?"),
        (NoSuchFilePattern(), "Check that the file or command path exists."),

        // Access / permissions
        (AccessDeniedPattern(), "Check file permissions, or run as administrator."),
        (PermissionDeniedPattern(), "Check file permissions, or run as administrator."),

        // .NET specific
        (DotnetSdkNotFoundPattern(), "Is the .NET SDK installed? Run 'dotnet --info' to check."),
        (NugetRestoreFailedPattern(), "Check network connectivity and NuGet source configuration."),
        (BuildFailedPattern(), "Review the compiler errors above. Use --interactive to pause and inspect."),

        // Inno Setup specific
        (IsccErrorPattern(), "Check your .iss script for syntax errors. Verify ISCC_PATH is correct."),

        // Timeout
        (TimeoutPattern(), "Step exceeded its timeout. Increase timeout_seconds or investigate why it's slow."),

        // Generic exit codes
        (ExitCode1Pattern(), "The command reported failure. Check stderr output above for details."),
    ];

    /// <summary>
    /// Analyze a failed StepResult and populate its Hints list.
    /// </summary>
    public static void Analyze(StepResult result)
    {
        if (result.Status != StepStatus.Failed)
            return;

        var stderrText = string.Join("\n", result.StandardError.Select(l => l.Text));
        var searchText = string.Join("\n", stderrText, result.ErrorMessage ?? "");

        var matched = new HashSet<string>();
        foreach (var (pattern, hint) in Patterns)
        {
            if (pattern.IsMatch(searchText) && matched.Add(hint))
                result.Hints.Add(hint);
        }
    }

    // ── Patterns ──────────────────────────────────────────────────────

    [GeneratedRegex(@"is not recognized as an? (internal or external|internal|external) command", RegexOptions.IgnoreCase)]
    private static partial Regex NotRecognizedPattern();

    [GeneratedRegex(@"command not found", RegexOptions.IgnoreCase)]
    private static partial Regex CommandNotFoundPattern();

    [GeneratedRegex(@"no such file or directory", RegexOptions.IgnoreCase)]
    private static partial Regex NoSuchFilePattern();

    [GeneratedRegex(@"access is denied", RegexOptions.IgnoreCase)]
    private static partial Regex AccessDeniedPattern();

    [GeneratedRegex(@"permission denied", RegexOptions.IgnoreCase)]
    private static partial Regex PermissionDeniedPattern();

    [GeneratedRegex(@"\.NET SDK.*not (found|installed)", RegexOptions.IgnoreCase)]
    private static partial Regex DotnetSdkNotFoundPattern();

    [GeneratedRegex(@"(nuget|restore) .*(fail|error|unable)", RegexOptions.IgnoreCase)]
    private static partial Regex NugetRestoreFailedPattern();

    [GeneratedRegex(@"Build (FAILED|failed)", RegexOptions.IgnoreCase)]
    private static partial Regex BuildFailedPattern();

    [GeneratedRegex(@"(ISCC|Inno Setup).*(error|fatal)", RegexOptions.IgnoreCase)]
    private static partial Regex IsccErrorPattern();

    [GeneratedRegex(@"(timed? ?out|timeout|exceeded.*time)", RegexOptions.IgnoreCase)]
    private static partial Regex TimeoutPattern();

    [GeneratedRegex(@"exit(ed)? (with )?(code |status )?1\b")]
    private static partial Regex ExitCode1Pattern();
}

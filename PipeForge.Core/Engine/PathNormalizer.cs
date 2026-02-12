namespace PipeForge.Core.Engine;

/// <summary>
/// Normalizes file paths for the current OS.
/// Applied only to paths consumed by .NET APIs (working dirs, artifact patterns,
/// condition file checks, watch paths) — NOT to shell command arguments.
/// </summary>
public static class PathNormalizer
{
    /// <summary>
    /// Full normalization: expand ~, normalize separators, resolve relative to base, resolve . and ..
    /// </summary>
    public static string Normalize(string path, string? basePath = null)
    {
        if (string.IsNullOrWhiteSpace(path))
            return path;

        // Expand ~ to user home directory (common in YAML configs)
        if (path is "~" || path.StartsWith("~/") || path.StartsWith("~\\"))
        {
            var home = System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);
            path = path == "~" ? home : Path.Combine(home, path[2..]);
        }

        // Normalize separators to OS convention (/ → \ on Windows, \ → / on Linux)
        path = path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);

        // Make absolute if relative
        if (!Path.IsPathRooted(path))
        {
            basePath ??= Directory.GetCurrentDirectory();
            path = Path.Combine(basePath, path);
        }

        // Resolve . and .. segments
        return Path.GetFullPath(path);
    }

    /// <summary>
    /// Normalize only directory separators without resolving to absolute.
    /// Use for artifact patterns or display strings that should stay relative.
    /// </summary>
    public static string NormalizeSeparators(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return path;

        return path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
    }
}

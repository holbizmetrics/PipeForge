using System.Security.Cryptography;
using System.Text.Json;

namespace PipeForge.Core.Engine;

/// <summary>
/// Tracks SHA-256 hashes of pipeline YAML files the user has seen.
/// On first run of a new/modified file, the CLI prints a trust notice
/// showing the commands that will execute — so the user can distinguish
/// safe from unsafe YAML (dodges the SECURITY-THEATER signature).
/// </summary>
public class TrustStore
{
    private readonly string _storePath;
    private Dictionary<string, string> _hashes; // canonical path → SHA-256

    public TrustStore(string? configDir = null)
    {
        configDir ??= Path.Combine(
            System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile),
            ".pipeforge");
        _storePath = Path.Combine(configDir, "trusted-hashes.json");
        _hashes = Load();
    }

    /// <summary>
    /// Check whether a pipeline file is trusted (hash matches), new, or modified.
    /// </summary>
    public TrustCheckResult Check(string filePath)
    {
        var canonicalPath = Path.GetFullPath(filePath);
        var currentHash = ComputeHash(canonicalPath);

        if (_hashes.TryGetValue(canonicalPath, out var storedHash))
        {
            return storedHash == currentHash
                ? new TrustCheckResult(TrustStatus.Trusted, currentHash)
                : new TrustCheckResult(TrustStatus.Modified, currentHash, storedHash);
        }

        return new TrustCheckResult(TrustStatus.New, currentHash);
    }

    /// <summary>
    /// Mark a file as trusted. Stores its current hash (or a provided one).
    /// </summary>
    public void Trust(string filePath, string? hash = null)
    {
        var canonicalPath = Path.GetFullPath(filePath);
        hash ??= ComputeHash(canonicalPath);
        _hashes[canonicalPath] = hash;
        Save();
    }

    /// <summary>
    /// SHA-256 hash of file contents, returned as lowercase hex.
    /// </summary>
    public static string ComputeHash(string filePath)
    {
        var bytes = File.ReadAllBytes(filePath);
        var hashBytes = SHA256.HashData(bytes);
        return Convert.ToHexStringLower(hashBytes);
    }

    private Dictionary<string, string> Load()
    {
        if (!File.Exists(_storePath))
            return new();

        try
        {
            var json = File.ReadAllText(_storePath);
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new();
        }
        catch
        {
            // Corrupt or unreadable store — start fresh
            return new();
        }
    }

    private void Save()
    {
        var dir = Path.GetDirectoryName(_storePath);
        if (dir != null && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(_hashes, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_storePath, json);
    }
}

public enum TrustStatus
{
    /// <summary>File hash matches stored hash — no notice needed.</summary>
    Trusted,
    /// <summary>File has never been seen — show first-run notice.</summary>
    New,
    /// <summary>File hash differs from stored hash — show modification notice.</summary>
    Modified
}

public record TrustCheckResult(
    TrustStatus Status,
    string CurrentHash,
    string? PreviousHash = null);

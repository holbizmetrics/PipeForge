using System.Diagnostics;

namespace PipeForge.Core.Engine;

/// <summary>
/// Wraps System.Diagnostics.Process for testability and output capture.
/// Every external command goes through here.
/// </summary>
public class ProcessWrapper
{
    public virtual async Task<int> RunAsync(
        string command,
        string? arguments,
        string workingDirectory,
        Dictionary<string, string> environment,
        Action<string> onStdOut,
        Action<string> onStdErr,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        // Determine shell based on OS
        var (shell, shellArgs) = GetShell(command, arguments);

        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = shell,
            Arguments = shellArgs,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        // Set environment variables
        foreach (var (key, value) in environment)
        {
            process.StartInfo.EnvironmentVariables[key] = value;
        }

        var outputComplete = new TaskCompletionSource<bool>();
        var errorComplete = new TaskCompletionSource<bool>();

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data != null) onStdOut(e.Data);
            else outputComplete.TrySetResult(true);
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null) onStdErr(e.Data);
            else errorComplete.TrySetResult(true);
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var timeoutCts = new CancellationTokenSource(timeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, timeoutCts.Token);

        try
        {
            await process.WaitForExitAsync(linkedCts.Token);
            
            // Wait for output streams to finish flushing
            await Task.WhenAll(
                outputComplete.Task.WaitAsync(TimeSpan.FromSeconds(5)),
                errorComplete.Task.WaitAsync(TimeSpan.FromSeconds(5))
            ).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
        {
            TryKill(process);
            throw new TimeoutException(
                $"Process '{command}' exceeded timeout of {timeout.TotalSeconds}s");
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }

        return process.ExitCode;
    }

    private static (string shell, string args) GetShell(string command, string? arguments)
    {
        var fullCommand = arguments != null ? $"{command} {arguments}" : command;

        if (OperatingSystem.IsWindows())
        {
            // Prefer PowerShell if available, fall back to cmd
            return ("cmd.exe", $"/c {fullCommand}");
        }
        else
        {
            return ("/bin/bash", $"-c \"{fullCommand.Replace("\"", "\\\"")}\"");
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch { /* Best effort */ }
    }
}

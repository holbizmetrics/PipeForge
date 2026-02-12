using System.Diagnostics;
using System.Text;

namespace PipeForge.Core.Engine;

/// <summary>
/// Sends completion notifications — terminal bell (universal)
/// and OS-level toast notifications (platform-specific, best-effort).
/// </summary>
public static class Notifier
{
    /// <summary>
    /// Emit terminal bell character. Works in all terminals.
    /// </summary>
    public static void Bell() => Console.Write('\a');

    /// <summary>
    /// Attempt to send an OS-level toast notification.
    /// Best-effort: silently ignores failures (missing tools, permissions, etc.).
    /// </summary>
    public static void OsNotify(string title, string message)
    {
        try
        {
            if (OperatingSystem.IsWindows())
                WindowsToast(title, message);
            else if (OperatingSystem.IsLinux())
                LinuxNotify(title, message);
            else if (OperatingSystem.IsMacOS())
                MacNotify(title, message);
        }
        catch
        {
            // Notification is best-effort — never block the pipeline
        }
    }

    private static void WindowsToast(string title, string message)
    {
        // Use -EncodedCommand to avoid all quoting/escaping issues
        var script = $"""
            Add-Type -AssemblyName System.Windows.Forms
            $n = New-Object System.Windows.Forms.NotifyIcon
            $n.Icon = [System.Drawing.SystemIcons]::Information
            $n.Visible = $true
            $n.ShowBalloonTip(5000, '{title.Replace("'", "''")}', '{message.Replace("'", "''")}', [System.Windows.Forms.ToolTipIcon]::Info)
            Start-Sleep 6
            $n.Dispose()
            """;
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));

        Process.Start(new ProcessStartInfo("powershell",
            $"-NoProfile -WindowStyle Hidden -EncodedCommand {encoded}")
        {
            CreateNoWindow = true,
            UseShellExecute = false
        });
    }

    private static void LinuxNotify(string title, string message)
    {
        Process.Start("notify-send", [title, message]);
    }

    private static void MacNotify(string title, string message)
    {
        var escaped = message.Replace("\"", "\\\"");
        var titleEsc = title.Replace("\"", "\\\"");
        Process.Start("osascript",
            ["-e", $"display notification \"{escaped}\" with title \"{titleEsc}\""]);
    }
}

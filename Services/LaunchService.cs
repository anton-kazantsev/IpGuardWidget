using System.Diagnostics;
using System.IO;

namespace IpGuard;

public sealed record LaunchOutcome(bool Ok, string? Message = null, string? Detail = null);

/// <summary>
/// Starts Claude Code in Windows Terminal. The tab stays open after Claude exits
/// (cmd /k by default) so the last output is still readable.
/// </summary>
public sealed class LaunchService
{
    public LaunchOutcome Launch(Settings settings)
    {
        var claude = ResolveClaude(settings);
        if (claude is null)
        {
            return new LaunchOutcome(
                false,
                "Claude Code не найден",
                "Установи: claude.ai/install");
        }

        var workingDirectory = ResolveWorkingDirectory(settings);
        var shell = NormalizeShell(settings.Shell);
        var terminal = FindWindowsTerminal();

        try
        {
            if (terminal is not null)
            {
                var arguments = BuildTerminalArguments(claude, workingDirectory, shell);
                Log.Info($"Launch: \"{terminal}\" {arguments}");
                Process.Start(new ProcessStartInfo(terminal)
                {
                    Arguments = arguments,
                    UseShellExecute = true,
                    WorkingDirectory = workingDirectory
                });
                return new LaunchOutcome(true);
            }

            // No Windows Terminal (older Windows 10): use a plain console window.
            var fallback = BuildFallback(claude, shell);
            Log.Info($"Launch (no wt.exe): {fallback.FileName} {fallback.Arguments}");
            fallback.UseShellExecute = true;
            fallback.WorkingDirectory = workingDirectory;
            Process.Start(fallback);
            return new LaunchOutcome(true);
        }
        catch (Exception ex)
        {
            Log.Error("Launch failed: " + ex);
            return new LaunchOutcome(false, "Не удалось запустить терминал", ex.Message);
        }
    }

    private static string BuildTerminalArguments(string claude, string workingDirectory, string shell)
    {
        var title = "\"Claude Code\"";
        var dir = Quote(workingDirectory);

        return shell switch
        {
            "pwsh" => $"new-tab --title {title} -d {dir} {FindPowerShell()} -NoExit -Command claude",
            "direct" when claude.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                => $"new-tab --title {title} -d {dir} {Quote(claude)}",
            _ => $"new-tab --title {title} -d {dir} cmd.exe /k {Quote(claude)}"
        };
    }

    private static ProcessStartInfo BuildFallback(string claude, string shell)
    {
        return shell switch
        {
            "pwsh" => new ProcessStartInfo(FindPowerShell(), "-NoExit -Command claude"),
            "direct" when claude.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                => new ProcessStartInfo(claude),
            _ => new ProcessStartInfo("cmd.exe", $"/k {Quote(claude)}")
        };
    }

    private static string ResolveWorkingDirectory(Settings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.WorkingDirectory) &&
            Directory.Exists(settings.WorkingDirectory))
        {
            return settings.WorkingDirectory;
        }

        return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }

    private static string? ResolveClaude(Settings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.ClaudePath) && File.Exists(settings.ClaudePath))
        {
            return settings.ClaudePath;
        }

        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        var candidates = new[]
        {
            Path.Combine(userProfile, ".local", "bin", "claude.exe"),
            Path.Combine(Path.GetDirectoryName(appData) ?? "", ".local", "bin", "claude.exe"),
            Path.Combine(appData, "npm", "claude.cmd"),
            Path.Combine(localAppData, "Programs", "claude", "claude.exe"),
            Path.Combine(localAppData, "Microsoft", "WinGet", "Links", "claude.exe"),
            Path.Combine(userProfile, "AppData", "Local", "Microsoft", "WindowsApps", "claude.exe")
        };

        foreach (var candidate in candidates)
        {
            if (!string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate))
            {
                Log.Info("Claude found: " + candidate);
                return candidate;
            }
        }

        foreach (var name in new[] { "claude.exe", "claude.cmd", "claude.bat" })
        {
            var fromPath = FindOnPath(name);
            if (fromPath is not null)
            {
                Log.Info("Claude found on PATH: " + fromPath);
                return fromPath;
            }
        }

        Log.Warn("Claude Code was not found");
        return null;
    }

    private static string NormalizeShell(string? shell)
    {
        return shell?.Trim().ToLowerInvariant() switch
        {
            "pwsh" or "powershell" => "pwsh",
            "direct" or "exe" => "direct",
            _ => "cmd"
        };
    }

    private static string FindPowerShell()
        => FindOnPath("pwsh.exe") is not null ? "pwsh.exe" : "powershell.exe";

    private static string? FindWindowsTerminal()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var alias = Path.Combine(localAppData, "Microsoft", "WindowsApps", "wt.exe");
        if (File.Exists(alias))
        {
            return alias;
        }

        return FindOnPath("wt.exe");
    }

    private static string? FindOnPath(string fileName)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        foreach (var raw in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var directory = raw.Trim().Trim('"');
            if (directory.Length == 0)
            {
                continue;
            }

            try
            {
                var candidate = Path.Combine(directory, fileName);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            catch
            {
                // malformed PATH entry
            }
        }

        return null;
    }

    private static string Quote(string value) => $"\"{value}\"";
}

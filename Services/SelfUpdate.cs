using System.Diagnostics;
using System.IO;
using System.Text;

namespace IpGuard;

/// <summary>
/// Replaces the running exe with a downloaded one. A copy cannot be written over a running
/// image, so a small hidden .cmd waits for this process, copies the file and starts it again.
/// </summary>
public static class SelfUpdate
{
    /// <summary>Starts the helper and returns true when the caller may shut down.</summary>
    public static bool TryApplyAndRestart(string newExePath, out string? error)
    {
        error = null;

        try
        {
            if (string.IsNullOrWhiteSpace(newExePath) || !File.Exists(newExePath))
            {
                error = "Файл обновления не найден";
                return false;
            }

            var currentExe = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(currentExe))
            {
                currentExe = StartupService.ExecutablePath;
            }

            var directory = Path.GetDirectoryName(currentExe);
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
            {
                error = "Не удалось найти папку приложения";
                return false;
            }

            // Read-only folder (Program Files, no rights): stop here instead of half-updating.
            if (!CanWrite(directory, out var reason))
            {
                error = reason;
                return false;
            }

            var script = Path.Combine(Path.GetTempPath(), $"ipguard-update-{Guid.NewGuid():N}.cmd");
            File.WriteAllText(script, BuildScript(Environment.ProcessId, currentExe, newExePath),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            var startInfo = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c \"\"{script}\"\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                WorkingDirectory = Path.GetTempPath()
            };

            if (Process.Start(startInfo) is null)
            {
                error = "Не удалось запустить помощник обновления";
                TryDelete(script);
                return false;
            }

            Log.Info($"Self update: helper started (pid {Environment.ProcessId} → {currentExe})");
            return true;
        }
        catch (Exception ex)
        {
            Log.Warn("Self update failed: " + ex.Message);
            error = ex.Message;
            return false;
        }
    }

    private static string BuildScript(int pid, string targetExe, string newExe)
    {
        var builder = new StringBuilder();
        builder.AppendLine("@echo off");
        builder.AppendLine("chcp 65001 >nul");
        builder.AppendLine("setlocal enabledelayedexpansion");
        builder.AppendLine(":wait");
        builder.AppendLine($"tasklist /FI \"PID eq {pid}\" /NH 2>nul | find \"{pid}\" >nul");
        builder.AppendLine("if not errorlevel 1 (");
        builder.AppendLine("  ping 127.0.0.1 -n 2 >nul");
        builder.AppendLine("  goto wait");
        builder.AppendLine(")");
        builder.AppendLine("set /a tries=0");
        builder.AppendLine(":copy");
        builder.AppendLine($"copy /y \"{newExe}\" \"{targetExe}\" >nul 2>&1");
        builder.AppendLine("if errorlevel 1 (");
        builder.AppendLine("  set /a tries+=1");
        builder.AppendLine("  if !tries! lss 30 (");
        builder.AppendLine("    ping 127.0.0.1 -n 2 >nul");
        builder.AppendLine("    goto copy");
        builder.AppendLine("  )");
        builder.AppendLine("  exit /b 1");
        builder.AppendLine(")");
        builder.AppendLine($"start \"\" \"{targetExe}\"");
        builder.AppendLine("del \"%~f0\"");
        builder.AppendLine("endlocal");
        return builder.ToString();
    }

    private static bool CanWrite(string directory, out string reason)
    {
        reason = "";
        var probe = Path.Combine(directory, $".ipguard-write-test-{Guid.NewGuid():N}.tmp");
        try
        {
            using var stream = new FileStream(probe, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 1, FileOptions.DeleteOnClose);
            return true;
        }
        catch (Exception ex)
        {
            reason = "Нет прав на запись в папку приложения: " + ex.Message;
            return false;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex)
        {
            Log.Warn("Self update: cannot delete " + path + ": " + ex.Message);
        }
    }
}

using System.Diagnostics;
using Microsoft.Win32;

namespace IpGuard;

public static class StartupService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "IpGuardWidget";

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
            return key?.GetValue(ValueName) is string value && value.Length > 0;
        }
        catch (Exception ex)
        {
            Log.Warn("Autostart read failed: " + ex.Message);
            return false;
        }
    }

    public static void SetEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true)
                            ?? Registry.CurrentUser.CreateSubKey(RunKey);
            if (key is null)
            {
                return;
            }

            if (enabled)
            {
                key.SetValue(ValueName, $"\"{ExecutablePath}\" --tray");
                Log.Info("Autostart enabled");
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
                Log.Info("Autostart disabled");
            }
        }
        catch (Exception ex)
        {
            Log.Warn("Autostart write failed: " + ex.Message);
        }
    }

    public static string ExecutablePath
    {
        get
        {
            var path = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(path))
            {
                return path;
            }

            using var process = Process.GetCurrentProcess();
            return process.MainModule?.FileName ?? "IpGuardWidget.exe";
        }
    }
}

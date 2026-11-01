using System.IO;

namespace IpGuard;

public static class Log
{
    private static readonly object Gate = new();
    private static readonly string FilePath = Path.Combine(AppPaths.DataDirectory, "ipguard.log");

    public static void Info(string message) => Write("INFO", message);
    public static void Warn(string message) => Write("WARN", message);
    public static void Error(string message) => Write("ERROR", message);

    private static void Write(string level, string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(AppPaths.DataDirectory);
                Rotate();
                File.AppendAllText(FilePath,
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // Logging must never break the widget.
        }
    }

    private static void Rotate()
    {
        var info = new FileInfo(FilePath);
        if (!info.Exists || info.Length < 512 * 1024)
        {
            return;
        }

        var old = Path.Combine(AppPaths.DataDirectory, "ipguard.old.log");
        try
        {
            if (File.Exists(old))
            {
                File.Delete(old);
            }

            File.Move(FilePath, old);
        }
        catch
        {
            // ignored
        }
    }
}

public static class AppPaths
{
    public static string DataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "IpGuardWidget");

    public static string SettingsFile => Path.Combine(DataDirectory, "settings.json");
}

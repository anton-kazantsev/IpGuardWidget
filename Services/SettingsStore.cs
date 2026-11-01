using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IpGuard;

public sealed class Settings
{
    /// <summary>Physical screen X of the window's right edge.</summary>
    public double? AnchorRight { get; set; }

    /// <summary>Physical screen Y of the window's bottom edge.</summary>
    public double? AnchorBottom { get; set; }

    public bool AlwaysOnTop { get; set; } = true;
    public string Shell { get; set; } = "cmd"; // cmd | pwsh | direct
    public string WorkingDirectory { get; set; } = "";
    public string ClaudePath { get; set; } = "";
}

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public Settings Current { get; private set; } = new();

    public void Load()
    {
        try
        {
            if (File.Exists(AppPaths.SettingsFile))
            {
                var json = File.ReadAllText(AppPaths.SettingsFile);
                var loaded = JsonSerializer.Deserialize<Settings>(json, Options);
                if (loaded is not null)
                {
                    Current = loaded;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warn("Cannot read settings: " + ex.Message);
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(AppPaths.DataDirectory);
            File.WriteAllText(AppPaths.SettingsFile, JsonSerializer.Serialize(Current, Options));
        }
        catch (Exception ex)
        {
            Log.Warn("Cannot save settings: " + ex.Message);
        }
    }
}

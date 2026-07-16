using System.IO;
using System.Text.Json;

namespace LyricFloat.Settings;

public sealed class AppSettings
{
    // Overlay position & look
    public double X { get; set; } = double.NaN;
    public double Y { get; set; } = double.NaN;
    public double PanelWidth { get; set; } = 440;
    public double PanelOpacity { get; set; } = 0.88;
    public double FontSize { get; set; } = 17;
    public int LineCount { get; set; } = 3;          // visible lines (odd numbers look best)
    public string AccentColor { get; set; } = "#E7C87A"; // champagne gold
    public bool AnimateTransitions { get; set; } = true;

    // Sync
    public int OffsetMs { get; set; } = 0;

    // Behaviour
    public bool AutoStart { get; set; } = false;
    public bool ShowAlbumArt { get; set; } = true;

    // Spotify (Premium enrichment - optional)
    public string SpotifyClientId { get; set; } = string.Empty;
    public string? SpotifyRefreshToken { get; set; }
}

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };
    private readonly string _path;

    public SettingsStore()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LyricFloat");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "settings.json");
    }

    public AppSettings Load()
    {
        try
        {
            if (File.Exists(_path))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path)) ?? new();
        }
        catch { /* corrupted file -> fresh defaults */ }
        return new AppSettings();
    }

    public void Save(AppSettings settings)
    {
        try { File.WriteAllText(_path, JsonSerializer.Serialize(settings, JsonOpts)); }
        catch { }
    }
}

public static class AutoStartHelper
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "LyricFloat";

    public static void Apply(bool enabled)
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        if (key is null) return;
        if (enabled)
            key.SetValue(AppName, $"\"{Environment.ProcessPath}\"");
        else
            key.DeleteValue(AppName, throwOnMissingValue: false);
    }
}

using System.IO;

namespace LyricFloat;

/// <summary>Lightweight debug log at %APPDATA%\LyricFloat\debug.log (trimmed at ~500 KB).</summary>
public static class Log
{
    private static readonly object Gate = new();
    private static readonly string FilePath = Init();

    private static string Init()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LyricFloat");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "debug.log");
    }

    public static void Write(string message)
    {
        try
        {
            lock (Gate)
            {
                if (File.Exists(FilePath) && new FileInfo(FilePath).Length > 500_000)
                    File.Delete(FilePath);
                File.AppendAllText(FilePath, $"{DateTime.Now:HH:mm:ss.fff}  {message}{Environment.NewLine}");
            }
        }
        catch { }
    }
}

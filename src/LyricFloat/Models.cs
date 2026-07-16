namespace LyricFloat;

public enum SyncType { Synced, Plain, NotFound }

public sealed record LyricLine(int TimeMs, string Text);

public sealed record LyricSet(SyncType Type, IReadOnlyList<LyricLine> Lines, string? PlainText)
{
    public static readonly LyricSet NotFound = new(SyncType.NotFound, Array.Empty<LyricLine>(), null);
}

public sealed record TrackInfo(string Title, string Artist, string Album, int DurationMs)
{
    public string Key => $"{Artist}|{Title}|{DurationMs / 1000}";
}

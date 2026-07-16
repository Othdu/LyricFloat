using System.Windows.Threading;
using LyricFloat.Media;

namespace LyricFloat.Sync;

/// <summary>
/// Ticks at ~60ms while playing, computes the interpolated playback position
/// and raises ActiveLineChanged only when the highlighted line index changes.
/// </summary>
public sealed class SyncEngine
{
    private readonly PositionInterpolator _position;
    private readonly DispatcherTimer _timer;
    private LyricSet _set = LyricSet.NotFound;
    private int _index = int.MinValue;

    /// <summary>Global user-adjustable offset in ms (positive = lyrics later).</summary>
    public int OffsetMs { get; set; }

    /// <summary>Raised on the UI thread. -1 means "before the first line".</summary>
    public event Action<int>? ActiveLineChanged;

    public SyncEngine(PositionInterpolator position)
    {
        _position = position;
        _timer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(60)
        };
        _timer.Tick += (_, _) => Tick();
    }

    public void SetLyrics(LyricSet set)
    {
        _set = set;
        _index = int.MinValue;
        Tick();
    }

    public void SetPlaying(bool playing)
    {
        if (playing) _timer.Start();
        else _timer.Stop();
    }

    private void Tick()
    {
        if (_set.Type != SyncType.Synced || _set.Lines.Count == 0) return;

        var ms = (int)_position.Current.TotalMilliseconds - OffsetMs;
        var idx = FindIndex(_set.Lines, ms);
        if (idx == _index) return;

        _index = idx;
        ActiveLineChanged?.Invoke(idx);
    }

    /// <summary>Binary search: index of the last line with TimeMs &lt;= ms, or -1.</summary>
    private static int FindIndex(IReadOnlyList<LyricLine> lines, int ms)
    {
        int lo = 0, hi = lines.Count - 1, ans = -1;
        while (lo <= hi)
        {
            var mid = (lo + hi) / 2;
            if (lines[mid].TimeMs <= ms) { ans = mid; lo = mid + 1; }
            else hi = mid - 1;
        }
        return ans;
    }
}

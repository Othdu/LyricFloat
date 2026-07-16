namespace LyricFloat.Media;

/// <summary>
/// Spotify updates the SMTC timeline in coarse steps (every few seconds).
/// This class anchors the last known (position, timestamp) pair and
/// interpolates the current position with a wall clock while playing.
/// The Spotify Web API enrichment can push a more precise anchor on top.
/// </summary>
public sealed class PositionInterpolator
{
    private readonly object _lock = new();
    private TimeSpan _anchorPos;
    private DateTimeOffset _anchorAt = DateTimeOffset.UtcNow;
    private bool _playing;

    public void SetAnchor(TimeSpan position, DateTimeOffset at)
    {
        lock (_lock)
        {
            _anchorPos = position;
            _anchorAt = at;
        }
    }

    public void SetPlaying(bool playing)
    {
        lock (_lock)
        {
            if (_playing && !playing)
            {
                // Freeze position at pause time.
                _anchorPos = CurrentUnsafe();
                _anchorAt = DateTimeOffset.UtcNow;
            }
            _playing = playing;
        }
    }

    public TimeSpan Current
    {
        get { lock (_lock) return CurrentUnsafe(); }
    }

    private TimeSpan CurrentUnsafe() =>
        _playing ? _anchorPos + (DateTimeOffset.UtcNow - _anchorAt) : _anchorPos;
}

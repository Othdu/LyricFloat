using Windows.Media.Control;

namespace LyricFloat.Media;

/// <summary>
/// Watches Windows System Media Transport Controls (SMTC) for the current
/// track, playback state and timeline position. Prefers the Spotify session.
///
/// Important quirk this class handles: when the user SKIPS a track, Spotify
/// raises MediaPropertiesChanged with the new title while the timeline still
/// holds the OLD track's duration (or zero). We therefore (a) wait briefly
/// for metadata to settle before emitting, and (b) re-emit the track if the
/// duration later corrects itself by more than 3 seconds, so lyrics lookups
/// that failed due to a stale duration are retried automatically.
/// </summary>
public sealed class MediaWatcher
{
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _session;
    private string? _lastTrackKey;            // "artist|title" (duration excluded on purpose)
    private int _lastEmittedDurationMs;

    public event Action<TrackInfo>? TrackChanged;
    public event Action<TimeSpan, DateTimeOffset>? PositionAnchor;
    public event Action<bool>? PlaybackStateChanged;
    public event Action? NoSession;
    public event Action? SessionAvailable;

    public async Task StartAsync()
    {
        _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        _manager.SessionsChanged += (_, _) => PickSession();
        PickSession();
    }

    private void PickSession()
    {
        if (_manager is null) return;

        GlobalSystemMediaTransportControlsSession? next = null;
        try
        {
            foreach (var s in _manager.GetSessions())
            {
                var id = s.SourceAppUserModelId ?? string.Empty;
                if (id.Contains("Spotify", StringComparison.OrdinalIgnoreCase))
                {
                    next = s;
                    break;
                }
            }
            next ??= _manager.GetCurrentSession();
        }
        catch
        {
            // Session list can be torn down mid-enumeration; retry on next event.
        }

        Detach();
        _session = next;

        if (_session is null)
        {
            _lastTrackKey = null;
            NoSession?.Invoke();
            return;
        }

        _session.MediaPropertiesChanged += OnMediaPropertiesChanged;
        _session.PlaybackInfoChanged += OnPlaybackInfoChanged;
        _session.TimelinePropertiesChanged += OnTimelinePropertiesChanged;

        SessionAvailable?.Invoke();

        _ = RefreshTrackAsync();
        PushTimeline();
        PushPlayback();
    }

    private void Detach()
    {
        if (_session is null) return;
        try
        {
            _session.MediaPropertiesChanged -= OnMediaPropertiesChanged;
            _session.PlaybackInfoChanged -= OnPlaybackInfoChanged;
            _session.TimelinePropertiesChanged -= OnTimelinePropertiesChanged;
        }
        catch { /* session may already be disposed */ }
        _session = null;
    }

    private void OnMediaPropertiesChanged(
        GlobalSystemMediaTransportControlsSession sender, MediaPropertiesChangedEventArgs args)
        => _ = RefreshTrackAsync();

    private void OnPlaybackInfoChanged(
        GlobalSystemMediaTransportControlsSession sender, PlaybackInfoChangedEventArgs args)
        => PushPlayback();

    private void OnTimelinePropertiesChanged(
        GlobalSystemMediaTransportControlsSession sender, TimelinePropertiesChangedEventArgs args)
    {
        PushTimeline();
        CheckDurationCorrection();
    }

    private async Task RefreshTrackAsync()
    {
        var session = _session;
        if (session is null) return;
        try
        {
            var props = await session.TryGetMediaPropertiesAsync();
            if (props is null || string.IsNullOrWhiteSpace(props.Title)) return;

            var key = $"{props.Artist}|{props.Title}";
            if (key == _lastTrackKey) return;
            _lastTrackKey = key;

            Log.Write($"Track event: {props.Artist} - {props.Title} (settling...)");

            // Let Spotify finish switching the timeline to the new track.
            await Task.Delay(400);

            // Re-read; if yet another track started meanwhile, its own event handles it.
            var confirm = await session.TryGetMediaPropertiesAsync();
            if (confirm is null || string.IsNullOrWhiteSpace(confirm.Title)) return;
            if ($"{confirm.Artist}|{confirm.Title}" != key) return;

            var timeline = session.GetTimelineProperties();
            var durationMs = (int)Math.Max(0, timeline.EndTime.TotalMilliseconds);
            _lastEmittedDurationMs = durationMs;

            Log.Write($"Track emit: {confirm.Artist} - {confirm.Title} [{durationMs / 1000}s]");

            TrackChanged?.Invoke(new TrackInfo(
                confirm.Title ?? string.Empty,
                confirm.Artist ?? string.Empty,
                confirm.AlbumTitle ?? string.Empty,
                durationMs));

            PushTimeline();
        }
        catch (Exception ex) { Log.Write($"RefreshTrack error: {ex.Message}"); }
    }

    /// <summary>
    /// If the timeline's duration settles to a materially different value than
    /// what we emitted (stale-duration skip race), re-emit the same track so
    /// the lyrics lookup runs again with the correct duration.
    /// </summary>
    private void CheckDurationCorrection()
    {
        var session = _session;
        if (session is null || _lastTrackKey is null) return;
        try
        {
            var t = session.GetTimelineProperties();
            var dur = (int)t.EndTime.TotalMilliseconds;
            if (dur <= 0 || Math.Abs(dur - _lastEmittedDurationMs) <= 3000) return;

            _lastEmittedDurationMs = dur;
            _ = ReEmitAsync(dur);
        }
        catch { }
    }

    private async Task ReEmitAsync(int durationMs)
    {
        var session = _session;
        if (session is null) return;
        try
        {
            // At track transitions Spotify updates the DURATION before the
            // TITLE, so an immediate re-emit would attach the next song's
            // duration to the old song. Wait for the transition to settle,
            // then only re-emit if the same track is genuinely still playing
            // with that corrected duration.
            await Task.Delay(1200);

            var settled = session.GetTimelineProperties();
            var settledDur = (int)settled.EndTime.TotalMilliseconds;
            if (Math.Abs(settledDur - durationMs) > 2000) return; // still in flux
            durationMs = settledDur;
            _lastEmittedDurationMs = settledDur;

            var props = await session.TryGetMediaPropertiesAsync();
            if (props is null || string.IsNullOrWhiteSpace(props.Title)) return;
            if ($"{props.Artist}|{props.Title}" != _lastTrackKey) return;

            Log.Write($"Duration corrected -> re-emit: {props.Artist} - {props.Title} [{durationMs / 1000}s]");

            TrackChanged?.Invoke(new TrackInfo(
                props.Title!, props.Artist ?? string.Empty,
                props.AlbumTitle ?? string.Empty, durationMs));
        }
        catch { }
    }

    private void PushTimeline()
    {
        var session = _session;
        if (session is null) return;
        try
        {
            var t = session.GetTimelineProperties();
            PositionAnchor?.Invoke(t.Position, t.LastUpdatedTime);
        }
        catch { }
    }

    private void PushPlayback()
    {
        var session = _session;
        if (session is null) return;
        try
        {
            var playing = session.GetPlaybackInfo().PlaybackStatus
                == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            PlaybackStateChanged?.Invoke(playing);
        }
        catch { }
    }
}

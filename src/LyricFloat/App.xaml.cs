using System.Windows;
using System.Windows.Threading;
using LyricFloat.Data;
using LyricFloat.Lyrics;
using LyricFloat.Media;
using LyricFloat.Overlay;
using LyricFloat.Settings;
using LyricFloat.Spotify;
using LyricFloat.Sync;
using LyricFloat.Tray;

namespace LyricFloat;

public partial class App : Application
{
    private Mutex? _singleInstance;

    private SettingsStore _store = null!;
    private AppSettings _settings = null!;
    private CacheDb _cache = null!;
    private PositionInterpolator _interpolator = null!;
    private MediaWatcher _watcher = null!;
    private LyricsService _lyrics = null!;
    private SyncEngine _sync = null!;
    private SpotifyEnrichment _spotify = null!;
    private OverlayViewModel _vm = null!;
    private OverlayWindow _overlay = null!;
    private TrayIconController _tray = null!;
    private HotkeyManager? _hotkeys;
    private DispatcherTimer? _idleHide;

    private int _fetchToken;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstance = new Mutex(true, "LyricFloat_SingleInstance", out var isNew);
        if (!isNew) { Shutdown(); return; }

        _store = new SettingsStore();
        _settings = _store.Load();
        _cache = new CacheDb();
        _interpolator = new PositionInterpolator();
        _lyrics = new LyricsService(_cache);
        _sync = new SyncEngine(_interpolator) { OffsetMs = _settings.OffsetMs };
        _spotify = new SpotifyEnrichment(_settings, _store, _interpolator);
        _vm = new OverlayViewModel(_settings);
        _overlay = new OverlayWindow(_vm, _settings, _store);
        _tray = new TrayIconController();
        _watcher = new MediaWatcher();

        WireEvents();

        _overlay.Show();
        _hotkeys = new HotkeyManager(_overlay);
        _hotkeys.Pressed += OnHotkey;

        // Warm up the LRCLIB connection (DNS + TLS) before the first lookup,
        // and keep it warm - cold connects are very slow on some routes.
        Lyrics.LrclibClient.KeepAlive();
        var keepAlive = new DispatcherTimer { Interval = TimeSpan.FromMinutes(3) };
        keepAlive.Tick += (_, _) => Lyrics.LrclibClient.KeepAlive();
        keepAlive.Start();

        await _watcher.StartAsync();
        await _spotify.StartAsync();
    }

    private void WireEvents()
    {
        // ---- SMTC (background threads -> UI dispatcher) ----
        _watcher.TrackChanged += track => Dispatcher.Invoke(() => OnTrackChanged(track));

        _watcher.PositionAnchor += (pos, at) => _interpolator.SetAnchor(pos, at);

        _watcher.PlaybackStateChanged += playing => Dispatcher.Invoke(() =>
        {
            _interpolator.SetPlaying(playing);
            _sync.SetPlaying(playing);
            if (playing) CancelIdleHide();
        });

        _watcher.NoSession += () => Dispatcher.Invoke(StartIdleHide);

        // CRITICAL: other apps opening/closing media sessions (browser video,
        // game launchers, Discord) shuffle the SMTC session list. If Spotify's
        // session is momentarily unreadable we may schedule an idle-hide - so
        // the moment a session is (re)attached or playback resumes, cancel it
        // and bring the panel back. Without this the overlay hides itself and
        // never returns until the next track change.
        _watcher.SessionAvailable += () => Dispatcher.Invoke(CancelIdleHide);

        // ---- Sync engine -> overlay ----
        _sync.ActiveLineChanged += index => _vm.SetActiveIndex(index);

        // ---- Spotify enrichment -> overlay ----
        _spotify.AlbumArtChanged += url => Dispatcher.Invoke(() => _vm.SetAlbumArt(url));

        // ---- Tray ----
        _tray.ToggleOverlayRequested += ToggleOverlay;
        _tray.EditModeChanged += on => _overlay.EditMode = on;
        _tray.SettingsRequested += OpenSettings;
        _tray.ExitRequested += () =>
        {
            _overlay.SavePosition();
            Shutdown();
        };
    }

    private string? _displayedTrackKey;
    private bool _displayedSynced;
    private string? _fetchingTrackKey;
    private bool _prefetching;

    private async void OnTrackChanged(TrackInfo track)
    {
        CancelIdleHide();

        // Spotify Free plays ads with no artist / "Advertisement" titles.
        if (IsAd(track)) { _vm.HideForAd(); return; }

        // A duration-correction re-emit for a track whose synced lyrics are
        // already on screen: nothing to gain, don't clear a working display.
        var trackKey = $"{track.Artist}|{track.Title}";
        if (trackKey == _displayedTrackKey && _displayedSynced)
        {
            Log.Write("Re-emit for already-synced track -> ignored");
            return;
        }

        // A lookup for this exact track is already in flight: don't double
        // the request load (it congests slow connections badly).
        if (trackKey == _fetchingTrackKey)
        {
            Log.Write("Lookup already in flight for this track -> ignored");
            return;
        }
        _fetchingTrackKey = trackKey;

        var token = Interlocked.Increment(ref _fetchToken);
        _vm.SetTrackLoading(track);
        _sync.SetLyrics(LyricSet.NotFound);

        var set = await _lyrics.GetAsync(track);

        // A newer track started while we were fetching: discard stale result.
        if (token != _fetchToken) return;

        if (set.Type == SyncType.NotFound)
        {
            // Don't give up yet: transient network errors and skip races are
            // common. Keep the "artist - title" display, wait, retry once
            // bypassing the cache, then accept the verdict.
            Log.Write("First lookup empty -> retrying in 4s");
            await Task.Delay(4000);
            if (token != _fetchToken) return;

            set = await _lyrics.GetAsync(track, bypassCache: true);
            if (token != _fetchToken) return;
        }

        _vm.SetLyricSet(set);
        _sync.SetLyrics(set);
        _displayedTrackKey = trackKey;
        _displayedSynced = set.Type == SyncType.Synced;
        _fetchingTrackKey = null;

        // Warm the cache for whatever Spotify says is coming next, so the
        // next track change shows lyrics instantly even on a slow route.
        _ = PrefetchUpcomingAsync();
    }

    private async Task PrefetchUpcomingAsync()
    {
        if (_prefetching || !_spotify.IsConnected) return;
        _prefetching = true;
        try
        {
            var upcoming = await _spotify.GetUpcomingAsync(take: 2);
            foreach (var next in upcoming)
            {
                if (IsAd(next)) continue;
                Log.Write($"Prefetch: {next.Artist} - {next.Title}");
                await _lyrics.GetAsync(next);   // sequential - gentle on the link
            }
        }
        catch { }
        finally { _prefetching = false; }
    }

    private static bool IsAd(TrackInfo t) =>
        string.IsNullOrWhiteSpace(t.Artist) ||
        t.Title.Equals("Advertisement", StringComparison.OrdinalIgnoreCase) ||
        t.Title.Equals("Spotify", StringComparison.OrdinalIgnoreCase);

    private void OnHotkey(Hotkey hk)
    {
        switch (hk)
        {
            case Hotkey.ToggleOverlay:
                ToggleOverlay();
                break;
            case Hotkey.ToggleEditMode:
                _overlay.EditMode = !_overlay.EditMode;
                _tray.SetEditMode(_overlay.EditMode);
                break;
            case Hotkey.OffsetPlus:
                _sync.OffsetMs += 250;
                _settings.OffsetMs = _sync.OffsetMs;
                _store.Save(_settings);
                break;
            case Hotkey.OffsetMinus:
                _sync.OffsetMs -= 250;
                _settings.OffsetMs = _sync.OffsetMs;
                _store.Save(_settings);
                break;
            case Hotkey.OpenSettings:
                OpenSettings();
                break;
        }
    }

    private void ToggleOverlay()
        => _overlay.Visibility = _overlay.Visibility == Visibility.Visible
            ? Visibility.Hidden
            : Visibility.Visible;

    private void OpenSettings()
    {
        var win = new SettingsWindow(_settings, _store, _spotify, applyChanges: () =>
        {
            _sync.OffsetMs = _settings.OffsetMs;
            _vm.RefreshFromSettings();
        });
        win.Show();
        win.Activate();
    }

    private void StartIdleHide()
    {
        // No media session (Spotify closed): hide after 15s of silence.
        // (Handler attached ONCE - the old += -on-every-call leaked handlers.)
        if (_idleHide is null)
        {
            _idleHide = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
            _idleHide.Tick += (_, _) =>
            {
                _idleHide!.Stop();
                Log.Write("Idle-hide: no media session for 15s -> hiding panel");
                _vm.HideForAd();
            };
        }
        _idleHide.Stop();
        _idleHide.Start();
    }

    private void CancelIdleHide()
    {
        _idleHide?.Stop();
        _vm.PanelVisibility = Visibility.Visible;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _hotkeys?.Dispose();
        _tray?.Dispose();
        _cache?.Dispose();
        _singleInstance?.ReleaseMutex();
        base.OnExit(e);
    }
}

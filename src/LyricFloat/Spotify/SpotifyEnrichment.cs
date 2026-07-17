using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Windows.Threading;
using LyricFloat.Media;
using LyricFloat.Settings;

namespace LyricFloat.Spotify;

/// <summary>
/// Optional Premium enrichment on top of the SMTC core:
///  - Polls /v1/me/player/currently-playing every 5s while connected
///  - Pushes a precise progress_ms anchor into the PositionInterpolator
///    (SMTC's timeline is coarse; the Web API is millisecond-accurate)
///  - Surfaces the album art URL for the overlay thumbnail
/// SMTC keeps working as the source of truth if this is disconnected.
/// </summary>
public sealed class SpotifyEnrichment
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };

    private readonly AppSettings _settings;
    private readonly SettingsStore _store;
    private readonly PositionInterpolator _position;
    private readonly SpotifyAuth _auth = new();
    private readonly DispatcherTimer _timer;
    private SpotifyTokens? _tokens;
    private string? _lastArtUrl;

    public bool IsConnected => _tokens is not null;

    /// <summary>Raised on the UI thread with the album art URL (or null).</summary>
    public event Action<string?>? AlbumArtChanged;
    public event Action<bool>? ConnectionChanged;

    public SpotifyEnrichment(AppSettings settings, SettingsStore store, PositionInterpolator position)
    {
        _settings = settings;
        _store = store;
        _position = position;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _timer.Tick += async (_, _) => await PollAsync();
    }

    public async Task StartAsync()
    {
        if (string.IsNullOrWhiteSpace(_settings.SpotifyClientId) ||
            string.IsNullOrWhiteSpace(_settings.SpotifyRefreshToken))
        {
            Log.Write("Spotify: no stored connection");
            return;
        }

        await TrySilentReconnectAsync();

        // Flaky-network resilience: keep retrying the silent reconnect every
        // 2 minutes until it works. A timeout at launch must never mean
        // "no Spotify for this whole session".
        if (_tokens is null)
        {
            var retry = new DispatcherTimer { Interval = TimeSpan.FromMinutes(2) };
            retry.Tick += async (_, _) =>
            {
                if (_tokens is not null ||
                    string.IsNullOrWhiteSpace(_settings.SpotifyRefreshToken))
                {
                    retry.Stop();
                    return;
                }
                await TrySilentReconnectAsync();
                if (_tokens is not null) retry.Stop();
            };
            retry.Start();
        }
    }

    private async Task TrySilentReconnectAsync()
    {
        try
        {
            _tokens = await _auth.RefreshAsync(_settings.SpotifyClientId, _settings.SpotifyRefreshToken!);
            if (_tokens is not null)
            {
                PersistRefreshToken();
                _timer.Start();
                ConnectionChanged?.Invoke(true);
                Log.Write("Spotify: connected (silent reconnect)");
            }
            else
            {
                // Definitive rejection - the token really is dead.
                Log.Write("Spotify: stored token rejected - reconnect via Settings");
                _settings.SpotifyRefreshToken = null;
                _store.Save(_settings);
            }
        }
        catch (Exception ex)
        {
            // Network trouble: keep the token, retry later.
            Log.Write($"Spotify: reconnect failed (network: {ex.Message.Split('(')[0].Trim()}) - will retry");
        }
    }

    /// <summary>Interactive connect (opens the browser). Call from the UI.</summary>
    public async Task<bool> ConnectAsync()
    {
        if (string.IsNullOrWhiteSpace(_settings.SpotifyClientId)) return false;

        _tokens = await _auth.AuthorizeAsync(_settings.SpotifyClientId);
        if (_tokens is null) return false;

        PersistRefreshToken();
        _timer.Start();
        ConnectionChanged?.Invoke(true);
        await PollAsync();
        return true;
    }

    public void Disconnect()
    {
        _timer.Stop();
        _tokens = null;
        _settings.SpotifyRefreshToken = null;
        _store.Save(_settings);
        AlbumArtChanged?.Invoke(null);
        ConnectionChanged?.Invoke(false);
    }

    private void PersistRefreshToken()
    {
        _settings.SpotifyRefreshToken = _tokens!.RefreshToken;
        _store.Save(_settings);
    }

    /// <summary>
    /// Upcoming tracks from the user's Spotify queue (Premium). Used to
    /// prefetch lyrics into the cache while the current song plays, so
    /// track changes show lyrics instantly even on slow connections.
    /// </summary>
    public async Task<List<TrackInfo>> GetUpcomingAsync(int take = 2)
    {
        var result = new List<TrackInfo>();
        if (_tokens is null) return result;
        try
        {
            if (_tokens.IsExpired)
            {
                SpotifyTokens? refreshed = null;
                try { refreshed = await _auth.RefreshAsync(_settings.SpotifyClientId, _tokens.RefreshToken); }
                catch { return result; }   // network hiccup - keep the token
                if (refreshed is null) return result;
                _tokens = refreshed;
                PersistRefreshToken();
            }

            using var req = new HttpRequestMessage(
                HttpMethod.Get, "https://api.spotify.com/v1/me/player/queue");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _tokens.AccessToken);
            using var resp = await Http.SendAsync(req);
            if (!resp.IsSuccessStatusCode) return result;

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            if (!doc.RootElement.TryGetProperty("queue", out var queue)) return result;

            foreach (var item in queue.EnumerateArray())
            {
                if (result.Count >= take) break;
                if (!item.TryGetProperty("name", out var name)) continue;

                var artists = item.TryGetProperty("artists", out var arr)
                    ? string.Join(", ", arr.EnumerateArray()
                        .Select(a => a.GetProperty("name").GetString()))
                    : string.Empty;
                var album = item.TryGetProperty("album", out var al) &&
                            al.TryGetProperty("name", out var alName)
                    ? alName.GetString() ?? string.Empty : string.Empty;
                var durationMs = item.TryGetProperty("duration_ms", out var d)
                    ? (int)d.GetInt64() : 0;

                result.Add(new TrackInfo(name.GetString() ?? "", artists, album, durationMs));
            }
        }
        catch { }
        return result;
    }

    private async Task PollAsync()
    {
        if (_tokens is null) return;

        try
        {
            if (_tokens.IsExpired)
            {
                SpotifyTokens? refreshed;
                try
                {
                    refreshed = await _auth.RefreshAsync(_settings.SpotifyClientId, _tokens.RefreshToken);
                }
                catch
                {
                    // Network failure - NOT a revoked token. Keep everything
                    // and try again on the next poll tick. (A previous version
                    // called Disconnect() here, which permanently deleted the
                    // refresh token on any flaky-connection hiccup.)
                    return;
                }

                if (refreshed is null)
                {
                    Log.Write("Spotify: token rejected during refresh - disconnected");
                    Disconnect();
                    return;
                }
                _tokens = refreshed;
                PersistRefreshToken();
            }

            using var req = new HttpRequestMessage(
                HttpMethod.Get, "https://api.spotify.com/v1/me/player/currently-playing");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _tokens.AccessToken);

            var sentAt = DateTimeOffset.UtcNow;
            using var resp = await Http.SendAsync(req);

            if (resp.StatusCode == HttpStatusCode.NoContent) return;   // nothing playing
            if (resp.StatusCode == HttpStatusCode.Unauthorized) { _tokens = null; return; }
            if (!resp.IsSuccessStatusCode) return;

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            var root = doc.RootElement;

            var isPlaying = root.TryGetProperty("is_playing", out var p) && p.GetBoolean();
            if (isPlaying && root.TryGetProperty("progress_ms", out var prog))
            {
                // Compensate for half the round-trip latency.
                var rtt = DateTimeOffset.UtcNow - sentAt;
                _position.SetAnchor(
                    TimeSpan.FromMilliseconds(prog.GetInt64()) + rtt / 2,
                    DateTimeOffset.UtcNow);
            }

            string? artUrl = null;
            if (root.TryGetProperty("item", out var item) &&
                item.ValueKind == JsonValueKind.Object &&
                item.TryGetProperty("album", out var album) &&
                album.TryGetProperty("images", out var images) &&
                images.GetArrayLength() > 0)
            {
                // Smallest image is plenty for a 44px thumbnail.
                artUrl = images[images.GetArrayLength() - 1].GetProperty("url").GetString();
            }

            if (artUrl != _lastArtUrl)
            {
                _lastArtUrl = artUrl;
                AlbumArtChanged?.Invoke(artUrl);
            }
        }
        catch
        {
            // Network hiccups: SMTC keeps the app working; try again next tick.
        }
    }
}

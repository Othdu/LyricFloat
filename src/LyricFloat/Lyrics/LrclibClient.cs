using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace LyricFloat.Lyrics;

public sealed class LrclibRecord
{
    [JsonPropertyName("trackName")] public string? TrackName { get; set; }
    [JsonPropertyName("artistName")] public string? ArtistName { get; set; }
    [JsonPropertyName("duration")] public double? DurationSeconds { get; set; }
    [JsonPropertyName("syncedLyrics")] public string? SyncedLyrics { get; set; }
    [JsonPropertyName("plainLyrics")] public string? PlainLyrics { get; set; }

    [JsonIgnore]
    public int DurationSec => DurationSeconds is null ? 0 : (int)Math.Round(DurationSeconds.Value);
}

public sealed class LrclibClient
{
    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        // High-latency-friendly setup: long request timeout (cold connects to
        // lrclib.net can take 10s+ on some routes), and a long pooled-connection
        // idle timeout so the warmed-up connection survives between songs.
        var handler = new SocketsHttpHandler
        {
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(10),
            ConnectTimeout = TimeSpan.FromSeconds(15),
            AutomaticDecompression = System.Net.DecompressionMethods.All,
        };
        var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
        // LRCLIB asks clients to identify themselves.
        http.DefaultRequestHeaders.UserAgent.ParseAdd("LyricFloat/1.0 (https://github.com/Othdu/LyricFloat)");
        return http;
    }

    /// <summary>
    /// Fire-and-forget tiny request that performs DNS + TLS + HTTP warm-up so
    /// the first real lyrics lookup doesn't pay the cold-connection tax.
    /// Called at startup and periodically to keep the pooled connection alive
    /// (home routers often kill idle TCP connections after a few minutes).
    /// </summary>
    public static void KeepAlive()
    {
        _ = Task.Run(async () =>
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Head, "https://lrclib.net/");
                using var resp = await Http.SendAsync(req);
                // success is the normal case - don't spam the log
            }
            catch (Exception ex)
            {
                Log.Write($"keepalive failed: {ex.Message.Split('(')[0].Trim()}");
            }
        });
    }

    /// <summary>Exact match endpoint. Returns null on 404.</summary>
    public async Task<LrclibRecord?> GetAsync(
        string artist, string title, string album, int durationSec, CancellationToken ct)
    {
        var url = "https://lrclib.net/api/get" +
                  $"?artist_name={Uri.EscapeDataString(artist)}" +
                  $"&track_name={Uri.EscapeDataString(title)}" +
                  $"&album_name={Uri.EscapeDataString(album)}" +
                  $"&duration={durationSec}";

        using var resp = await Http.GetAsync(url, ct);
        if (resp.StatusCode == HttpStatusCode.NotFound) return null;
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<LrclibRecord>(cancellationToken: ct);
    }

    /// <summary>Fuzzy search; picks the best candidate by duration proximity.</summary>
    public async Task<LrclibRecord?> SearchBestAsync(
        string artist, string title, int durationSec, CancellationToken ct)
    {
        var url = "https://lrclib.net/api/search" +
                  $"?artist_name={Uri.EscapeDataString(artist)}" +
                  $"&track_name={Uri.EscapeDataString(title)}";

        List<LrclibRecord>? list;
        try
        {
            list = await Http.GetFromJsonAsync<List<LrclibRecord>>(url, ct);
        }
        catch (HttpRequestException)
        {
            return null;
        }
        if (list is null || list.Count == 0) return null;

        var usable = list
            .Where(r => !string.IsNullOrWhiteSpace(r.SyncedLyrics) ||
                        !string.IsNullOrWhiteSpace(r.PlainLyrics))
            .ToList();
        if (usable.Count == 0) return null;

        return PickBest(usable, durationSec);
    }

    /// <summary>Free-text search ("artist title" in one query) - last-resort fallback.</summary>
    public async Task<LrclibRecord?> SearchQueryAsync(string query, int durationSec, CancellationToken ct)
    {
        var url = $"https://lrclib.net/api/search?q={Uri.EscapeDataString(query)}";

        List<LrclibRecord>? list;
        try
        {
            list = await Http.GetFromJsonAsync<List<LrclibRecord>>(url, ct);
        }
        catch (HttpRequestException)
        {
            return null;
        }
        if (list is null || list.Count == 0) return null;

        var usable = list
            .Where(r => !string.IsNullOrWhiteSpace(r.SyncedLyrics) ||
                        !string.IsNullOrWhiteSpace(r.PlainLyrics))
            .ToList();
        return usable.Count == 0 ? null : PickBest(usable, durationSec);
    }

    /// <summary>
    /// Synced lyrics first; duration proximity is a tie-breaker, never a hard
    /// filter (the OS-reported duration can be stale right after a skip).
    /// </summary>
    private static LrclibRecord? PickBest(List<LrclibRecord> usable, int durationSec)
    {
        return usable
            .OrderByDescending(r => !string.IsNullOrWhiteSpace(r.SyncedLyrics))
            .ThenBy(r => durationSec > 0 ? Math.Abs(r.DurationSec - durationSec) : 0)
            .First();
    }
}

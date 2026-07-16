using LyricFloat.Data;

namespace LyricFloat.Lyrics;

/// <summary>
/// Lookup pipeline: SQLite cache -> LRCLIB exact -> LRCLIB field search
/// -> LRCLIB free-text search -> not found.
/// Every online step is individually fault-isolated so one failure can't
/// kill the rest. Negative results are only cached when we had a trustworthy
/// duration, and expire after 7 days.
/// </summary>
public sealed class LyricsService
{
    private static readonly TimeSpan NotFoundTtl = TimeSpan.FromHours(2);

    private readonly CacheDb _cache;
    private readonly LrclibClient _client = new();

    public LyricsService(CacheDb cache) => _cache = cache;

    public async Task<LyricSet> GetAsync(TrackInfo track, bool bypassCache = false, CancellationToken ct = default)
    {
        var durationSec = track.DurationMs / 1000;
        var artistNorm = TitleNormalizer.PrimaryArtist(track.Artist);
        var titleNorm = TitleNormalizer.Normalize(track.Title);

        Log.Write($"Lyrics lookup: '{artistNorm}' - '{titleNorm}' [{durationSec}s]");

        // 1. Cache
        var cached = bypassCache ? null : _cache.Get(artistNorm, titleNorm, durationSec);
        if (cached is not null)
        {
            if (cached.SyncTypeText == "not_found")
            {
                if (DateTime.UtcNow - cached.FetchedAtUtc < NotFoundTtl)
                {
                    Log.Write("  cache: not_found (within TTL)");
                    return LyricSet.NotFound;
                }
                // TTL expired -> fall through and retry online.
            }
            else
            {
                Log.Write($"  cache hit: {cached.SyncTypeText}");
                return ToLyricSet(cached.SyncTypeText, cached.Body);
            }
        }

        // 2+3. Run the exact-match lookups and the field search IN PARALLEL -
        // sequential requests were painfully slow on high-latency connections.
        // Preference order on completion: get(raw) > get(norm) > search(fields).
        var sw = System.Diagnostics.Stopwatch.StartNew();

        var attempts = new List<Task<LrclibRecord?>>();
        if (durationSec > 0)
        {
            attempts.Add(Try(() => _client.GetAsync(track.Artist, track.Title, track.Album, durationSec, ct), "get(raw)"));
            attempts.Add(Try(() => _client.GetAsync(artistNorm, titleNorm, track.Album, durationSec, ct), "get(norm)"));
        }
        attempts.Add(Try(() => _client.SearchBestAsync(artistNorm, titleNorm, durationSec, ct), "search(fields)"));

        // Return the FIRST successful result - never wait for the slow ones.
        // (Task.WhenAll here meant every lookup lasted as long as the slowest
        // request, i.e. a full timeout, even when another hit in 1 second.)
        LrclibRecord? rec = null;
        var pending = new List<Task<LrclibRecord?>>(attempts);
        while (pending.Count > 0 && rec is null)
        {
            var done = await Task.WhenAny(pending);
            pending.Remove(done);
            rec = await done;
        }

        // 4. Free-text search only if everything above missed
        rec ??= await Try(() => _client.SearchQueryAsync($"{artistNorm} {titleNorm}", durationSec, ct), "search(q)");

        Log.Write($"  lookup took {sw.ElapsedMilliseconds}ms");

        if (rec is null)
        {
            Log.Write("  result: NOT FOUND");
            // Don't poison the cache when the duration was unreliable (skip race).
            if (durationSec > 0)
                _cache.Put(artistNorm, titleNorm, durationSec, "not_found", null);
            return LyricSet.NotFound;
        }

        if (!string.IsNullOrWhiteSpace(rec.SyncedLyrics))
        {
            Log.Write($"  result: SYNCED ('{rec.ArtistName} - {rec.TrackName}' [{rec.DurationSec}s])");
            _cache.Put(artistNorm, titleNorm, durationSec, "synced", rec.SyncedLyrics);
            return ToLyricSet("synced", rec.SyncedLyrics);
        }

        Log.Write("  result: PLAIN (unsynced)");
        _cache.Put(artistNorm, titleNorm, durationSec, "plain", rec.PlainLyrics);
        return ToLyricSet("plain", rec.PlainLyrics);
    }

    private static async Task<LrclibRecord?> Try(Func<Task<LrclibRecord?>> step, string name)
    {
        try
        {
            var rec = await step();
            Log.Write($"  {name}: {(rec is null ? "miss" : "HIT")}");
            return rec;
        }
        catch (Exception ex)
        {
            Log.Write($"  {name}: error {ex.Message}");
            return null;
        }
    }

    private static LyricSet ToLyricSet(string type, string? body) => type switch
    {
        "synced" when body is not null => new LyricSet(SyncType.Synced, LrcParser.Parse(body), null),
        "plain" when body is not null => new LyricSet(SyncType.Plain, Array.Empty<LyricLine>(), body),
        _ => LyricSet.NotFound,
    };
}

using System.IO;
using Microsoft.Data.Sqlite;

namespace LyricFloat.Data;

public sealed record CacheEntry(string SyncTypeText, string? Body, DateTime FetchedAtUtc);

public sealed class CacheDb : IDisposable
{
    private readonly SqliteConnection _conn;

    public CacheDb()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LyricFloat");
        Directory.CreateDirectory(dir);

        _conn = new SqliteConnection($"Data Source={Path.Combine(dir, "cache.db")}");
        _conn.Open();

        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS lyrics_cache (
                id INTEGER PRIMARY KEY,
                artist_norm TEXT NOT NULL,
                title_norm  TEXT NOT NULL,
                duration_s  INTEGER NOT NULL,
                sync_type   TEXT NOT NULL,
                lrc_body    TEXT,
                source      TEXT NOT NULL DEFAULT 'lrclib',
                fetched_at  TEXT NOT NULL,
                UNIQUE(artist_norm, title_norm, duration_s)
            );
            """;
        cmd.ExecuteNonQuery();

        // Self-healing: negative entries are session hints only. Purging them
        // at startup means a bad lookup can never poison the app across runs.
        using var purge = _conn.CreateCommand();
        purge.CommandText = "DELETE FROM lyrics_cache WHERE sync_type = 'not_found';";
        purge.ExecuteNonQuery();
    }

    public CacheEntry? Get(string artist, string title, int durationSec)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            SELECT sync_type, lrc_body, fetched_at FROM lyrics_cache
            WHERE artist_norm = $a AND title_norm = $t
              AND ABS(duration_s - $d) <= 2
            LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("$a", artist.ToLowerInvariant());
        cmd.Parameters.AddWithValue("$t", title.ToLowerInvariant());
        cmd.Parameters.AddWithValue("$d", durationSec);

        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;

        return new CacheEntry(
            reader.GetString(0),
            reader.IsDBNull(1) ? null : reader.GetString(1),
            DateTime.Parse(reader.GetString(2)).ToUniversalTime());
    }

    public void Put(string artist, string title, int durationSec, string syncType, string? body)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO lyrics_cache (artist_norm, title_norm, duration_s, sync_type, lrc_body, fetched_at)
            VALUES ($a, $t, $d, $s, $b, $f)
            ON CONFLICT(artist_norm, title_norm, duration_s)
            DO UPDATE SET sync_type = $s, lrc_body = $b, fetched_at = $f;
            """;
        cmd.Parameters.AddWithValue("$a", artist.ToLowerInvariant());
        cmd.Parameters.AddWithValue("$t", title.ToLowerInvariant());
        cmd.Parameters.AddWithValue("$d", durationSec);
        cmd.Parameters.AddWithValue("$s", syncType);
        cmd.Parameters.AddWithValue("$b", (object?)body ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$f", DateTime.UtcNow.ToString("O"));
        cmd.ExecuteNonQuery();
    }

    public void Dispose() => _conn.Dispose();
}

using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LyricFloat.Spotify;

public sealed class SpotifyTokens
{
    public required string AccessToken { get; init; }
    public required string RefreshToken { get; init; }
    public required DateTime ExpiresAtUtc { get; init; }
    public bool IsExpired => DateTime.UtcNow >= ExpiresAtUtc.AddSeconds(-30);
}

/// <summary>
/// Authorization Code + PKCE flow. No client secret needed - safe for a
/// desktop app. The user creates a free app at developer.spotify.com,
/// adds the redirect URI below, and pastes the Client ID into Settings.
/// </summary>
public sealed class SpotifyAuth
{
    public const string RedirectUri = "http://127.0.0.1:8898/callback";
    private const string Scopes = "user-read-playback-state user-read-currently-playing";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    public async Task<SpotifyTokens?> AuthorizeAsync(string clientId, CancellationToken ct = default)
    {
        var verifier = RandomUrlSafe(64);
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var state = RandomUrlSafe(16);

        var authUrl = "https://accounts.spotify.com/authorize" +
                      $"?client_id={Uri.EscapeDataString(clientId)}" +
                      "&response_type=code" +
                      $"&redirect_uri={Uri.EscapeDataString(RedirectUri)}" +
                      $"&scope={Uri.EscapeDataString(Scopes)}" +
                      "&code_challenge_method=S256" +
                      $"&code_challenge={challenge}" +
                      $"&state={state}";

        using var listener = new HttpListener();
        listener.Prefixes.Add("http://127.0.0.1:8898/");
        listener.Start();

        // Open the user's default browser on the Spotify consent page.
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(authUrl)
        {
            UseShellExecute = true
        });

        var contextTask = listener.GetContextAsync();
        var timeout = Task.Delay(TimeSpan.FromMinutes(3), ct);
        if (await Task.WhenAny(contextTask, timeout) != contextTask)
        {
            listener.Stop();
            return null;
        }

        var context = await contextTask;
        var query = context.Request.QueryString;
        var code = query["code"];
        var returnedState = query["state"];

        var ok = code is not null && returnedState == state;
        var html = ok
            ? "<html><body style='font-family:sans-serif;background:#0e0e12;color:#e7c87a;text-align:center;padding-top:80px'><h2>LyricFloat connected to Spotify \u2713</h2><p style='color:#aaa'>You can close this tab and get back to the music.</p></body></html>"
            : "<html><body style='font-family:sans-serif'><h2>Authorization failed.</h2></body></html>";
        var bytes = Encoding.UTF8.GetBytes(html);
        context.Response.ContentType = "text/html; charset=utf-8";
        context.Response.ContentLength64 = bytes.Length;
        await context.Response.OutputStream.WriteAsync(bytes, ct);
        context.Response.Close();
        listener.Stop();

        if (!ok) return null;

        return await ExchangeAsync(clientId, new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code!,
            ["redirect_uri"] = RedirectUri,
            ["client_id"] = clientId,
            ["code_verifier"] = verifier,
        }, fallbackRefresh: null, ct);
    }

    public Task<SpotifyTokens?> RefreshAsync(string clientId, string refreshToken, CancellationToken ct = default)
        => ExchangeAsync(clientId, new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
            ["client_id"] = clientId,
        }, fallbackRefresh: refreshToken, ct);

    private static async Task<SpotifyTokens?> ExchangeAsync(
        string clientId, Dictionary<string, string> form, string? fallbackRefresh, CancellationToken ct)
    {
        using var resp = await Http.PostAsync(
            "https://accounts.spotify.com/api/token", new FormUrlEncodedContent(form), ct);
        if (!resp.IsSuccessStatusCode) return null;

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        var root = doc.RootElement;

        var refresh = root.TryGetProperty("refresh_token", out var r)
            ? r.GetString() : fallbackRefresh;
        if (refresh is null) return null;

        return new SpotifyTokens
        {
            AccessToken = root.GetProperty("access_token").GetString()!,
            RefreshToken = refresh,
            ExpiresAtUtc = DateTime.UtcNow.AddSeconds(root.GetProperty("expires_in").GetInt32()),
        };
    }

    private static string RandomUrlSafe(int bytes)
        => Base64Url(RandomNumberGenerator.GetBytes(bytes));

    private static string Base64Url(byte[] data)
        => Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

# LyricFloat

A Rainmeter-style floating lyrics widget for Windows. Shows live-synced lyrics
for whatever is playing on Spotify (or YouTube Music, or any media app),
always on top — built for staying visible while gaming.

- **Zero-config core:** reads the current track and playback position straight
  from Windows (SMTC). No API keys needed to get synced lyrics on screen.
- **Lyrics from [LRCLIB](https://lrclib.net)** — free, open, synced `.lrc` lyrics.
- **SQLite cache** — repeat songs load instantly and work offline.
- **Spotify Premium enrichment (optional):** connect your account for
  millisecond-precise sync anchoring and album art in the widget.
- **Game-friendly:** click-through, never steals focus, hidden from Alt+Tab,
  re-asserts topmost if a game fights for z-order. No injection, no hooks —
  anti-cheat safe by design.
- **Arabic/RTL aware:** per-line right-to-left rendering for mixed-language songs.

## Build

Requirements: Windows 10 2004+ / Windows 11, [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```powershell
git clone https://github.com/Othdu/LyricFloat
cd LyricFloat
dotnet publish src/LyricFloat -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true
```

The EXE lands in `src/LyricFloat/bin/Release/net8.0-windows10.0.19041.0/win-x64/publish/`.
(Use `--self-contained true` if you want it to run on machines without .NET installed.)

## Usage

1. Run `LyricFloat.exe` — a gold music-note icon appears in the tray.
2. Play a song on Spotify. Synced lyrics appear in the floating panel.
3. **Ctrl+Shift+E** → edit mode: drag to move, scroll wheel to resize width. Press again to lock (clicks pass through).
4. **Ctrl+Shift+L** → show/hide. **Ctrl+Shift+Up/Down** → nudge sync ±250 ms.
5. **Ctrl+Shift+S** or the tray icon → Settings (opacity, font size, line count, auto-start, Spotify).

> **Gaming tip:** set your game to **Borderless Windowed** (in Valorant:
> Settings → Video → Display Mode → Windowed Fullscreen). True fullscreen-exclusive
> hides every overlay on the system — that's a Windows limitation, not an app bug.

## Spotify Premium enrichment (optional)

The app works fully without this. Connecting adds album art and a more precise
sync anchor (the OS position feed is coarse; the Web API is exact).

1. Go to [developer.spotify.com/dashboard](https://developer.spotify.com/dashboard) → **Create app**.
2. Set **Redirect URI** to exactly: `http://127.0.0.1:8898/callback`
3. Check **Web API**, save, and copy the **Client ID**.
4. In LyricFloat: tray → Settings → paste the Client ID → **Connect Spotify** → approve in the browser.

Auth uses Authorization Code + PKCE (no client secret involved). The refresh
token is stored in `%APPDATA%\LyricFloat\settings.json` on your own machine.

## Architecture (short version)

```
SMTC (Windows) ──track/position──▶ PositionInterpolator ──▶ SyncEngine (60ms tick, binary search)
      │                                    ▲                        │ active line
      ▼                                    │ precise anchor         ▼
LyricsService ── SQLite cache ── LRCLIB    │                 OverlayWindow (WPF, topmost,
                                  Spotify Web API (Premium)   click-through, RTL-aware)
```

Full design rationale in [`docs/BUILD-SPEC.md`](docs/BUILD-SPEC.md).

## Reliability notes

- **Slow or flaky connections:** the app warms up the LRCLIB connection at
  startup and pings it every 3 minutes so lookups don't pay a cold DNS/TLS
  handshake on every song. Lookups run in parallel and the first successful
  response wins; the request timeout is 15 s to accommodate high-latency routes.
- **Track skips:** Spotify updates the Windows media session in stages
  (title before duration). The watcher waits ~400 ms for metadata to settle,
  re-emits if the duration corrects itself later, and never clears lyrics
  that are already displaying correctly.
- **Caching:** synced/plain lyrics are cached forever; "not found" results
  expire after 7 days (LRCLIB's database keeps growing). Cache lives at
  `%APPDATA%\LyricFloat\cache.db` - safe to delete anytime.

## Troubleshooting

- **Debug log:** `%APPDATA%\LyricFloat\debug.log` records every track event,
  each lyrics lookup step and its timing, and tray/keepalive status.
- **No lyrics on many songs + timeouts in the log:** your DNS may be slow;
  try switching to Cloudflare DNS (1.1.1.1) in your adapter settings.
- **Overlay invisible in a game:** switch the game to Borderless Windowed.
- **Tray icon missing:** use Ctrl+Shift+S to reach Settings; check the log.

## Credits

Lyrics data by the [LRCLIB](https://lrclib.net) community database.
Not affiliated with Spotify. For personal use.

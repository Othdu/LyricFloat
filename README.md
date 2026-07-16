<div align="center">

# 🎵 LyricFloat

**Floating, live-synced lyrics for Windows — built to stay on screen while you game.**

A Rainmeter-style overlay that shows the current lyric line for whatever is playing
on Spotify (or YouTube Music, or any media app), perfectly synced, always on top,
and completely invisible to anti-cheat.

![Platform](https://img.shields.io/badge/platform-Windows%2010%2F11-0078D6?logo=windows)
![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet)
![UI](https://img.shields.io/badge/UI-WPF-5C2D91)
![Lyrics](https://img.shields.io/badge/lyrics-LRCLIB-E7C87A)
![License](https://img.shields.io/badge/license-MIT-green)

<!-- TODO: add screenshot/GIF here — a shot of the overlay over a game sells this instantly -->
<!-- ![LyricFloat in action](docs/screenshot.png) -->

</div>

---

## Why LyricFloat?

Every existing lyrics overlay either injects into the game (anti-cheat risk),
requires a subscription, or dies the moment Spotify changes its API.
LyricFloat takes a different path:

- It reads playback **directly from Windows** (SMTC — the same layer that powers
  your media keys). No Spotify API keys, no OAuth, no polling limits, nothing to break.
- It renders lyrics in a **plain external window** — no hooks, no injection, no DLLs
  touching the game process. Vanguard, VAC, and friends have nothing to object to.
- Lyrics come from **[LRCLIB](https://lrclib.net)**, a free and open community
  database of time-synced lyrics.

## ✨ Features

| | |
|---|---|
| 🎯 **Zero-config sync** | Launch it, play a song, lyrics appear — synced to the millisecond via an interpolated playback clock |
| 🖱️ **True overlay behavior** | Click-through, never steals focus, hidden from Alt+Tab, re-claims topmost every second when games fight for z-order |
| ⚡ **Instant on track change** | Lyrics for upcoming queue tracks are prefetched into a local SQLite cache while the current song plays *(Spotify connection)* |
| 🌐 **Built for bad internet** | Parallel lookups where the first success wins, connection warm-up + keepalive, generous timeouts, automatic retries |
| 🎨 **Your style** | 7 color presets (champagne gold, Valorant red, Spotify green…) or any hex color, adjustable opacity/font/line count, smooth slide-up line transitions |
| 🌍 **Arabic & RTL aware** | Per-line right-to-left rendering — mixed-language songs render every line correctly |
| 💾 **Offline-friendly** | Every fetched lyric is cached forever; repeat plays work with no connection at all |
| 🎧 **Spotify Premium extras** | Optional: album art in the widget + millisecond-exact sync anchoring via the Web API |

## 🚀 Quick start

**Requirements:** Windows 10 2004+ / Windows 11 · [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (build only)

```powershell
git clone https://github.com/Othdu/LyricFloat
cd LyricFloat
dotnet publish src/LyricFloat -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true
```

Run `LyricFloat.exe` from
`src\LyricFloat\bin\Release\net8.0-windows10.0.19041.0\win-x64\publish\`,
play a song on Spotify, and the lyrics panel appears. That's the whole setup.

> Building for a machine without .NET installed? Use `--self-contained true`.

## ⌨️ Hotkeys

| Shortcut | Action |
|---|---|
| `Ctrl` `Shift` `E` | **Edit mode** — drag to move, scroll wheel to resize, press again to lock (clicks pass through) |
| `Ctrl` `Shift` `L` | Show / hide the overlay |
| `Ctrl` `Shift` `S` | Open Settings |
| `Ctrl` `Shift` `↑` / `↓` | Nudge lyric sync ± 250 ms |

Settings (via tray icon or `Ctrl+Shift+S`): opacity, font size, visible line count,
lyrics color, transition animation, sync offset, auto-start with Windows, Spotify connection.

## 🎮 Gaming setup

Set your game's display mode to **Borderless / Windowed Fullscreen**
(Valorant: *Settings → Video → Display Mode → Windowed Fullscreen*).

True fullscreen-exclusive bypasses the Windows compositor entirely — no overlay
from *any* app can render over it. Borderless costs virtually nothing on modern
systems and is what most competitive players use anyway.

**Anti-cheat note:** LyricFloat never injects code, hooks rendering, or reads game
memory. It is an ordinary always-on-top window — the same class of thing as a
sticky-note app. There is nothing for Vanguard or VAC to detect.

## 🎧 Spotify connection (optional)

The app is fully functional without this. Connecting adds **album art** in the
widget, **millisecond-precise sync anchoring**, and **queue prefetching** so the
next songs' lyrics are cached before they start playing.

1. Go to [developer.spotify.com/dashboard](https://developer.spotify.com/dashboard) → **Create app**
2. Set the **Redirect URI** to exactly `http://127.0.0.1:8898/callback` and enable **Web API**
3. Copy the **Client ID**, paste it in LyricFloat's Settings, click **Connect Spotify**, approve in the browser

Authorization uses **Code + PKCE** — no client secret exists anywhere in this app.
Your refresh token is stored locally in `%APPDATA%\LyricFloat\settings.json` and
never leaves your machine.

## 🏗️ How it works

```
Windows SMTC ──track / position──▶ PositionInterpolator ──▶ SyncEngine (60 ms tick, binary search)
     │                                     ▲                          │ active line index
     ▼                                     │ precise anchor           ▼
LyricsService ─ SQLite cache ─ LRCLIB      │                   OverlayWindow (WPF, topmost,
     ▲                          Spotify Web API (optional)      click-through, RTL-aware)
     └── queue prefetch ◀───────────┘
```

- **PositionInterpolator** — Spotify updates the OS timeline in coarse steps; a wall-clock
  interpolation between anchors produces smooth per-line timing.
- **SyncEngine** — ticks at 60 ms while playing, binary-searches the active line,
  raises an event only when the highlighted line actually changes.
- **LyricsService** — cache → LRCLIB exact match → field search → free-text search,
  all fault-isolated, racing in parallel with first-success-wins.
- **MediaWatcher** — handles the messy reality of SMTC: skips report the new title
  before the new duration, so it settles, verifies, and re-emits corrections.

Full design rationale lives in [`docs/BUILD-SPEC.md`](docs/BUILD-SPEC.md).

## 🔧 Troubleshooting

| Symptom | Fix |
|---|---|
| Overlay invisible in a game | Switch the game to **Borderless / Windowed Fullscreen** |
| Lyrics slow or missing on many songs | Check `%APPDATA%\LyricFloat\debug.log` for timeouts; slow DNS is the usual cause — try Cloudflare DNS (`1.1.1.1`) |
| Tray icon missing | `Ctrl+Shift+S` still opens Settings; check the debug log |
| Wrong lyrics timing on one song | `Ctrl+Shift+↑/↓` nudges sync in 250 ms steps (persisted) |
| Weird cached result | Delete `%APPDATA%\LyricFloat\cache.db` — it rebuilds itself |

Every track event, lookup step, and timing is written to
`%APPDATA%\LyricFloat\debug.log` — attach it when reporting an issue.

## 📦 Tech stack

**C# / .NET 8 · WPF** · Windows SMTC (WinRT) · SQLite (`Microsoft.Data.Sqlite`) ·
[LRCLIB API](https://lrclib.net/docs) · Spotify Web API (PKCE) · H.NotifyIcon

## 🙏 Credits

Lyrics data by the wonderful [LRCLIB](https://lrclib.net) community database.
Not affiliated with Spotify AB or Riot Games. For personal use.

## 📄 License

MIT — do whatever you want, attribution appreciated.

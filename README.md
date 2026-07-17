<div align="center">

# 🎵 LyricFloat

**Floating, live-synced lyrics for Windows — built to stay on screen while you game.
Yes, even in Valorant.**

A Rainmeter-style overlay that shows the current lyric line for whatever is playing
on Spotify (or YouTube Music, or any media app) — perfectly synced, always on top,
with zero injection and nothing for anti-cheat to object to.

![Platform](https://img.shields.io/badge/platform-Windows%2010%2F11-0078D6?logo=windows)
![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet)
![UI](https://img.shields.io/badge/UI-WPF-5C2D91)
![Lyrics](https://img.shields.io/badge/lyrics-LRCLIB-E7C87A)
![Release](https://img.shields.io/github/v/release/Othdu/LyricFloat?color=green)

<!-- TODO: screenshot/GIF of the overlay over a game — docs/screenshot.png -->

</div>

---

## 📥 Download

Grab the latest zip from **[Releases](https://github.com/Othdu/LyricFloat/releases)** — no .NET install needed, everything is bundled.

| Zip | For | Setup |
|---|---|---|
| **`standard`** | Everyone — works over every app and game *except Valorant* | Extract → run `LyricFloat.exe`. Done. |
| **`valorant`** | Valorant players | Extract → right-click `install-uiaccess.ps1` → *Run with PowerShell* → launch from the desktop shortcut |

> **First run:** Windows SmartScreen may warn because this is a small open-source
> project without a paid code-signing certificate. Click **More info → Run anyway**.

Then just play a song on Spotify. Lyrics appear. That's the entire onboarding.

## Why LyricFloat?

Every existing lyrics overlay either injects into the game (anti-cheat roulette),
charges a subscription, or breaks whenever Spotify changes its API. LyricFloat
takes a different path:

- **It reads playback directly from Windows** (SMTC — the layer behind your media
  keys). No API keys, no OAuth required, nothing for Spotify to break.
- **It never touches the game.** No hooks, no injected DLLs, no memory reading —
  just a window. For Valorant it uses Windows' own accessibility z-band
  (`uiAccess`) to stay visible, which is why the one-time install step exists.
- **Lyrics come from [LRCLIB](https://lrclib.net)**, a free, open, community
  database of time-synced lyrics.

## ✨ Features

| | |
|---|---|
| 🎯 **Zero-config sync** | Play a song, lyrics appear — synced to the millisecond via an interpolated playback clock |
| 🎮 **Survives Valorant** | The uiAccess build sits in the privileged z-band above the game's own always-on-top window |
| 🖱️ **True overlay behavior** | Click-through, never steals focus, hidden from Alt+Tab, auto-reclaims topmost the instant any window takes foreground |
| ⚡ **Instant track changes** | Upcoming queue tracks are prefetched into a local SQLite cache while the current song plays *(Spotify connection)* |
| 🌐 **Built for bad internet** | Parallel lookups where the first success wins, connection warm-up + keepalive, automatic retries — designed and tested on a genuinely hostile connection |
| 🎨 **Your style** | 7 color presets (champagne gold, Valorant red, Spotify green…) or any hex, opacity/font/line-count controls, smooth slide-up line transitions |
| 🌍 **Arabic & RTL aware** | Per-line right-to-left rendering — mixed-language songs render every line correctly |
| 💾 **Offline-friendly** | Cached lyrics work with no connection at all |
| 🎧 **Spotify Premium extras** | Optional: album art in the widget + millisecond-exact sync anchoring |

## ⌨️ Hotkeys

| Shortcut | Action |
|---|---|
| `Ctrl` `Shift` `E` | **Edit mode** — drag to move, scroll to resize, press again to lock (clicks pass through) |
| `Ctrl` `Shift` `L` | Show / hide the overlay |
| `Ctrl` `Shift` `S` | Open Settings |
| `Ctrl` `Shift` `↑` / `↓` | Nudge lyric sync ± 250 ms |

## 🎮 Gaming setup

Set the game to **Borderless / Windowed Fullscreen** (Valorant: *Settings → Video →
Display Mode → Windowed Fullscreen*). True exclusive fullscreen bypasses the
Windows compositor — nothing can draw over it, by design of Windows itself.

For Valorant, use the **valorant** release zip. The one-time installer exists
because Windows only grants the uiAccess privilege to a signed exe in Program
Files — the script generates a certificate **locally on your own machine**,
trusts it locally, installs, and signs. Nothing external is downloaded or trusted.

## 🎧 Spotify connection (optional)

Fully optional — the app works without it. Connecting adds album art,
millisecond-precise sync anchoring, and queue prefetching (instant lyrics on
every track change).

1. [developer.spotify.com/dashboard](https://developer.spotify.com/dashboard) → **Create app**
2. Redirect URI: exactly `http://127.0.0.1:8898/callback`, enable **Web API**
3. Paste the **Client ID** into LyricFloat Settings → **Connect Spotify** → approve

Auth is Authorization Code + **PKCE** — no client secret exists anywhere.
Tokens live in `%APPDATA%\LyricFloat\settings.json` and never leave your machine.

## 🏗️ How it works

```
Windows SMTC ──track / position──▶ PositionInterpolator ──▶ SyncEngine (60 ms tick, binary search)
     │                                     ▲                          │ active line index
     ▼                                     │ precise anchor           ▼
LyricsService ─ SQLite cache ─ LRCLIB      │                   OverlayWindow (WPF, topmost,
     ▲                          Spotify Web API (optional)      uiAccess band, click-through)
     └── queue prefetch ◀───────────┘
```

Some battle scars worth reading about in the code:

- **The skip race** — Spotify updates the OS media session in stages (title before
  duration), so a naive watcher looks up lyrics with the *previous* song's length.
  `MediaWatcher` settles, verifies, and re-emits corrections.
- **First-success racing** — exact-match and search lookups run in parallel and
  the first hit wins; on a high-latency route this cut lookup times from ~24 s
  worst-case to a few seconds.
- **The hidden-owner-window bug** — WPF windows with `ShowInTaskbar=False` get a
  hidden owner that never receives `WS_EX_TOPMOST`, silently losing z-order fights
  with games. `OverlayWindow` forces the owner into the topmost band too.
- **uiAccess** — the documented, injection-free way to render above a game that
  aggressively self-raises. See `scripts/install-uiaccess.ps1`.

Full design rationale in [`docs/BUILD-SPEC.md`](docs/BUILD-SPEC.md).

## 🛠️ Building from source

Requires the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```powershell
git clone https://github.com/Othdu/LyricFloat
cd LyricFloat

# Standard build
dotnet publish src/LyricFloat -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true

# Valorant (uiAccess) build + local install
dotnet publish src/LyricFloat -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:UiAccessBuild=true
powershell -ExecutionPolicy Bypass -File scripts\install-uiaccess.ps1   # from an admin shell
```

Releases are built automatically by GitHub Actions on every `v*` tag —
see [`.github/workflows/release.yml`](.github/workflows/release.yml).

## 🔧 Troubleshooting

| Symptom | Fix |
|---|---|
| Overlay invisible in a game | Borderless/Windowed Fullscreen; for Valorant use the **valorant** zip |
| Valorant build won't launch | It must run from Program Files via the installer — re-run `install-uiaccess.ps1` |
| Lyrics slow or missing on many songs | Check `%APPDATA%\LyricFloat\debug.log` for timeouts; try Cloudflare DNS (`1.1.1.1`) |
| Wrong timing on one song | `Ctrl+Shift+↑/↓` nudges sync in 250 ms steps (persisted) |
| Weird cached result | Delete `%APPDATA%\LyricFloat\cache.db` — it rebuilds |

Every track event and lookup step is logged to `%APPDATA%\LyricFloat\debug.log` —
attach it when opening an issue.

## 📦 Tech stack

**C# / .NET 8 · WPF** · Windows SMTC (WinRT) · SQLite · [LRCLIB API](https://lrclib.net/docs) ·
Spotify Web API (PKCE) · GitHub Actions CI/CD

## 🙏 Credits

Lyrics data by the wonderful [LRCLIB](https://lrclib.net) community database.
Not affiliated with Spotify AB or Riot Games. For personal use.

## 📄 License

MIT — do whatever you want, attribution appreciated.

<div align="center">

# 🎵 LyricFloat

**Floating, live-synced lyrics for Windows — always on top, even while gaming.**

Shows the current lyric line for whatever is playing on Spotify (or any media app),
synced to the millisecond. No injection, no hooks — nothing for anti-cheat to mind.

![Platform](https://img.shields.io/badge/platform-Windows%2010%2F11-0078D6?logo=windows)
![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet)
![Lyrics](https://img.shields.io/badge/lyrics-LRCLIB-E7C87A)
![License](https://img.shields.io/badge/license-MIT-green)

<!-- screenshot goes here: docs/screenshot.png -->

</div>

---

## 📥 How to get it

You'll need the free [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) installed (one-time, 2 minutes).

**1. Download the code**

Click the green **Code** button at the top of this page → **Download ZIP** → extract it.
(Or `git clone https://github.com/Othdu/LyricFloat` if you use git.)

**2. Build it** — open a terminal in the extracted folder and run:

```powershell
dotnet publish src/LyricFloat -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true
```

**3. Install it** — open PowerShell **as Administrator** in the same folder and run:

```powershell
powershell -ExecutionPolicy Bypass -File ".\RUN THIS WITH POWERSHELL FIRST!!!!!.ps1"
```

This installs LyricFloat to Program Files, signs it with a certificate created
locally on your own machine (required by Windows for the always-on-top privilege
that beats games), and puts a shortcut on your Desktop.

**4. Run it** — launch from the **desktop shortcut**, play a song on Spotify. Done. 🎶

> ⚠️ Always launch via the shortcut — the exe in the build folder won't start
> (Windows only grants the overlay privilege to the installed, signed copy).

## 🎮 Playing games?

Set your game to **Borderless / Windowed Fullscreen**
(Valorant: *Settings → Video → Display Mode → Windowed Fullscreen*).
True exclusive fullscreen hides every overlay on Windows — that's an OS rule, not an app bug.

## ⌨️ Hotkeys

| Shortcut | Action |
|---|---|
| `Ctrl` `Shift` `E` | Edit mode — drag to move, scroll to resize, press again to lock |
| `Ctrl` `Shift` `L` | Show / hide |
| `Ctrl` `Shift` `S` | Settings (colors, opacity, font, auto-start, Spotify) |
| `Ctrl` `Shift` `↑` / `↓` | Nudge lyric timing ± 250 ms |

## 🎧 Optional: connect Spotify

The app works without it. Connecting adds album art, extra-precise sync,
and prefetching (lyrics load instantly on track changes):

1. [developer.spotify.com/dashboard](https://developer.spotify.com/dashboard) → **Create app**
2. Redirect URI: exactly `http://127.0.0.1:8898/callback` · enable **Web API**
3. Copy the **Client ID** → LyricFloat Settings (`Ctrl+Shift+S`) → paste → **Connect Spotify**

## ✨ What's inside

- Reads playback straight from **Windows** (the media-keys layer) — no API keys needed
- Synced lyrics from the free, open **[LRCLIB](https://lrclib.net)** database
- **SQLite cache** — repeat songs load instantly, even offline
- **Arabic / RTL** lyrics render correctly, line by line
- 7 color presets + any hex color, smooth line transitions, adjustable everything
- Built for weak connections: parallel lookups, keepalive, automatic retries
- Debug log at `%APPDATA%\LyricFloat\debug.log` if anything acts up

## 🔧 Quick fixes

| Problem | Fix |
|---|---|
| Overlay invisible in a game | Switch the game to Borderless / Windowed Fullscreen |
| App won't start from the build folder | Normal — use the desktop shortcut (installed copy) |
| A song shows wrong timing | `Ctrl+Shift+↑/↓` to nudge, it's remembered |
| Lyrics missing on many songs | Check debug.log for timeouts; try Cloudflare DNS (1.1.1.1) |

## 🙏 Credits

Lyrics by the wonderful [LRCLIB](https://lrclib.net) community.
Not affiliated with Spotify or Riot Games. Personal use. MIT license.

# LyricFloat — Floating Synced Lyrics Overlay for Windows

**Build Specification & Implementation Plan v1.0**

A Rainmeter-style, always-on-top floating widget that displays live-synced lyrics for whatever is playing on Spotify (or any media app), designed to stay visible while gaming.

---

## 1. Product Vision

A frameless, translucent, draggable lyrics panel that floats over games and apps. It shows the current lyric line (highlighted) with the previous/next lines dimmed, synced to the millisecond with Spotify playback. Zero configuration: launch it, play music, lyrics appear.

**Design language:** dark glass panel, subtle blur, champagne-gold accent for the active line (consistent with the StreamFlow aesthetic). Feels like a native Windows widget, not an app.

**Non-goals (v1):** playback controls, mobile version, word-level karaoke sync, macOS/Linux.

---

## 2. Core Technical Decisions

| Decision | Choice | Why |
|---|---|---|
| Stack | **C# / WPF, .NET 8** | Native Windows transparency + click-through, ~30 MB RAM, no runtime deps, portfolio value |
| Track detection | **Windows SMTC** (`GlobalSystemMediaTransportControlsSessionManager`) | No API keys, no OAuth, no polling limits, works with Spotify Free & Premium, also works with YouTube Music / any SMTC app |
| Lyrics source | **LRCLIB** (`lrclib.net/api`) | Free, no auth, purpose-built for FOSS players, returns both synced (.lrc) and plain lyrics |
| Lyrics cache | **SQLite** (`Microsoft.Data.Sqlite`) | Instant repeat plays, offline for cached songs |
| Spotify Web API | **Optional, Phase 4 only** | Only for album art / progress enrichment; not required for core function. Note: parts of the Web API now require Premium |
| Distribution | Single-file self-contained EXE + optional installer (Inno Setup), auto-start via registry Run key | Same pattern as ValoPingChecker |

### Why SMTC instead of the Spotify API (important)

SMTC gives us, directly from the OS with event-driven callbacks (no polling):
- Track title, artist, album
- Playback status (playing/paused)
- **Timeline position** — this is what drives lyric sync
- Media-changed events fired instantly on track change

This removes the two biggest failure modes of Spotify-API-based lyric apps: OAuth token refresh headaches, and Spotify tightening API access. The app never talks to Spotify at all.

**SMTC timeline caveat:** Spotify updates the SMTC timeline position in coarse steps (roughly every few seconds), not continuously. The sync engine must interpolate: on each timeline event, record `(position, timestamp)`, then compute `currentPosition = lastPosition + (Now - lastTimestamp)` while status == Playing. Pause/seek events reset the anchor. This yields smooth per-line sync.

---

## 3. Architecture

```
┌──────────────────────────────────────────────────────┐
│                     LyricFloat.exe                    │
│                                                       │
│  ┌────────────────┐   track/position   ┌───────────┐  │
│  │ MediaWatcher   │──────────────────▶│ SyncEngine │  │
│  │ (SMTC events)  │                    │ (interp.  │  │
│  └────────────────┘                    │  clock)   │  │
│          │ track changed               └─────┬─────┘  │
│          ▼                                   │ active │
│  ┌────────────────┐                          │ line   │
│  │ LyricsService  │                          ▼        │
│  │  1. SQLite     │                   ┌───────────┐   │
│  │  2. LRCLIB API │──── LyricSet ───▶│ OverlayVM │   │
│  │  3. fallback   │                   │ (MVVM)    │   │
│  └────────────────┘                   └─────┬─────┘   │
│                                             ▼         │
│                                     ┌──────────────┐  │
│                                     │ OverlayWindow │  │
│                                     │ (WPF, topmost,│  │
│                                     │ click-through)│  │
│                                     └──────────────┘  │
│  ┌────────────────┐  ┌──────────────┐                 │
│  │ TrayIcon +     │  │ SettingsStore│                 │
│  │ SettingsWindow │  │ (JSON)       │                 │
│  └────────────────┘  └──────────────┘                 │
└──────────────────────────────────────────────────────┘
```

### Components

**MediaWatcher**
- Wraps `GlobalSystemMediaTransportControlsSessionManager.RequestAsync()`
- Prefers the Spotify session (`SourceAppUserModelId` contains "Spotify"); falls back to the current session so YT Music etc. also work
- Subscribes to `MediaPropertiesChanged`, `PlaybackInfoChanged`, `TimelinePropertiesChanged`
- Emits: `TrackChanged(title, artist, album, durationMs)`, `PositionAnchor(positionMs, utcNow)`, `PlaybackStateChanged(isPlaying)`
- NuGet: none needed — use `Microsoft.Windows.SDK.Contracts` or target `net8.0-windows10.0.19041.0` TFM for WinRT projection

**LyricsService**
- Lookup order:
  1. SQLite cache (key: normalized `artist|title|duration`)
  2. LRCLIB `GET /api/get?artist_name=&track_name=&album_name=&duration=` (exact match)
  3. LRCLIB `GET /api/search?q=` (fuzzy fallback; pick best match by duration ±3 s)
  4. If only `plainLyrics` exists → unsynced mode (show full scrollable text, no highlight)
  5. Nothing found → panel shows "♪ no lyrics found" and collapses to a slim bar
- Title normalization before search: strip `(feat. …)`, `- Remastered`, `[Explicit]`, etc. If the exact title fails, retry with the normalized one
- Parse LRC into `List<LyricLine { TimeMs, Text }>`, sorted
- Cache every successful result AND negative results (`not_found` with a 7-day TTL so we retry eventually)
- `User-Agent: LyricFloat/1.0 (github.com/Othdu/LyricFloat)` — LRCLIB asks for an identifying UA

**SyncEngine**
- 60 ms `DispatcherTimer` (only while playing) computing interpolated position
- Binary search for the active line index; raises `ActiveLineChanged` only when index changes
- Global offset setting (±ms) for users who feel lyrics early/late, hotkey-adjustable in 250 ms steps

**OverlayWindow (the widget)**
- `WindowStyle=None`, `AllowsTransparency=True`, `Background=Transparent`, `Topmost=True`, `ShowInTaskbar=False`
- Click-through mode via `WS_EX_TRANSPARENT | WS_EX_LAYERED` (SetWindowLong); toggled off in "edit mode" so the panel can be dragged/resized
- Topmost watchdog: some games steal z-order; re-assert `Topmost` on a 2 s timer or via `SetWindowPos(HWND_TOPMOST)` when deactivated
- Layout: 3–5 lines visible; active line larger, gold, 100% opacity; neighbors dimmed; smooth `TranslateTransform` scroll animation between lines (150 ms ease-out)
- **RTL support:** detect Arabic/Hebrew glyphs per line → `FlowDirection.RightToLeft` + right alignment for that line (mixed-language songs handled per-line)
- Appearance settings: font, size, opacity, blur on/off, accent color, max width, line count
- Position persisted per-monitor; snap-to-edge helpers

**Tray + Settings**
- Tray icon (H.NotifyIcon.Wpf): Show/Hide overlay, Edit mode, Offset +/-, Settings, Exit
- Global hotkeys (RegisterHotKey): toggle overlay (default Ctrl+Shift+L), toggle edit mode, offset nudge
- Settings persisted to `%APPDATA%\LyricFloat\settings.json`

---

## 4. The Gaming Reality Check (Valorant-specific)

- **Fullscreen Exclusive hides ALL overlays.** The app must detect this is possible and the README/first-run tip should say: set the game to *Borderless Windowed*. In Valorant borderless costs essentially nothing.
- **Vanguard:** LyricFloat never injects, hooks, or reads game memory — it is a plain topmost window, the same class of thing as Discord's popout or OBS preview. No anti-cheat risk. (This is a selling point vs Overwolf-style overlays.)
- **Performance budget:** < 1% CPU while playing, 0% when paused (timer stops), ~30–40 MB RAM, no GPU-heavy effects by default (blur optional).

---

## 5. Data Model

```sql
CREATE TABLE lyrics_cache (
    id INTEGER PRIMARY KEY,
    artist_norm TEXT NOT NULL,
    title_norm  TEXT NOT NULL,
    duration_s  INTEGER NOT NULL,
    sync_type   TEXT NOT NULL,      -- 'synced' | 'plain' | 'not_found'
    lrc_body    TEXT,               -- raw LRC or plain text
    source      TEXT,               -- 'lrclib'
    fetched_at  TEXT NOT NULL,
    UNIQUE(artist_norm, title_norm, duration_s)
);
```

```csharp
record LyricLine(int TimeMs, string Text);
record LyricSet(SyncType Type, IReadOnlyList<LyricLine> Lines, string? PlainText);
record TrackInfo(string Title, string Artist, string Album, int DurationMs);
```

---

## 6. Project Structure

```
LyricFloat/
├── LyricFloat.sln
├── src/LyricFloat/
│   ├── App.xaml(.cs)                  # single-instance mutex, tray bootstrap
│   ├── Media/
│   │   ├── MediaWatcher.cs            # SMTC wrapper
│   │   └── PositionInterpolator.cs
│   ├── Lyrics/
│   │   ├── LyricsService.cs           # cache → lrclib pipeline
│   │   ├── LrclibClient.cs
│   │   ├── LrcParser.cs
│   │   └── TitleNormalizer.cs
│   ├── Sync/SyncEngine.cs
│   ├── Overlay/
│   │   ├── OverlayWindow.xaml(.cs)    # transparency, click-through, drag
│   │   └── OverlayViewModel.cs
│   ├── Settings/ (SettingsStore.cs, SettingsWindow.xaml, HotkeyManager.cs)
│   ├── Tray/TrayIconController.cs
│   └── Data/CacheDb.cs
└── tests/LyricFloat.Tests/            # LrcParser, TitleNormalizer, interpolator
```

---

## 7. Build Phases

### Phase 1 — Core pipeline (weekend 1)
- [ ] Console spike: SMTC → print current track + position (proves the hard part in ~50 lines)
- [ ] LrclibClient + LrcParser + TitleNormalizer with unit tests
- [ ] SyncEngine with interpolation; console prints the active lyric line in real time
- **Milestone:** synced lyrics scrolling in a console while Spotify plays

### Phase 2 — The widget (weekend 2)
- [ ] OverlayWindow: transparent, topmost, styled 3-line view, scroll animation
- [ ] Click-through + edit mode (drag/resize), position persistence
- [ ] Tray icon, show/hide hotkey
- **Milestone:** usable daily driver

### Phase 3 — Polish
- [ ] SQLite cache + negative caching
- [ ] Unsynced-lyrics fallback view, "no lyrics" slim state
- [ ] RTL/Arabic per-line handling
- [ ] Offset adjustment hotkeys, settings window, auto-start toggle
- [ ] Topmost watchdog, multi-monitor sanity
- **Milestone:** v1.0 release on GitHub (github.com/Othdu)

### Phase 4 — Nice-to-haves (optional)
- [ ] Album-art-tinted accent color (needs Spotify Web API or SMTC thumbnail)
- [ ] Theme presets (Rainmeter-style skins via JSON)
- [ ] Word-level sync when LRCLIB provides enhanced LRC
- [ ] Publish to winget / Microsoft Store

---

## 8. Key Edge Cases

| Case | Handling |
|---|---|
| Spotify closed / nothing playing | Overlay auto-hides after 5 s of no session |
| Podcast / local files with no metadata match | Negative cache, slim "no lyrics" bar |
| User seeks mid-song | `TimelinePropertiesChanged` resets anchor; binary search re-targets line instantly |
| Two media apps playing | Prefer Spotify session explicitly |
| Ads (Spotify Free) | Title like "Advertisement" → hide panel until next track |
| Track change race (lyrics arrive after next track started) | Tag fetches with a track token; discard stale results |
| Very long lines | Wrap up to 2 rows, ellipsis beyond |
| DPI scaling / multi-monitor | PerMonitorV2 DPI awareness in app manifest |

---

## 9. Risks & Mitigations

1. **SMTC position granularity** → interpolation (Section 2). Verified approach used by several open-source lyric tools.
2. **LRCLIB coverage gaps** (especially Arabic tracks) → fuzzy search fallback + future secondary provider interface (`ILyricsProvider`) so another source can be slotted in.
3. **Games hiding the overlay** → borderless-windowed guidance + topmost watchdog. Accepted limitation for true fullscreen-exclusive.
4. **Legal:** LRCLIB is a community lyrics database; the app displays lyrics for personal use and does not redistribute a database. Ship with attribution to LRCLIB.

---

## 10. Definition of Done (v1)

- Launch → play any Spotify song → synced lyrics appear within 2 s, correct line highlighted
- Alt-tab into Valorant (borderless) → lyrics remain visible, mouse clicks pass through
- Replay a cached song offline → lyrics still work
- CPU < 1%, RAM < 50 MB during a 2-hour session
- Arabic-lyric track renders right-to-left correctly

# Nothing Shell — Design

*Date: 2026-11-17 · Status: **approved by the user 2026-11-17** · Working name: **Nothing Shell** (folder: `D:\NothingLauncher`)*

A personal Windows 11 shell layer styled after Nothing OS: a custom taskbar, a centered search "start menu", a quick-settings + notification panel, a top-center notch, and desktop widgets. It runs in the background, starts with Windows, and stays light on memory.

Visual source of truth: the approved clickable mockup [`docs/mockups/final-v2.html`](../../mockups/final-v2.html) (open it in a browser). [`docs/mockups/notch-v3.html`](../../mockups/notch-v3.html) shows the notch at a larger size.

Items marked **(builder's call)** were proposed by Claude and not explicitly chosen by the user. They may be changed if there's a good reason.

---

## 1. Intent

**Why:** the user wants their PC to *"feel like mine and less cluttered"*. Today they use the Fenn taskbar and notch. They like its behavior (the Windows taskbar never appears; a custom bar slides up when the mouse touches the bottom edge) but want something that looks like Nothing OS and is calmer.

**Who:** one person, on their own laptop (Windows 11 Home, 1920×1080 primary display). **Personal use only.** It will not be shared or published.

**Success looks like:**
- Day to day, the screen is a calm Nothing-style desktop: monochrome, dot-matrix accents, one red accent.
- Everyday actions are faster than stock Windows: Win → type → Enter to launch; one click to reach Wi-Fi, Bluetooth, volume, brightness and notifications; hover the notch for timer, media, clipboard and notes.
- It's invisible in cost: starts with Windows, idles near 0% CPU, and uses well under 100 MB of memory.

## 2. Goals, non-goals, anti-goals

**Goals**
1. Replace the visible Windows taskbar with a hidden floating dock that appears on bottom-edge hover.
2. Replace the Start menu with a centered, search-first launcher opened by the Win key.
3. Replace the Windows quick settings/notification center with one panel (Wi-Fi, Bluetooth, volume + output device, brightness, notifications).
4. Add a top-center notch with resting, live and open states.
5. Add desktop widgets: clock, weather, media, battery, calendar, snake game, grid timer. Each can be resized and dragged into place.
6. Start with Windows, run in the background, and stay light.

**Non-goals (v1)**
- Multi-user, sharing, publishing, installers for others, auto-update.
- Replacing Alt-Tab, the lock screen, File Explorer, or window management (snapping and so on).
- Web search results in the launcher.
- Replying to notifications from the panel. Clicking a notification opens its app instead.
- Multi-monitor support beyond the primary display **(builder's call)**. On secondary monitors, nothing is drawn and nothing is hidden.

**Anti-goals: things that would make it a failure even if it works**
- **Clutter.** Anything always-on that the user didn't ask for: stats columns, busy bars, extra tiles on page 1 of the notch.
- **Heavy.** Idling above ~100 MB working set, or visible CPU use while idle. Animations run only when something changes.
- **Getting stuck without a taskbar.** If Nothing Shell crashes, is killed, or is closed, the Windows taskbar must come back on its own within a few seconds.
- **Getting in the way.** The notch or dock popping up over full-screen games or videos.
- **Breaking the look.** Off-palette colors (no blue or purple accents, unlike the GNOME/KDE references), or long text set in the dot font.

## 3. Constraints

- **Windows 11** (build 26300 on the user's machine). Primary display 1920×1080.
- **Memory:** target under 100 MB total working set at idle, measured after 10 minutes. CPU about 0% when nothing is changing.
- **Install:** a **one-time admin setup is acceptable** (user, 2026-11-17). Use it to install under Program Files as a signed `uiAccess=true` exe, so keyboard hooks and dock actions also work over elevated windows like Task Manager, and to trust a self-signed certificate for package identity (§9). After setup, it **starts at logon with no prompts**.
- **Windows won't let the taskbar be removed,** only hidden. The dock is drawn by us; the native taskbar is hidden and must be restorable.
- **Lone Win key** must open our launcher. Win+A and Win+N open our quick-settings panel. Other Win+ combos keep working normally.
- **Reading other apps' notifications** (`UserNotificationListener`) requires package identity and user consent. This may require MSIX packaging or "packaging with external location" (sparse package). **Not yet verified, see §9.**
- **Fonts:** Nothing's NDot/NType fonts are allowed because this is personal use. Do **not** copy code from `jaxparrow07/nothing-kde-widgets` (GPL-3.0); it's a visual reference only.

## 4. Approaches considered

| | Approach | Idle memory (est.) | Trade-off |
|---|---|---|---|
| **A** | **C# + WPF on .NET, one background process** | ~60–90 MB (a plain WPF app measured ~74 MB) | Light enough, fast to build, easy to change; direct access to every Windows API needed (WinRT media/notifications/Bluetooth, Win32 hooks, Core Audio) |
| B | C++ with Win32 + Direct2D/DirectComposition | ~15–30 MB (estimate, not measured) | Lightest, but every control, animation and text field is hand-built; several times slower to build and to change later |
| C | Tauri (WebView2), reusing the HTML mockups | ~140–200 MB (WebView2 runs Chromium) | Fastest UI work, but about twice A's memory, which conflicts with the "light" goal |

**Chosen: A (C# + WPF)**, picked by the user on 2026-11-17.

## 5. Visual system

### Palette
| Token | Value | Use |
|---|---|---|
| card | `#1A1A1A` | widget cards |
| tile | `#161616` (hover `#202020`) | notch tiles |
| glass | `rgba(16,16,16,0.90)` + background blur | dock, launcher, quick-settings panel |
| edge | `rgba(255,255,255,0.13)`, 1.5px, inside | outline on every widget card, so cards show up on dark wallpapers |
| panel line | `rgba(255,255,255,0.10)`, 1px | border on glass surfaces |
| text | `#F2F2F2` · dim `#8A8A8A` | |
| dot off | `#262626`–`#2C2C2C` | unlit dots |
| **red** | **`#E8231F`** | the **only** accent: "now", the next dot to go out, the snake's food, low battery, today's date, the day name, the second hand, the last start-button dot, alerts |
| on-state | `#F2F2F2` fill with dark text | active toggles (Wi-Fi, Bluetooth, keep awake) |

No blue, purple, green or yellow anywhere. Notifications and the media widget use only greyscale artwork treatments where we draw them; album art itself stays as-is, and so do app icons in the dock (user decision 2026-11-18).

### Type
- **Dot matrix:** numbers only (clocks, timer, percentages, scores, temperatures). Use the **5×7 dot renderer from the mockups** as the default so it matches what was approved; NDot can be offered as an option **(builder's call)**. Never use dots for sentences.
- **Launcher + quick-settings panel:** **Geist** (text) and **Geist Mono** (small uppercase labels), as the user asked.
- **Notch + widgets:** Space Grotesk (text) + Space Mono (small uppercase labels), as in the mockups.
- **Date numerals:** DM Serif Display (date widget and calendar header).
- Fonts are bundled with the app (no network font loading).

### Shapes and motion
- Corner radius: widgets 26px, notch tiles 22px, panels 28px, dock 30px (pill), notch 20px resting / 34px open.
- Grid: widget unit 136px plus a 12px gap, shown at **65% scale by default** (user found 100% too big). Global scale options S 50% / M 65% / L 80%.
- Motion: short and crisp, 160–320ms with `cubic-bezier(.2,.8,.2,1)`. No bounce or springs. Fades and slides only.
- Wallpapers: four 4K PNGs in `wallpapers/` (`nothing_halftone_light`, `nothing_halftone_dark`, `nothing_glyph_dark`, `nothing_grid_light`), generated by `wallpapers/make_wallpapers.py`.

## 6. The parts

### 6.1 Dock (taskbar)
- **Hidden by default.** The native Windows taskbar never appears.
- **Reveal:** when the cursor touches the bottom edge of the primary display, the dock slides up after **150ms**. It hides **500ms** after the cursor leaves, unless a panel is open or "keep visible" is on.
- **Layout (left → right):** start button (3×3 dots, last one red) · search button · divider · pinned + running apps (**apps' original colour icons** — user decision 2026-11-18, replacing the earlier monochrome icons; a small dot under running apps, red dot + highlight for the focused app) · divider · **⌃ overflow button** · tray (Wi-Fi, volume, battery, time + day/date in mono).
- **⌃ overflow (user's choice):** opens **Windows' own tray overflow flyout**, so other apps' tray icons (Discord, Steam, OneDrive, VPN…) keep their real icons and right-click menus. Nothing Shell does **not** draw third-party tray icons itself. Mechanism is the builder's call (e.g. invoking the native overflow, or briefly showing the native tray area); it must work while the native taskbar is otherwise hidden.
- **Initial pinned apps (builder's call):** import the user's current Windows taskbar pins on first run.
- **"Keep dock visible" on (builder's call):** the dock **reserves its screen space** like a normal taskbar (registered appbar / work area), so maximized windows end above it; it handles Explorer restarts. When off, the dock floats over windows and nothing is reserved.
- **Behavior (builder's call, standard taskbar):** click a pinned app to launch it; click a running app to activate it, or minimize it if it's already focused; apps with several windows show a small list; right-click offers pin/unpin and close.
- Clicking the tray opens the quick-settings panel.
- Hidden while a full-screen app is in front; the bottom edge does nothing then.
- Floating pill, glass surface, centered, 12px above the screen bottom, 60px tall.

### 6.2 Launcher ("start menu")
- Opens on **lone Win key**, the start button, or the search button. **Centered on screen** (user's choice), with a slight scrim over the desktop.
- **Cursor goes straight into the search box.**
- **Empty state:** "Most used": 6 apps ranked by how often they're launched from Nothing Shell; until there's history, the 6 most recently used Start Menu apps **(builder's call)**. Small "All apps ›" link for an A–Z list. Power button (sleep / restart / shut down).
- **Typing:** results come only from this PC, ranked **apps → settings → files**. The top result is a large "Best match" card; up to 5 more rows below show the kind (App / Setting / File). ↑/↓ moves, Enter opens, Esc closes. **No web results.**
- Opening something closes the launcher, and the notch briefly shows "Opening ‹name›…".
- Sources **(builder's call)**: Start Menu shortcuts (all-users + per-user) plus installed packaged apps; a fixed list of common `ms-settings:` pages with friendly names; files from the Windows Search index (name match) and Recent items.

### 6.3 Quick settings + notifications panel
- Opens from the dock tray, **Win+A** or **Win+N**. Anchored at the bottom-right above the dock, 400px wide, glass, **Geist** font.
- **Wi-Fi tile:** tap toggles the radio; **▸** expands an inline list of networks (connected / join). Joining a secured network that has no saved profile opens Windows' own Wi-Fi settings instead of asking for a password in our UI.
- **Bluetooth tile:** tap toggles the radio; **▸** expands paired devices (connect / disconnect).
- **Volume slider** (draggable) plus an **output chip** that switches the default playback device (e.g. Speakers ↔ CMF Buds 2).
- **Brightness slider** for the built-in display.
- **Notifications:** grouped by app (count + latest text), ✕ to dismiss a group, "Clear all"; clicking one opens its app. Nothing else (no Focus, airplane mode, Night light, calendar or media here; media lives in the notch).
- New notifications **do not** show Windows' bottom-right pop-up; they appear in the notch instead (§6.4). **(builder's call** on how to suppress Windows banners, e.g. turn off banners per app while keeping notifications in the notification center, so the listener still sees them; verify in the spike.)

### 6.4 Notch
Black pill, top-center of the primary display, 8px from the top, with a faint 1px light edge.

| State | Size | Content | When |
|---|---|---|---|
| Resting | 196×34 | dot-matrix time · battery icon + % | nothing live |
| Music | 300×34 | mini album art · animated 10×4 dot equalizer (red tops) | media playing |
| Timer | 270×34 | mini dot grid (draining) · dot countdown | timer running |
| Notification | 420×64 | app icon · app name · one line of text · "now" | ~3.6s after a new notification |
| Open | 770 × content | tool grid (below) | mouse hovers the notch |

- Live priority: **notification > timer > music > resting**.
- **Open dashboard:** grid of 84px cells with 10px gaps, 8 columns. **Page 1 (the user's daily four): Timer 2×2, Media 2×2, Clipboard 2×2, Notes 2×2.** **Page 2:** file shelf 2×2, keep awake 2×1, weather 2×1, tools 1×1 (grab text, phone, scan QR, color picker, snip, draw, settings). Page dots at the bottom right; ✎ Edit at the bottom left.
- **Edit mode:** each tile gets a size chip (cycles through its allowed sizes: 1×1 / 2×1 / 2×2) and ✕ remove; "＋ Add" brings removed tiles back. The grid packs densely. Edit mode keeps the notch open until "Done".
- Closes when the mouse leaves (unless editing).
- **Tile behavior:**
  - *Timer:* grid of dots that go out in a scattered order; − / play-pause / +; **shared with the desktop timer widget** (one timer).
  - *Media:* current system media session (title, artist, source app, dotted progress, prev / play-pause / next).
  - *Clipboard:* last N **text** items copied (newest first); click to copy again; expand icon opens a longer list. Saved across restarts.
  - *Notes:* one plain-text note, saved as you type, kept across restarts.
  - *Keep awake:* prevents sleep and screen-off while on.
  - *File shelf:* drop files to hold them; drag them out to use. Holds references, not copies **(builder's call)**.
  - *Tool buttons* **(builder's call** on implementation; guesses from the user's Fenn icons): Grab text = screen-region OCR to clipboard; Phone = open Phone Link; Scan QR = decode a QR code from a screen region; Color = screen color picker → hex to clipboard; Snip = region screenshot; Draw = launch Windows' Snipping Tool or annotation; Settings = Nothing Shell settings.
- Hidden while a full-screen app is in front.

### 6.5 Desktop widgets
Live on the desktop layer: under every app window, visible on an empty desktop and after Win+D. Black cards with the 1.5px edge.

| Widget | Sizes | Content |
|---|---|---|
| Clock | 2×1 · **3×1** · 4×2 | Date line + large dot-matrix HH:MM drawn straight on the wallpaper (no card); dark dots on light wallpapers, light dots on dark ones |
| Snake | **2×2** (14×14) · 3×3 (20×20) | Playable. Click to focus, arrows/WASD, Space pauses or restarts; white snake, red food, score in dots; **pauses automatically when focus leaves**; best score saved |
| Timer | **2×2** · 2×1 · 1×1 | **Grid style** (user's choice): 12×12 dots go out one by one in a scattered order, the next one red; flashes red at zero and sends a "Time is up" notification; click to start or pause; shared with the notch timer |
| Calendar | **2×2** month · 2×1 date + next event · 1×1 serif date | Today in red, dots under days with events. **Source: the user's Google Calendar** (user's choice); see below |
| Weather | **2×1** · 1×1 · 2×2 | Dot-drawn condition icon, temperature, high/low, 5-day row. Source below |
| Media | **2×1** · 1×1 · 2×2 | Art, title/artist, dotted progress, controls |
| Battery | **1×1** · 2×1 · 2×2 | **Ring plus device icon only, no number** (user's choice). One device fills the whole tile. With more devices connected (buds, mouse…), page dots appear on the right; tap a dot or scroll to switch, and the widget jumps to a newly connected device. Ring and icon turn red under 20%; ⚡ when charging; hover tooltip shows the exact % |

- Bold = default size. Default layout as in `final-v2.html`.
- **Desktop icons:** the user keeps **no icons on the desktop** (user, 2026-11-17), so widgets can sit anywhere on the desktop layer and must be clickable there. If icons ever exist, widgets sit above them.
- **Google Calendar (user's choice):** a one-time Google sign-in (OAuth, desktop-app flow, **read-only** calendar scope) from Settings; tokens stored encrypted for the current Windows user (DPAPI). Shows the user's calendars' events for the month (event dots) and the next upcoming event. Refreshes every 15 min and when the widget becomes visible **(builder's call)**. Not signed in: plain dates, plus a small "Connect Google Calendar" hint in edit mode only. Offline: last cached data.
- **Weather (builder's call):** Open-Meteo (free, no API key). **Location = a city the user types in Settings** (asked on first run; the "Delhi" in the mockups is placeholder). Refreshes every 30 min. Offline: last data, dimmed, with "updated hh:mm".
- **Timer defaults (builder's call):** 25:00; − / + change by 1 minute (minimum 1:00). "Time is up" while a full-screen app is in front: the widget still flashes and the notification is queued in the panel, but the notch stays hidden until full-screen ends.
- **Edit widgets mode:** a dot grid appears on the desktop. **Drag any widget** (including the clock); it snaps to a **74px grid** (half a cell, before scaling). **Overlaps are refused:** the outline turns red and the widget returns to where it was. Size chips cycle sizes; a resize that causes overlap shows a red outline until the widget is moved.
- Layout, sizes and the global scale are saved across restarts.

### 6.6 How the parts link
- One timer: desktop widget ⇄ notch tile ⇄ notch live state.
- One media session: media widget ⇄ notch tile ⇄ notch music state (pausing music returns the notch to resting).
- Bluetooth: connecting buds in the panel → they appear in the battery widget and the volume output chip.
- Notifications: shown in the notch, stored in the panel.
- Launcher: launching shows "Opening…" in the notch.

### 6.7 Settings and lifecycle
- **Settings** are reached from the notch page 2 "Settings" tile and from right-clicking the notch **(builder's call)**. Contents: widget scale (S/M/L), wallpaper (the four wallpapers, applied to Windows' desktop), start with Windows (on by default), keep-dock-visible, and **Quit**.
- On quit, or on any exit path, the Windows taskbar is restored.
- No system-tray icon of its own.

## 7. Technical design (builder's calls unless noted)

- **Stack:** C# / WPF (**user-chosen**), current .NET LTS, targeting a Windows 10.0.22621+ SDK for WinRT projections. One process.
- **Windows (surfaces):** notch (topmost, layered, no-activate), dock (topmost, no-activate), launcher (topmost, activatable, with scrim), quick-settings panel (topmost, activatable), widget host (desktop layer: **above** the desktop icon view so widgets receive clicks, below every app window; survives Win+D and Explorer restarts. Since there are no desktop icons, covering the icon view is fine). Hidden surfaces should be released or made very cheap.
- **Install/identity:** installed once with admin under Program Files, signed, `uiAccess=true` (needed so hooks and dock actions work over elevated windows); package identity via a sparse package (packaging with external location) signed with a self-signed certificate trusted at install. Startup via `StartupTask` (packaged) or a per-user Run entry; whichever the spike shows works with `uiAccess`.
- **DPI:** design numbers in this doc are in DIPs at 100%; the app is per-monitor DPI aware **(builder's call)**.
- **Coexisting with Fenn:** the user currently runs Fenn. Nothing Shell's first run tells them to quit or disable Fenn, and doesn't start its own taskbar or notch while another taskbar replacement is detected **(builder's call)**.
- **Native taskbar:** hide `Shell_TrayWnd`/`Shell_SecondaryTrayWnd`; set the appbar to auto-hide so maximized windows use the full screen; re-apply on `TaskbarCreated` (Explorer restart). **Hard requirement:** if Nothing Shell dies for any reason (crash, kill, logoff), the taskbar is usable again within a few seconds. One way is a tiny watchdog; the builder chooses, and must test kill -9.
- **Bottom-edge detection:** low-level mouse hook or cheap cursor polling, disabled while a full-screen app is in front.
- **Full-screen detection:** `SHQueryUserNotificationState` plus a foreground-window-covers-monitor check.
- **Keys:** `WH_KEYBOARD_LL` for a lone Win press/release → launcher; Win+A / Win+N → panel. Every other combo passes through.
- **Data sources:** media `GlobalSystemMediaTransportControlsSessionManager`; notifications `UserNotificationListener`; battery (laptop) WinRT `Battery` / power status; Bluetooth devices + battery via device enumeration / PnP battery property; radios `Windows.Devices.Radios`; Wi-Fi list/connect via WLAN API; volume + default device via Core Audio (default-device switching uses the well-known `IPolicyConfig` interface); brightness via WMI `WmiMonitorBrightness`; clipboard via `AddClipboardFormatListener`.
- **Storage:** `%AppData%\NothingShell\` with settings, layout, notes, clipboard history (text only, capped e.g. 50) and snake best score, as JSON.
- **Startup:** HKCU Run entry if unpackaged, or `StartupTask` if packaged (depends on §9).
- **Performance rules:** no idle timers faster than 1s except while something is visible and animating; equalizer, snake and countdown render only while on screen; measure memory and CPU at each phase.

## 8. Build order (builder's call)

0. **Spike (throwaway):** prove the risky pieces: (a) hide + guaranteed restore of the taskbar, including kill -9; (b) lone-Win-key interception, including while an elevated window is focused (needs the signed `uiAccess` install); (c) clickable widget window on the desktop layer, surviving Win+D and Explorer restarts; (d) `UserNotificationListener` working with a sparse package + self-signed cert, and suppressing Windows' banners without losing notifications; (e) the ⌃ button opening Windows' tray overflow while the taskbar is hidden. Record memory for an empty WPF host. **If (d) fails**, report back before phase 3; don't silently drop notification features.
1. **Host + dock:** startup, taskbar hide/restore, bottom-edge dock with pinned/running apps and the tray, full-screen handling, settings file.
2. **Launcher.**
3. **Quick-settings panel + notifications + notch live states.**
4. **Notch open dashboard** (page 1, then page 2, then edit mode).
5. **Desktop widgets + edit mode (drag/resize) + wallpaper setting.**

Each phase ends with a memory/CPU check against the budget.

## 9. Known risks and open items

- **Notification access:** `UserNotificationListener` needs package identity; the plan is a sparse package with a self-signed cert trusted at the one-time admin install. Unverified until spike (d).
- **Suppressing Windows' own notification banners** without losing them from the listener: verify in spike (d).
- **Lone-Win-key handling** may conflict with Windows features in some builds; fallback is the dock button plus a configurable hotkey.
- **Tray overflow** (⌃) relies on Windows' own flyout while the taskbar is hidden; verify in spike (e).
- **Google sign-in** needs an OAuth client registered in a Google Cloud project (personal use, "testing" mode is fine). The builder sets it up with the user at phase 5.
- `DEVPKEY` Bluetooth battery reporting depends on the device; some buds won't report it. Those devices show no ring.

## 10. Left to the builder

Project structure, MVVM or not, which libraries (e.g. NAudio vs. raw COM for Core Audio), icon set (monochrome line icons matching the mockups), exact animation curves within the stated range, app-icon treatment in the dock, OCR/QR/color-picker implementations, logging, tests.

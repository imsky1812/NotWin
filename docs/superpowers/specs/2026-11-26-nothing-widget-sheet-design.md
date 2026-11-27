# Plan 13: the Nothing widget sheet — eight new desktop widgets (design)

**Why (user, 2026-11-26):** NotWin's desktop should look like Nothing OS's widget sheet
(`docs/references/nothing-widgets.webp`, kept local): small round and rounded-square tiles in black, light grey and red,
with dot-matrix numbers. The user picked all eight widgets below from a proposed list. Research notes (the sheet and
Dribbble patterns) are in `docs/references/widget-research.md`.

**Approach (user picked A):** every widget is its own `WidgetKind`, class in `src/NotWin/Widgets` and testable logic in
`NotWin.Core`, like the existing widgets. Toggle and World clock can be added many times; what each copy is (which
toggle, which time zone, and the analogue clock's face) is chosen from a small list that opens when you click the widget
in edit mode.
- Rejected B — one kind per toggle ("Wi-Fi toggle", "Bluetooth toggle", …): six more picker rows, one of each only, and
  world clocks would still need a chooser.
- Rejected C — a toggle strip (one card with 2–4 buttons): reads as a control panel, not Nothing's separate tiles.

## Goals
- Eight new widgets that look like they belong on the reference sheet, built with the app's tokens (Ink `#101010`,
  card `#1A1A1A`, text `#E8E8E8`, red `#D71921` only for "on / notice this"), Space Grotesk / Space Mono and dot-matrix
  numerals (`DotMatrix`) and dot icons (`DotArtView`).
- Light on the machine: nothing polls faster than it must; every timer stops while a full-screen app is in front
  (`SetPaused`) and when the widget is disposed. System events instead of polling where Windows has them.

## Non-goals
- Do Not Disturb and Night light (no supported API; only undocumented hacks), Airplane mode (apps can only flip
  single radios), Hotspot, a real compass heading or step counter (no sensors on a PC).
- A dot-matrix "now playing" and a "next trip" card: later, as new looks for the existing Media and Up next widgets.
- Per-app screen time; syncing screen time between PCs.

## Anti-goals (a failure even if it works)
- Showing or logging personal data: no SSID, Bluetooth device names, city, adapter names, window or app titles. Logs
  say only things like `toggle wifi -> on`.
- Changing any setting without the user's click on that tile.
- A widget that keeps a timer running while nothing on screen changes.

## Shared pieces

### Kinds, sizes, rules (`WidgetLayout.cs`)
New kinds are appended (saved as numbers): `Toggle = 13, AnalogClock, WorldClock, StackedClock, Stopwatch, ScreenTime,
SystemMeters, SunCompass`. A new tall size `S12 = (1, 2)`.

| Kind | Sizes (first = default) | Round? | Repeatable |
|---|---|---|---|
| Toggle | 1×1, 2×1 | yes (circle / pill) | yes |
| AnalogClock | 1×1, 2×2 | yes (circle) | no |
| WorldClock | 1×1, 2×1 | yes (circle / pill) | yes |
| StackedClock | 1×2, 1×1 | yes (tall pill / circle) | no |
| Stopwatch | 1×1, 2×1 | yes (circle / pill) | no |
| ScreenTime | 2×1, 2×2 | no (radius 26) | no |
| SystemMeters | 2×1, 2×2 | no | no |
| SunCompass | 1×1, 2×2 | yes (circle) | no |

- `WidgetRules.IsRound(WidgetPlacement)`: true for the round kinds above and for a photo frame with shape `circle`
  (moves today's photo-only check out of `EditBackdrop`). "Round" = corner radius half the shorter side, so 1×1 and 2×2
  are circles, 2×1 and 1×2 are pills. Edit mode's outline uses the same rule.
- `IsRepeatable`: Photo, Folder, **Toggle, WorldClock**.
- Picker order: the new kinds join the list grouped with their relatives (Toggle after Folder; the clocks after Clock;
  Stopwatch after Timer; ScreenTime, SystemMeters, SunCompass at the end). Exact order is the builder's call.

### Per-widget choice (`WidgetConfig.Choice`)
- `WidgetConfig` gains `string? Choice`. Its meaning depends on the kind: a toggle id (`wifi`, `bluetooth`, `dark`,
  `awake`, `mic`, `camera`), a Windows time-zone id, or an analogue face (`light`, `dark`).
- `WidgetChoices` (Core) lists the options per kind and gives the default:
  - a new Toggle gets the first toggle not already on the desktop (all used → `wifi`);
  - a new World clock gets `UTC`;
  - the analogue clock defaults to `light`.
  - An unknown or missing choice falls back to the default (sanitised on load).
- World-clock options are a fixed list of about 24 Windows zone ids, one per common offset from UTC−10 to UTC+13
  (including +05:30, +05:45, +09:30), labelled in the list by the zone's Windows display name. A zone missing on this PC
  is skipped.
- **Choosing:** in edit mode, a click without moving on a Toggle, World clock or Analogue clock opens a **choice list**
  under the widget, in the same black popup style as ＋ Add, with the current choice ticked. A pick saves the config and
  rebuilds the widget; Esc or a click outside closes it. (The folder's "click steps to the next picture" stays as is.)

### Card surfaces (`WidgetCard`)
- `WidgetCard.Wrap(content, surface, round, bare)` with `Surface = Dark | Light | Red`:
  - Dark: today's card (`#1A1A1A`, 1.5 px edge `rgba(255,255,255,0.13)`).
  - Light: `#E8E8E8` with Ink `#101010` content, edge `rgba(0,0,0,0.08)`.
  - Red: `#D71921` with `#FFFFFF` content, no edge.
- Round cards use radius = half the shorter side and a smaller padding (10) so content fits the circle.
- `IDesktopWidget` gains `Surface Surface => Dark` and `bool Round => false`. A widget whose surface changes (a toggle
  turning on) rebuilds only its own card.

### Dot primitives
- **DotRing** (new, `NotWin/Notch`): N dots on a circle; `Lit` count drawn in a lit brush (red or ink/text), the rest in
  a dim brush. Core `DotRing.Points(n, radius)` gives the positions (tested). Used by the Stopwatch and Sun compass.
- `DotMatrix` and `DotArtView` as today; new dot pictures (sun, moon, arrow) go in `DotArt`.

## The eight widgets

### 1. Toggle (repeatable; 1×1 circle, 2×1 pill)
- **Choices:** Wi-Fi, Bluetooth, Dark mode, Keep awake, Mic access, Camera access.
- **Look:**
  - 1×1: a line icon, centred.
  - 2×1: icon on the left; the name ("Wi-Fi") and the state ("On" / "Off" / "Blocked") on the right. Never an SSID or
    device name.
  - Off: Dark card, light icon. On: Light card, ink icon.
  - Mic / Camera: allowed = Light, **blocked = Red** with a slashed icon.
  - Unavailable (no radio, access denied): Dark with a dim icon; a click opens the matching `ms-settings:` page.
- **Click** (outside edit mode) flips it:
  - Wi-Fi / Bluetooth: `RadioService.SetAsync`.
  - Dark mode: HKCU `…\Themes\Personalize` `AppsUseLightTheme` and `SystemUsesLightTheme` (both 0 = dark), then
    broadcast `WM_SETTINGCHANGE` with `"ImmersiveColorSet"`.
  - Keep awake: the same `KeepAwakeService` as quick settings, so both stay in step.
  - Mic / Camera: HKCU
    `Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\{microphone|webcam}`:
    - "allowed" = `Value` and `NonPackaged\Value` both `Allow`;
    - blocking writes `Deny` to both, allowing writes `Allow` to both.
    - If the machine-wide HKLM switch is `Deny`, the tile shows Blocked and a click opens
      `ms-settings:privacy-microphone` / `privacy-webcam` (it can't be changed without admin).
- **Staying current without polling:**
  - radios: `Radio.StateChanged`;
  - dark mode: `SystemEvents.UserPreferenceChanged`;
  - keep awake: `KeepAwakeService.Changed`;
  - mic / camera: a registry change notification (`RegNotifyChangeKeyValue`) on the two keys while the tile exists.
- **Check before building on it:** whether running desktop apps honour a mic / camera change at once is verified on the
  user's PC, only with their OK, since it changes a privacy setting.

### 2. Analogue clock (1×1, 2×2 circle; choice: light / dark face)
- No numerals.
  - 12 tick marks; the 12, 3, 6 and 9 ticks are longer.
- **Light face:** Light card, ink hour and minute hands, a thin red seconds hand. It ticks once a second, and only while
  not paused.
- **Dark face:** Dark card, chunky light hands with rounded ends, a single red dot at the centre. No seconds hand, so it
  ticks once a minute.
- Hand angles come from Core `ClockHands.Angles(TimeOnly)` (tested).

### 3. World clock (repeatable; 1×1 circle, 2×1 pill; choice: time zone)
- **1×1:** dot `HH:MM` for that zone, then under it the offset label ("GMT+1") and the day (`TUE`).
- **2×1:** dot `HH:MM` on the left. On the right: the day ("TUESDAY", mono caps), the offset label, and the difference
  from local time ("+4H 30M", "SAME TIME", "−1H").
- Core `ZoneLabels`:
  - `GmtLabel(offset)`: `GMT`, `GMT+1`, `GMT−3`, `GMT+5:30`, with daylight saving applied for "now";
  - `Difference(local, zone)`.
- Ticks once a minute, on the minute (like `ClockWidget`). No city is shown on the tile; the choice list shows Windows'
  zone names.

### 4. Stacked clock (1×2 tall pill, 1×1 circle)
- Dark card. `HH` above `MM` in large dot matrix, centred. The 1×1 version is smaller. Ticks once a minute.

### 5. Stopwatch (1×1 circle, 2×1 pill)
- A `StopwatchService` (in memory; not saved; starts at zero each launch). Its logic sits in Core `StopwatchModel` with
  an injected clock (tested).
- **1×1:** a ring of 60 dots, with the seconds of the current minute lit red. In the middle, dot `MM:SS`
  (`H:MM` once past an hour).
- **2×1:** the ring on the left. On the right: "STOPWATCH", the dot time, and the state ("TAP TO START" / "RUNNING" /
  "PAUSED · DOUBLE-CLICK TO RESET").
- **Click:** start / pause. **Double-click while paused:** reset.
- Redraws once per elapsed second, timed to the next whole second, only while running and not paused.

### 6. Screen time (2×1, 2×2)
- **What counts:** time the user is at the PC while NotWin runs.
  - A minute counts when the session is unlocked and the last input (`GetLastInputInfo`) was under 5 minutes ago.
  - Sampled once a minute by one app-wide `ScreenTimeTracker`; lock / unlock from `SystemEvents.SessionSwitch`.
  - It runs whether or not the widget is on the desktop, so the week is filled in when the widget is added later.
- **Storage:** `screentime.json` in the data folder, `{ "yyyy-MM-dd": minutes }`.
  - Only the last 8 days are kept.
  - Written at most once every 5 minutes, and on exit.
  - Not synced.
  - Day rollover is at local midnight.
- Core `ScreenTimeLog` does the accumulation, trimming and the week view (tested).
- **2×1:** big dot "6H20", label "TODAY", and a small "AVG 5H10" for the last 7 days.
- **2×2:** "6H20 · TODAY" on top, and a 7-day dot chart under it:
  - one column per day, one dot per hour (rounded, up to 12);
  - today's column is red, the other days light;
  - day letters (M T W T F S S) underneath.
- Redraws when the tracker changes (once a minute at most).

### 7. System meters (2×1, 2×2)
- **CPU %:** from `GetSystemTimes` deltas.
- **RAM % used:** `GlobalMemoryStatusEx`.
- **Network:** down and up bytes per second, summed over non-loopback interfaces that are up (`NetworkInterface`
  IPv4 statistics deltas).
- Core `MeterMath` does the deltas and the formatting ("1.2 MB/s", "340 KB/s"), and `DotBar.Lit(value, dots)`
  (tested).
- **2×1:** three rows, CPU / RAM / NET: a caps label, a 10-dot bar and the value. A bar's lit dots turn red above 90%.
- **2×2:** CPU history as dot columns (last 20 samples × 8 rows), then the RAM and NET bars.
- Samples every 2 s, and only while the widget exists and isn't paused. Adapter names are never shown.

### 8. Sun compass (1×1, 2×2 circle)
- A compass face pointing at the sun instead of north:
  - Dark card with a ring of dots, "N" in red at the top, and tiny E / S / W;
  - a dot arrow from the centre toward the sun's direction (azimuth).
  - When the sun is down, the arrow is dim and the 2×2 shows "BELOW HORIZON".
- **2×2** adds "SUNRISE 06:42", "SUNSET 17:58" and "SUN 34°" (elevation).
- **Place:** the latitude and longitude from the weather setting (`WeatherService.Place`). The city is never shown.
  With no city set: "SET A CITY IN SETTINGS".
- Core `SolarPosition`:
  - `Position(utc, lat, lon)` → azimuth and elevation (NOAA algorithm);
  - `SunTimes(date, lat, lon, zone)` → sunrise and sunset, or none for polar day / night.
  - Tested against NOAA reference values within ±1° / ±2 min.
- Redraws every 5 minutes (the sun moves about 1.25° in that time), and when the weather place changes.

## Testing
- **Core (xUnit):**
  - kinds, sizes, repeatable and picker order;
  - `IsRound`;
  - `WidgetChoices` (defaults, first unused toggle, sanitising);
  - `ZoneLabels`, `ClockHands`, `StopwatchModel`, `ScreenTimeLog`, `MeterMath` / `DotBar`, `SolarPosition`,
    `DotRing`;
  - layout sanitising with the new kinds and the 1×2 size.
- **App (STA tests, like `WidgetRootTests`):**
  - `WidgetCard` surfaces and round radius;
  - each widget builds at each of its sizes without throwing;
  - the choice list's options for each kind.
- `dotnet test NotWin.sln` green before asking to reinstall.

## Left to the builder
- Exact dot sizes and gaps per size, the line icons' geometry, and the choice popup's look (within the Add picker's
  style).
- Picker order within the groups above.
- Whether the registry watcher for mic / camera is one shared watcher or one per tile.

## Follow-up (user, 2026-11-26, after the first install): dark mode drives the wallpaper and widget colours; on = red

**What the user asked:** "use dark mode to change a black wallpaper, change widgets colours" and "when a toggle is on
mark it red". Answers to the follow-up questions: black wallpaper + widgets follow; the wallpaper is NotWin's own dark
dot art; a blocked mic / camera is a plain card with a slashed icon.

### Dark mode tile
- **On:** remember the current wallpaper (saved in settings), then apply the bundled `nothing_halftone_dark.png`
  (`<app>\Wallpapers`). Windows goes dark as today.
- **Off:** Windows goes light; the remembered wallpaper comes back if its file still exists (if there was none — a solid
  colour or slideshow — the wallpaper is left as it is). The remembered path is then cleared.
- The wallpaper changes **only** from a click on this tile (project rule: never change the wallpaper unless the user
  does it). Changing the theme in Windows Settings recolours the widgets but leaves the wallpaper alone.

### Widget colours follow Windows' app theme
- Dark theme: today's look (`#1A1A1A` cards, light text). Light theme: every card that is dark today becomes light
  (`#E8E8E8`, ink `#101010` text and dots, a faint dark edge). Red stays red. The bare clock keeps sampling the wallpaper.
- Done with theme brushes that widgets share (card, text, dim, unlit dots): a theme change recolours every widget in
  place, without rebuilding. Edit-mode chrome and the Add / choice popups stay dark.
- The analogue clock's own face choice (light / dark) is kept as chosen.

### Toggle colours
- **On = red** card with white icon and text. **Off** = the theme's card. **Blocked** mic / camera (by this user or the
  machine) = the theme's card with the slashed icon and "Blocked". **Unavailable** = the theme's card, dim icon.

### Non-goals
- Per-widget colour choices; changing the dock, notch, launcher or quick-settings colours with the theme.

### Second follow-up (user, 2026-11-26, after seeing light mode)
- "fix the widgets for white theme": every widget's text, icons and dots draw in the theme's ink in light mode (Up next,
  Snake, folders, the dark analogue face and the rest were still white-on-light).
- "use a white wallpaper for white theme": the Dark mode tile now pairs NotWin's dark dot wallpaper (dark mode) with its
  white dot wallpaper (light mode); the remembered-wallpaper idea is dropped.
- "why the taskbar is black, change its colour too; everything should softly blend": the dock follows Windows' theme
  (a soft #EDEDED pill with ink glyphs in light mode; black as before in dark mode).
- Not changed yet: the notch pill and its dashboard, the launcher and quick settings stay dark — offered as the next step.

### Third follow-up (user, 2026-11-26): Lucent light mode, blur in both modes
- Light mode separated nothing from the halftone wallpaper. User picked "Lucent" (Nothing OS 5 style) and asked for
  blur on the wallpaper in both modes.
- Two new bundled wallpapers, `nothing_lucent_light` / `nothing_lucent_dark` (wallpapers/make_wallpapers.py, section E):
  soft blurred tinted blobs with film grain, nothing sharp behind widgets. The Dark mode tile switches between them.
  Both are also in Settings › Wallpaper.
- Cards are frosted: white at ~90% opacity in light mode, #222 at ~88% in dark mode; unlit dots lighter in light mode.
  The dock pill is frosted too. (A live blur behind desktop widgets isn't possible for these windows, so the wallpaper
  itself is the blurred layer.)

### Fourth follow-up (user, 2026-11-26): the rest of the shell follows the theme; the wallpaper follows the photo
- The notch pill, quick settings (Win+A / Win+N), the dock's window preview, the Notes widget and editor, and the
  round photo frame's ring follow light / dark (Theme/ShellTheme "Shell.*" resources; code-built parts bind to them
  with SetResourceReference, so an open panel recolours at once). The notch dashboard's tiles stay dark for now.
- **Photo-tinted Lucent wallpaper:** whenever a photo frame shows a photo (each slideshow step, about once a minute) or
  the theme flips, NotWin draws a soft blurred wallpaper from the photo's three strongest colours (pastel on light grey
  in light mode, deep on charcoal in dark mode, film grain) and sets it (`%AppData%\NotWin\lucent\lucent_tint_*`,
  two files used in turn). **Builder's call:** only while one of NotWin's own Lucent wallpapers (bundled or tinted) is
  showing — a wallpaper the user picked is never replaced; picking a Lucent wallpaper (Dark mode tile or Settings) turns
  it on, picking any other turns it off.

### Fifth follow-up (user, 2026-11-26): smoother wallpaper changes, notch tiles, a better snake
- Wallpaper changes from NotWin (photo tint, Dark mode tile) use Explorer's cross-fade (IActiveDesktop after the
  Progman 0x052C message), falling back to the instant change. The tint's "is this our wallpaper" check reads the
  Active Desktop source path, since Windows may report its cached TranscodedWallpaper copy.
- The notch dashboard's tiles follow light / dark (Notch/Dashboard/DashChrome: TileChrome's API with theme colours;
  TileChrome itself stays dark for Settings and the edit popups).
- Snake: a theme-aware shade between games (no grey slab in light mode), quieter empty dots, a fading tail and a
  slightly larger head, a gently pulsing red food dot, "BEST n" / "NEW BEST" (red) on game over, and it speeds up as you
  score (120 ms, 3 ms quicker per point, never under 70 ms).

### Sixth follow-up (user, 2026-11-26): the launcher and the remaining popups
- The launcher (start menu) follows light / dark: its window uses Shell.* DynamicResources (glass, search field, app
  circles, selected row, placeholder, scrim, best-match card); the settings gear is drawn per theme.
- The Alarm editor, the app-folder popup and app chooser, and the photo frame's manage popup follow the widgets' theme
  (white cards with ink text in light mode). The ringing-alarm card stays dark, as an alert.

### Seventh follow-up (user, 2026-11-26): Settings › Theme, no more wallpaper picker
- Settings has a **Theme** row (Light / Dark) that does exactly what the Dark mode tile does (Windows' theme + the
  matching Lucent wallpaper, cross-faded); the tile and the row share `DarkModeToggle.SetAsync`.
- The Settings window follows light / dark (DashChrome + Shell.Glass) and rebuilds itself when the theme changes.
- The **Wallpaper** section is gone, with the "✦ New" maker (`WallpaperMaker`, `WallpaperArt`) and the four old dot
  wallpapers; only the two Lucent wallpapers ship. Wallpapers the user made earlier stay in their AppData folder.

### Eighth follow-up (user, 2026-11-27): folder icons and the notch clock
- Research (docs/references/widget-research.md): Nothing OS folders show their apps as monochrome icons; the Phone (3)
  Glyph Matrix; islands stay black. Mockups of three folder and three notch options were shown; the user picked
  **folder C, "Peek + name"**, and for the notch only asked to swap the dot clock for the serif of Nothing's date widget.
- Folder face: the apps inside as monochrome icon circles (1×1: up to three, the last "+N" when there are more; 2×2: up
  to six), then the folder's name (mono caps) and "N APPS" ("ADD APPS" when empty). The name is set at the top of the
  folder's popup (`WidgetConfig.Title`, max 24). The old dot pictures and their edit-mode stepping are gone; the face
  redraws when apps or the name change.
- Notch: the resting clock is DM Serif Display (the bundled OFL serif closest to Nothing's NType 82, which can't ship).

### Ninth follow-up (user, 2026-11-27): quick, smooth motion everywhere; notch tiles
- One motion helper (Theme/Motion.cs): popups pop in (fade + 0.96→1 scale + 6 px rise, 170 ms ease-out) and leave
  quicker (110 ms fade); all instant when Windows' animation effects are off. `Motion.Smooth(window)` on every popup:
  dock menus, ＋ Add, the choice list, Notes / Alarm editors, folder popup and app chooser, photo popup, Settings, the
  ringing alarm. `Motion.CloseNow` skips the fade where a popup must be gone first (photo ＋ → file picker; Settings
  rebuilding itself after a theme change).
- Quick settings and the start menu gained a 110 ms fade-and-drop exit (a fading window counts as closed, so a
  re-open is instant); their open is 170 ms.
- Notch: resize 300 → 240 ms; page swipes 280 → 220 ms; tiles fade up in an 18 ms stagger when the dashboard opens;
  tiles lift (1.02) on hover and dip (0.98) on press; the built page is reused when nothing changed (layout, page,
  edit mode, theme), so opening the notch no longer rebuilds every tile.

### Tenth follow-up (user, 2026-11-27): better notch tiles, weather overlap

"Make the tiles better in the notch, the weather tile temp degree is overlapping."

- Weather 2×1 / 2×2: two rows. Icon and city on top (shortened with "…" only when really long; "Updated HH:mm" on the
  right when stale), the dot temperature below with the high and low stacked beside it. The dot minus is 3 dots wide,
  not 5, so "-12°" fits. Tests: the temperature never overlaps the city; a usual city and the high / low show in full.
- Tool tiles (1×1): the icon sits in a round chip, like the 2×1 tile.
- Keep awake (1×1): red with a white icon while on, like the desktop toggles.
- Shelf: the empty-state hint wraps inside the drop area instead of being cut off.

### Eleventh follow-up (user, 2026-11-27): polish the music / clipboard / notes / timer page

- Clipboard: an image row shows its thumbnail and pixel size ("970 × 546"); the "IMAGE ·" prefix is gone because the
  thumbnail already says so, and the size used to be cut off.
- Music 2×2: the seek dots spread edge to edge across the tile at any display scale (DotBar.Spread); still 20 steps.
- Timer 2×2: finer grid dots (0.5 of the pitch, desktop widget unchanged at 0.64); − and + are round 30 px chips.
- Notes: unchanged.

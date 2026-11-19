# Plan 5: Desktop Widgets, Edit Mode, Scale and Wallpaper Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Seven desktop widgets sit on the desktop layer, under every app window and above the wallpaper: Clock, Snake, Timer, Calendar, Weather, Media and Battery. They can be dragged and resized in an edit mode. Settings gains widget scale (S/M/L) and the four Nothing wallpapers.

**Architecture:**
- **Pure logic in `NothingShell.Core`, unit-tested:**
  - the widget layout (sizes, 74 px snap, overlap refusal, sanitizing);
  - the Snake game and its key gate;
  - the calendar month grid;
  - battery device pages;
  - clock ink from wallpaper brightness;
  - weather dot glyphs.
- **Desktop host:** each widget is its own small child window of the desktop (`Progman`, the window hosting `SHELLDLL_DefView`), attached the way spike Option A proved (`0x052C`, `SetParent`, `HWND_TOP`). Each window is an `HwndSource` with per-pixel transparency. Small surfaces keep redraws cheap, and child windows never appear in Alt+Tab (spike D.4 failed with a top-level window).
- **Widgets reuse Plan 3–4 services:** `TimerService`, `MediaSessionService`, `WeatherService`, `TrayStatusService`, plus a new `DeviceBatteryService` for Bluetooth battery.

**Tech Stack:**
- .NET 10, WPF and CsWin32, as before.
- WinRT `Windows.Devices.Enumeration` for the Bluetooth battery property.
- `SystemParametersInfo` for the wallpaper.
- **No new NuGet packages.**

**Spec:** `docs/superpowers/specs/2026-11-17-nothing-shell-design.md`. Read:
- §5 (palette, type, grid unit 136 + 12 gap, scale S 50% / M 65% / L 80%, wallpapers);
- §6.5 (widgets table, desktop icons, weather, timer defaults, edit mode);
- §6.6, §6.7 (Settings: widget scale, wallpaper);
- §7 (widget host on the desktop layer, performance rules).

Also read:
- `docs/superpowers/spikes/2026-11-17-phase0-findings.md`, "Spike: desktop layer" (Option A; D.3 re-attach 106 ms; D.4 Alt+Tab FAIL);
- `spikes/DesktopLayerSpike/Program.cs` (`Attach`, `Watch`);
- `docs/mockups/final-v2.html` lines 289–375: `W` default layout, `SNAP=74`, `dims`, `overlaps`, `renderClock / Timer / Cal / Weather / Media / Battery`, the snake code, drag-to-place;
- the `G` glyph table at line 262 for the `c` / `r` / `m` weather glyphs.

**Branch:** `plan5-desktop-widgets`, created from `plan4-notch-dashboard` (PRs #1–#4 are still open). Ask the user before pushing.

**Out of scope (Plan 6, user decision 2026-11-19):** Google Calendar sign-in, events, event dots and "Up next". In Plan 5 the calendar shows dates only. The 2×1 calendar shows the date with "No events" instead of "Up next".

## Global Constraints

- **Desktop layer** (spec §6.5, §7):
  - widgets sit under every app window, are visible on an empty desktop and after Win+D, and are clickable there;
  - they survive Explorer restarts;
  - the user keeps no desktop icons, so widgets may cover the icon view;
  - **never in Alt+Tab** (spike D.4).
- **Look** (spec §6.5):
  - black cards `#1A1A1A` with a 1.5 px inner edge `rgba(255,255,255,0.13)`, radius **26**, padding 16;
  - the clock has **no card**: dot-matrix HH:MM drawn on the wallpaper, **dark dots on light wallpapers, light dots on dark**;
  - red `#E8231F` is the only accent (today, the day name, low battery, snake food, the timer's next dot);
  - Geist / Geist Mono; **DM Serif Display** for the calendar's dates and header;
  - numbers in dot matrix only.
- **Grid** (spec §5, mockup):
  - widget unit **136 px + 12 px gap**, so `Px(n) = n·136 + (n−1)·12`;
  - positions snap to **74 px** (half a cell);
  - all in unscaled pixels, drawn at the global scale **S 0.5 / M 0.65 (default) / L 0.8** and then by the monitor's DPI;
  - the layout's origin is the primary work area's top-left plus **(80, 56)** unscaled.
- **Default layout** (mockup `W`):
  - Clock **3×1** at (0, 0);
  - Snake **2×2** at (0, 148); Timer **2×2** at (296, 148); Calendar **2×2** at (592, 148); Weather **2×1** at (888, 148);
  - Media **2×1** at (888, 296);
  - Battery **1×1** at (0, 444).
- **Allowed sizes**, first = default:

  | Widget | Sizes |
  |---|---|
  | Clock | 3×1, 2×1, 4×2 |
  | Snake | 2×2 (14×14 board), 3×3 (20×20) |
  | Timer | 2×2, 1×1, 2×1 |
  | Calendar | 2×2, 2×1, 1×1 |
  | Weather | 2×1, 1×1, 2×2 |
  | Media | 2×1, 1×1, 2×2 |
  | Battery | 1×1, 2×1, 2×2 |
- **Edit mode** (spec §6.5):
  - a dot grid appears on the desktop;
  - drag any widget, clock included; it snaps to 74 px;
  - **overlaps are refused**: the outline turns red, and on release the widget returns to where it was;
  - size chips cycle sizes; a resize that causes an overlap shows a red outline until the widget is moved;
  - layout, sizes and scale are saved across restarts.
- **Snake** (spec §6.5):
  - playable; click to focus; arrows / WASD; Space pauses or restarts;
  - white snake, red food, score in dots;
  - **pauses automatically when focus leaves**;
  - the best score is saved.
- **Timer widget:** grid style, 12×12 dots at 2×2; the next dot red; flashes red at zero. It is the same `TimerService` as the notch (spec §6.6).
- **Battery widget:**
  - **ring plus device icon, no number**;
  - one device fills the tile; with more devices, page dots on the right, switched by dot or scroll;
  - jumps to a newly connected device;
  - ring and icon red under **20%**; ⚡ when charging; the tooltip shows the exact %;
  - devices that report no battery show no ring (spec §9).
- **Weather widget:** dot-drawn condition icon, temperature, high/low, a 5-day row. Offline shows the last data dimmed with "updated hh:mm". It reuses `WeatherService` and the city from Settings.
- **Wallpaper** (spec §5, §6.7): the four `wallpapers/*.png`, applied to Windows' desktop only when the user picks one in Settings.
- **Performance** (spec §7): no idle timers faster than 1 s; snake and countdown render only while active. Budget: **under 100 MB** total working set and about 0% CPU idle after 10 min.
- **Live checks:** implementers never start, kill or message the user's running installed shell, never restart Explorer, and never change the user's wallpaper. Those are hands-on steps (Task 12). Offscreen renders are fine.

## Review Focus

1. **Snake keys.** Arrows must reach the game only while it is armed by a click, and keystrokes must never be eaten otherwise. Clicking anywhere else, or focus moving to any window, pauses and disarms it. *(Task 2 tests `Gate_OnlyArmedSwallows`, `Gate_ForegroundChange_DisarmsAndPauses`.)*
2. **Explorer restart and Win+D.** Widgets reattach within 2 s of the new desktop window appearing, stay clickable after Win+D, and never show in Alt+Tab. *(Task 5 `DesktopAttachTests.ChildStyle_IsNotATopLevelWindow` plus hands-on DW.3/DW.4.)*
3. **A different screen.** After a resolution, DPI or scale change, a saved layout must come back on screen. Off-screen widgets are clamped, and overlaps are flagged, not lost. *(Task 1 `Sanitize_ClampsOntoTheArea`, `Sanitize_KeepsOverlapsFlagged`.)*
4. **Clock on any wallpaper.** The ink flips with the wallpaper under the clock: dark dots on a light area, light on dark, recomputed on a wallpaper change or a move. *(Task 3 `ClockInk_FlipsAtMidGrey`.)*
5. **Battery devices that come and go**, or report no level. A device disconnecting while shown returns to the laptop page, a newly connected one is jumped to, and no-level devices show no ring. *(Task 3 `BatteryPages_*`.)*

---

## File Structure

```
src/NothingShell.Core/
  Widgets/WidgetLayout.cs      WidgetKind, WidgetSize, WidgetPlacement, DesktopLayout (defaults, snap, move, cycle, sanitize)
  Widgets/SnakeGame.cs         board, step, turns, food, states; SnakeKeyGate
  Widgets/CalendarMonth.cs     6x7 month grid
  Widgets/BatteryPages.cs      device pages, jump rules
  Widgets/ClockInk.cs          ink from luminance
  Notch/DotFont.cs             (modify) weather glyphs c m r + s (sun) w (snow) t (storm) f (fog)
  Forecast/WeatherRules.cs     (modify) WeatherRules.Glyph(kind, isDay)
  ShellSettings.cs             (modify) Widgets, SnakeBest
src/NothingShell.Native/
  DesktopAttach.cs             find the DefView host, 0x052C, child styles, SetParent, HWND_TOP
  WallpaperNative.cs           SPI_GETDESKWALLPAPER / SPI_SETDESKWALLPAPER, WallpaperStyle
src/NothingShell/
  Widgets/WidgetHost.cs        one child HwndSource per widget, attach/reattach, place, scale
  Widgets/DesktopWidgets.cs    controller: layout, settings, edit mode, services
  Widgets/EditBackdrop.cs      dot grid child window (edit mode only)
  Widgets/WidgetCard.cs        card chrome (26 radius, 1.5 edge), size chip, red outline
  Widgets/{Clock,Timer,Weather,Media,Calendar,Battery,Snake}Widget.cs
  Widgets/DeviceBatteryService.cs  laptop + Bluetooth battery list
  Widgets/WallpaperSampler.cs  mean luminance of the wallpaper under a rect
  Settings/SettingsWindow.cs   (modify) widget scale, wallpaper, "Edit widgets"
  Launcher/WinKeyListener.cs   (modify) route armed snake keys
build/install.ps1              (modify) install wallpapers/*.png
build/verify/plan1-handson.ps1 (modify) Widgets section
```

---

### Task 1: Widget layout model

**Files:**
- Create: `src/NothingShell.Core/Widgets/WidgetLayout.cs`
- Modify: `src/NothingShell.Core/ShellSettings.cs` (`DesktopLayout? Widgets`)
- Test: `tests/NothingShell.Core.Tests/Widgets/WidgetLayoutTests.cs`, `SettingsStoreTests.cs` (add)

**Interfaces:**
- Produces:
  - `enum WidgetKind { Clock, Snake, Timer, Calendar, Weather, Media, Battery }`, serialized with the existing `TolerantEnumConverter<T>`.
  - `readonly record struct WidgetSize(int W, int H)`, with `static int Px(int cells)` (= `n·136 + (n−1)·12`), `int PxW`, `int PxH`.
  - `WidgetRules.Sizes(WidgetKind) → IReadOnlyList<WidgetSize>` (Global Constraints table, first = default).
  - `sealed record WidgetPlacement(WidgetKind Kind, WidgetSize Size, int X, int Y)`, in unscaled px relative to the layout origin.
  - `sealed record DesktopLayout(IReadOnlyList<WidgetPlacement> Widgets)` with:
    - `static DesktopLayout Default`;
    - `const int Snap = 74, Gap = 6`;
    - `bool Overlaps(int index)` (the mockup's `overlaps`: rects grown by `Gap` on the right and bottom);
    - `static int SnapTo(double v)`;
    - `(DesktopLayout Layout, bool Moved) TryMove(int index, double x, double y, int areaW, int areaH)`: snap, clamp so the widget stays inside the area, and refuse (`Moved = false`, unchanged) when the result overlaps;
    - `DesktopLayout CycleSize(int index)` (the next allowed size, even if it overlaps);
    - `static DesktopLayout Sanitize(DesktopLayout? loaded, int areaW, int areaH)`.
  - `ShellSettings.Widgets`.

- [ ] **Step 1: Confirm the branch.** `plan5-desktop-widgets` was created from `plan4-notch-dashboard` with this plan. Run `git branch --show-current`. Expected: `plan5-desktop-widgets`.
- [ ] **Step 2: Write the failing tests.**
  - `Default_MatchesMockup`: the seven placements and sizes from Global Constraints, in that order.
  - `Px_Formula`: `Px(1) = 136`, `Px(2) = 284`, `Px(3) = 432`, `Px(4) = 580`.
  - `SnapTo_HalfCell`: 36 → 0, 38 → 74, 300 → 296.
  - `TryMove_SnapsAndMoves`: Battery to (520, 470) → (518, 444).
  - `TryMove_RefusesOverlap_StaysPut`: Snake onto the Timer's spot gives `Moved = false` and the layout unchanged.
  - `TryMove_ClampsInsideTheArea`: a far drag ends with `X + PxW ≤ areaW`.
  - `CycleSize_AllowsOverlap_ThenFlagged`: Timer 2×2 → 1×1 → 2×1; growing Weather to 2×2 at (888, 148) overlaps Media, and `Overlaps(index)` is true.
  - `Sanitize_ClampsOntoTheArea`: a widget at (5000, 3000) in a 1200×700 area ends inside it.
  - `Sanitize_KeepsOverlapsFlagged`: two overlapping widgets are both kept.
  - `Sanitize_DropsUnknownAndDuplicates_AddsMissing`: unknown kind −1 and a duplicate Clock are dropped; missing kinds are added at their default spots, or at the first free snapped spot if that overlaps.
  - `Sanitize_Null_Default`.
  - `SaveThenLoad_RoundTripsWidgets`.
- [ ] **Step 3: Run them and confirm they fail.** Run `dotnet test tests/NothingShell.Core.Tests --filter "WidgetLayout|SettingsStore"`. Expected: compile errors.
- [ ] **Step 4: Implement.** The first free spot scans rows, then columns, in 74 px steps.
- [ ] **Step 5: Run the tests.** Expected: pass.
- [ ] **Step 6: Commit.** Message: `feat: desktop widget layout model (sizes, 74 px snap, overlap refusal, sanitize)`.

### Task 2: Snake game and key gate

**Files:**
- Create: `src/NothingShell.Core/Widgets/SnakeGame.cs`
- Modify: `ShellSettings.cs` (`int SnakeBest`)
- Test: `tests/NothingShell.Core.Tests/Widgets/SnakeGameTests.cs`

**Interfaces:**
- Produces:
  - `enum SnakeState { Idle, Play, Paused, Over }`
  - `SnakeGame(int size, Random rng)`:
    - `int Size`, `IReadOnlyList<(int X, int Y)> Body` (head first), `(int X, int Y) Food`, `int Score`, `int Best`, `SnakeState State`;
    - `void Turn(int dx, int dy)` (a reversal is ignored);
    - `void Step()`: on Play, move one cell; eating grows the snake and places new food off the body; hitting a wall or itself → Over and `Best = max`;
    - `void Click()` (Idle / Paused → Play; Over → reset and Play);
    - `void Space()` (Play ↔ Paused; Over → reset and Play);
    - `void Pause()`;
    - `void Resize(int size)` (reset; Play becomes Paused).
  - The start position is the mockup's `snakeReset`: head at (size/2, size/2) moving right, length 3.
  - `SnakeKeyGate`:
    - `bool Armed`, `void Arm()`, `void Disarm()`;
    - `SnakeKey? Map(int vk)` → Up / Down / Left / Right / Space for arrows, WASD and Space; null otherwise;
    - `bool ShouldSwallow(int vk)` = `Armed && Map(vk) is not null`.
  - The tick is **120 ms** (mockup `setInterval(snakeStep, 120)`), run by the widget only in Play.

- [ ] **Step 1: Write the failing tests.**
  - `Start_CentredMovingRight_Length3`.
  - `Step_MovesHead`.
  - `Turn_ReverseIgnored`.
  - `Eat_GrowsAndScores_FoodNotOnBody` (seeded `Random`).
  - `Wall_EndsGame_UpdatesBest`.
  - `Self_EndsGame`.
  - `Space_TogglesPause_AndRestartsAfterOver`.
  - `Resize_ResetsAndPauses`.
  - `Gate_OnlyArmedSwallows`: unarmed → `false` for arrows; armed → `true` for arrows, WASD and Space, `false` for Enter and letters.
  - `Gate_ForegroundChange_DisarmsAndPauses`: covered by the widget calling `Disarm()` and `game.Pause()`. In Core, assert that `Disarm()` makes `ShouldSwallow(Up)` false.
- [ ] **Step 2: Run them and confirm they fail.** Then implement and confirm they pass. Commit `feat: snake game and key gate`.

### Task 3: Calendar grid, battery pages, clock ink, weather glyphs

**Files:**
- Create: `src/NothingShell.Core/Widgets/CalendarMonth.cs`, `BatteryPages.cs`, `ClockInk.cs`
- Modify: `src/NothingShell.Core/Notch/DotFont.cs`, `src/NothingShell.Core/Forecast/WeatherRules.cs`
- Test: `tests/NothingShell.Core.Tests/Widgets/{CalendarMonth,BatteryPages,ClockInk}Tests.cs`, `Notch/DotFontTests.cs`, `Forecast/WeatherRulesTests.cs` (add)

**Interfaces:**
- Produces:
  - `CalendarMonth.Build(int year, int month, DateOnly today) → IReadOnlyList<CalendarCell>` with `record CalendarCell(int Day, bool InMonth, bool IsToday)`. It has 35 or 42 cells, weeks start on **Sunday** (mockup `S M T W T F S`), and leading and trailing days of the neighbouring months are muted.
  - `record BatteryDevice(string Id, string Name, string Icon, int? Percent, bool Charging, bool IsLaptop)` (`Icon` ∈ `laptop, head, mouse, speaker`).
  - `BatteryPages`:
    - `IReadOnlyList<BatteryDevice> Devices` (laptop first, then by name; devices with null `Percent` are kept but drawn without a ring);
    - `int Current`;
    - `void Update(IReadOnlyList<BatteryDevice> now)` (jumps to a newly connected device; if the current one vanished → the laptop page);
    - `void Go(int index)` (clamped);
    - `void Scroll(int delta)`.
  - `ClockInk.For(double luminance) → bool DarkDots` (true when luminance ≥ 0.5, on 0..1 relative luminance).
  - `DotFont` glyphs: the mockup's `c` (cloud), `r` (rain), `m` (moon); plus `s` (sun), `w` (snow), `t` (storm), `f` (fog) as 7-row glyphs in the same style.
  - `WeatherRules.Glyph(WeatherKind kind, bool isDay) → char`:
    - Clear → `s` by day, `m` by night;
    - PartlyCloudy / Cloudy → `c`;
    - Drizzle / Rain → `r`;
    - Snow → `w`; Storm → `t`; Fog → `f`.

- [ ] **Step 1: Write the failing tests.**
  - `November2026_StartsSunday1_Has35Cells_TodayMarked`, with today = 2026-11-19.
  - `February2026_LeadingDaysMuted`.
  - `BatteryPages_NewDevice_JumpsToIt`.
  - `BatteryPages_CurrentVanishes_BackToLaptop`.
  - `BatteryPages_Scroll_Clamped`.
  - `BatteryPages_NoPercent_KeptWithoutLevel`.
  - `ClockInk_FlipsAtMidGrey`: 0.49 → false, 0.5 → true.
  - `DotFont_HasWeatherGlyphs`: `c r m s w t f` each with 7 rows.
  - `Glyph_MapsKinds` (a theory).
- [ ] **Step 2: Run them and confirm they fail.** Then implement and confirm they pass. Commit `feat: calendar grid, battery pages, clock ink, weather dot glyphs`.

### Task 4: Wallpaper and scale in Settings

**Files:**
- Create: `src/NothingShell.Native/WallpaperNative.cs`
- Modify: `src/NothingShell/Settings/SettingsWindow.cs`, `build/install.ps1` (copy `wallpapers/*.png` to `Program Files\NothingShell\Wallpapers`)
- Test: `tests/NothingShell.Core.Tests/SettingsStoreTests.cs` (`WidgetScale` already exists; add `ScaleFactor_MapsSml`)

**Interfaces:**
- Produces:
  - `WallpaperNative.Current() → string?` (`SPI_GETDESKWALLPAPER`).
  - `WallpaperNative.Apply(string path) → bool`: sets `HKCU\Control Panel\Desktop` `WallpaperStyle = "10"` (Fill) and `TileWallpaper = "0"`, then `SystemParametersInfo(SPI_SETDESKWALLPAPER, path, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE)`.
  - `ShellSettings.ScaleFactor(string scale)`: `"S"` → 0.5, `"M"` → 0.65, `"L"` → 0.8, anything else → 0.65.
  - `SettingsWindow` takes `Action<string> setScale` and a wallpaper folder.

- [ ] **Step 1: Write the failing test** `ScaleFactor_MapsSml`. Run it, confirm it fails, implement, and confirm it passes.
- [ ] **Step 2: Settings.**
  - **Widget size:** an S / M / L segmented pill (the selected one `#F2F2F2` with dark text). It applies live through `setScale`.
  - **Wallpaper:** the four `*_preview.jpg` thumbnails, 2×2 at 88×50, radius 10; the current one gets a 2 px white ring. A click calls `Apply` on the installed 4K PNG.
  - **Edit widgets:** a row that closes Settings and starts edit mode (Task 11).
  - Wallpapers never change unless the user clicks one.
- [ ] **Step 3: Install.** `install.ps1` copies `wallpapers\*.png` and `*_preview.jpg` into `$install\Wallpapers`; `uninstall.ps1` deletes them with the folder.
- [ ] **Step 4: Build, test, and render Settings offscreen.** Commit `feat: Settings: widget size and wallpaper`.

### Task 5: Desktop host

**Files:**
- Create: `src/NothingShell.Native/DesktopAttach.cs`, `src/NothingShell/Widgets/WidgetHost.cs`
- Test: `tests/NothingShell.Tests/DesktopAttachTests.cs`

**Interfaces:**
- Produces:
  - `DesktopAttach`:
    - `static nint FindHost()`: Progman if it has a `SHELLDLL_DefView` child, else the top-level `WorkerW` that has one (spike `FindHost`);
    - `static void Prepare()` (`SendMessageTimeout(Progman, 0x052C, 0xD, 1, SMTO_NORMAL, 1000)`);
    - `static int ChildStyle(int style)` and `static int ChildExStyle(int ex)`: add `WS_CHILD`, remove `WS_POPUP`, `WS_CAPTION` and `WS_EX_APPWINDOW`; add `WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE`;
    - `static bool Attach(nint child, nint host, RectI rect)` (`SetParent`, then `SetWindowPos(HWND_TOP, …, SWP_NOACTIVATE | SWP_SHOWWINDOW | SWP_FRAMECHANGED)`; coordinates are host-client relative).
  - `WidgetHost`, one per widget:
    - `WidgetHost(FrameworkElement content)` creates an `HwndSource` with `UsesPerPixelTransparency = true` and the child styles;
    - `void Place(RectI screenRect)`;
    - `void Reattach()`;
    - `event Action? Lost` (the parent is gone);
    - `void Dispose()`.
  - `DesktopWidgets` (Task 6+) reattaches all hosts on `TaskbarCreated` (the existing taskbar watcher). It also runs a 250 ms check **only while a host is lost**, to time the reattachment. Log `widgets reattached in N ms`.

- [ ] **Step 1: Write the failing test** `ChildStyle_IsNotATopLevelWindow`: `ChildStyle(WS_POPUP | WS_CAPTION)` has `WS_CHILD` and neither `WS_POPUP` nor `WS_CAPTION`; `ChildExStyle(WS_EX_APPWINDOW)` has `WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE` and not `WS_EX_APPWINDOW`. Run it, confirm it fails, implement, and confirm it passes.
- [ ] **Step 2: Implement `WidgetHost` and the attach path.**
  - The host is a child window and never has an owner. That is the fix for spike D.4 (the spike's widget was a top-level window shown in Alt+Tab).
  - DPI: placement is in physical pixels from `GetPrimaryMonitor` (DPI) × scale.
- [ ] **Step 3: Build and test.** Commit `feat: desktop widget host (child of the desktop, reattach)`.

### Task 6: Widget controller, Clock and Timer widgets

**Files:**
- Create: `src/NothingShell/Widgets/DesktopWidgets.cs`, `WidgetCard.cs`, `ClockWidget.cs`, `TimerWidget.cs`, `WallpaperSampler.cs`
- Modify: `App.xaml.cs`

**Interfaces:**
- Consumes: `DesktopLayout` (Task 1); `WidgetHost` (Task 5); `ClockInk` (Task 3); `TimerService`, `GridTimerView`, `DotMatrix` (Plan 4).
- Produces:
  - `interface IDesktopWidget : IDisposable { WidgetKind Kind { get; } FrameworkElement Build(WidgetSize size); }`
  - `DesktopWidgets(Func<ShellSettings>, Action<Func<ShellSettings, ShellSettings>>, IReadOnlyDictionary<WidgetKind, IDesktopWidget>)`, with `void Start()`, `void SetScale(string)`, `void BeginEdit()`, `void EndEdit()`, and `bool Editing`.
  - `WidgetCard.Wrap(FrameworkElement content, bool bare) → FrameworkElement` (`#1A1A1A`, radius 26, 1.5 inner edge `#21FFFFFF`, padding 16; `bare` = no card).
  - `WallpaperSampler.MeanLuminance(string? wallpaperPath, RectI screenRect, RectI monitor) → double?` (decodes at 256 px wide, cached per path; assumes Fill).

- [ ] **Step 1: Controller.**
  - Builds one host per placement at `origin + (X, Y)·scale·dpi`, scaled with `LayoutTransform`.
  - Origin = the primary work area's top-left + (80, 56)·scale.
  - Re-places on display or DPI change and reattaches on Explorer restart (Task 5).
- [ ] **Step 2: Clock** (mockup `renderClock`), bare:
  - a date line "Thu, 19 Nov" in Geist 14 (20 at 4×2), then dot HH:MM;
  - dot size / gap: 12/4 at 3×1, 7/3 at 2×1, 17/5 at 4×2;
  - ink `#141414` when `ClockInk.For(luminance)` says dark dots, else `#F2F2F2`; the date in `#333` or `#BBB` accordingly;
  - updates on the tray's minute tick;
  - recomputes the ink on `WM_SETTINGCHANGE` (wallpaper) and after a move.
- [ ] **Step 3: Timer** (mockup `renderTimer`):
  - **1×1:** `GridTimerView(100, 10)`;
  - **2×1:** grid 100/10, then "TIMER" and dot `mm:ss` (3.4 / 1.2);
  - **2×2:** grid 200/12 and dot `mm:ss` (3 / 1.2) with "RUNNING" or "TAP TO START";
  - a click toggles; it flashes from `TimerService.FlashOn`.
- [ ] **Step 4: Build, test, and render both offscreen** at M scale on a dark and a light background. Commit `feat: desktop widgets host + clock and timer widgets`.

### Task 7: Weather and Media widgets

**Files:**
- Create: `src/NothingShell/Widgets/WeatherWidget.cs`, `MediaWidget.cs`

**Interfaces:**
- Consumes: `WeatherService`, `WeatherRules.Glyph` (Task 3); `MediaSessionService` (Plan 4); `DotMatrix`, `DotBar`.

- [ ] **Step 1: Weather** (mockup `renderWeather`):
  - **1×1:** glyph dots (4 / 1.4), temperature dots (3.4 / 1.2), and the city label;
  - **2×1:** glyph, temperature (Geist 34 Medium), "↑hi ↓lo", city label top right, and the 5-day row (mono day, glyph 2.4 / 0.9, high);
  - **2×2:** city plus condition labels, big glyph and temperature (6 / 2), hi/lo, and the 5-day row;
  - no city → "SET A CITY IN SETTINGS"; stale → dimmed with "UPDATED HH:MM".
- [ ] **Step 2: Media** (mockup `renderMedia`):
  - **1×1:** art filling the card with a play button;
  - **2×1:** art 104 wide, title 17 SemiBold, artist, a 20-dot bar, prev / play / next;
  - **2×2:** art 150 high, title, artist, a 24-dot bar, controls;
  - progress ticks once a second only while playing; no session → "Nothing playing".
- [ ] **Step 3: Build, test and render.** Commit `feat: weather and media widgets`.

### Task 8: Calendar widget

**Files:**
- Create: `src/NothingShell/Widgets/CalendarWidget.cs`

**Interfaces:**
- Consumes: `CalendarMonth` (Task 3); `Font.Serif`.

- [ ] **Step 1: Calendar** (mockup `renderCal`, without events):
  - **2×2:** "November 2026" (DM Serif 20, the year `#666`), then the S–S header (mono 9 dim) and the grid (12 px, 30 px cells); other months' days `#444`; today on a red 30 px circle with white text.
  - **2×1:** the day name (DM Serif 16, red) over the date (DM Serif 60), plus "NO EVENTS" (mono) — "Up next" is Plan 6.
  - **1×1:** the day name (DM Serif 18, red) top right, and the date (DM Serif 76) bottom centre.
  - Updates at midnight: one timer to the next local midnight.
- [ ] **Step 2: Build, test and render.** Commit `feat: calendar widget (dates; Google sign-in is Plan 6)`.

### Task 9: Battery widget and device batteries

**Files:**
- Create: `src/NothingShell/Widgets/DeviceBatteryService.cs`, `BatteryWidget.cs`

**Interfaces:**
- Consumes: `TrayStatusService` (laptop %, charging); `BatteryPages` (Task 3).
- Produces:
  - `DeviceBatteryService`:
    - `IReadOnlyList<BatteryDevice> Devices`, `event Action? Changed`;
    - laptop from `TrayStatusService`;
    - paired, connected Bluetooth devices from a `DeviceWatcher` on `BluetoothDevice.GetDeviceSelectorFromPairingState(true)` and on the AEP selector, requesting `System.Devices.Aep.IsConnected` and the battery property `{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2`;
    - icon from the class of device: audio → `head`, peripheral → `mouse`, else `speaker`;
    - device names never logged.

- [ ] **Step 1: Service.** Watcher-driven, with no polling. A device with no battery property gets `Percent = null`.
- [ ] **Step 2: Widget** (mockup `renderBattery`, `bRing`):
  - a ring (stroke = size × 0.09; track `#2C2C2C`; level `#F2F2F2`, red under 20%) with the device icon in the middle;
  - **1×1** ring 100 / icon 32; **2×2** 220 / 60;
  - **2×1** ring 100 plus the name (15 SemiBold) and "OK" or "LOW · CHARGE SOON" (red);
  - more than one device → page dots (7 px) on the right; the wheel or a dot switches; pages slide vertically (320 ms ease);
  - ⚡ over the icon when charging; tooltip "Name · 72%";
  - no level → icon only.
- [ ] **Step 3: Build, test and render** with fake devices (laptop at 36%, buds at 12%). Commit `feat: battery widget (laptop + Bluetooth devices)`.

### Task 10: Snake widget

**Files:**
- Create: `src/NothingShell/Widgets/SnakeWidget.cs`
- Modify: `src/NothingShell/Launcher/WinKeyListener.cs` (snake routing)

**Interfaces:**
- Consumes: `SnakeGame`, `SnakeKeyGate` (Task 2); `ShellSettings.SnakeBest`.
- Produces: `WinKeyListener.SnakeGate` (a `SnakeKeyGate?`) and `event Action<SnakeKey>? SnakeKey`. The keyboard hook swallows a key only when `gate.ShouldSwallow(vk)` and raises it on the UI thread.

- [ ] **Step 1: Widget** (mockup `drawSnake`):
  - dots of radius pitch × 0.36 on a 14 or 20 board; unlit `#262626`, body `#F2F2F2`, head white and 1.12× larger, food red;
  - game over: body `#777` and head red;
  - overlays: Idle "Snake / CLICK · ARROWS / WASD / BEST n"; Paused "PAUSED · CLICK TO RESUME"; Over the score in dots (5 / 2) and "GAME OVER · SPACE TO RETRY";
  - the score dots top right while playing.
- [ ] **Step 2: Input and focus.**
  - A click arms the gate, calls `game.Click()`, and draws a 2 px white inner ring.
  - The 120 ms timer runs only in Play.
  - Any foreground change (the existing foreground watcher), a click outside the snake (mouse hook), or Esc disarms and pauses (spec: "pauses automatically when focus leaves").
  - `Best` is saved on Over.
- [ ] **Step 3: Build, test and render.** Commit `feat: snake widget (hook-routed keys, pauses when focus leaves)`.

### Task 11: Edit mode

**Files:**
- Create: `src/NothingShell/Widgets/EditBackdrop.cs`
- Modify: `DesktopWidgets.cs`, `WidgetCard.cs`, `SettingsWindow.cs` (an "Edit widgets" row), plus a right-click on any widget for an "Edit widgets" popup (reuse `DockPopup`)

**Interfaces:**
- Consumes: `DesktopLayout.TryMove/CycleSize/Overlaps` (Task 1).

- [ ] **Step 1: Backdrop.** A child window covering the work area, attached **under** the widgets: a 74 px dot grid (3 px dots, `rgba(110,110,110,.7)`, scaled), plus a pill at the top centre with "Drag widgets · size chips change size" and **Done**.
- [ ] **Step 2: Drag.**
  - Press and move on a widget host; the window follows the pointer, raised with a shadow.
  - A red outline (2 px `#E8231F`, dashed in edit mode) shows while the snapped spot overlaps.
  - Release → `TryMove`; refused → it animates back (180 ms).
  - Widget content ignores clicks while editing.
- [ ] **Step 3: Size chip** (mockup `.chip`): bottom right, `#F2F2F2` pill, mono 11 dark, e.g. "3×1 ▸". A click calls `CycleSize`; an overlap keeps the red outline until moved.
- [ ] **Step 4: Saving.** Every change goes through `UpdateSettings(s => s with { Widgets = … })`. **Done** or Esc ends edit mode. Full screen ends it too.
- [ ] **Step 5: Build, test, and render edit mode** with one overlap. Commit `feat: widget edit mode (drag, snap, size chips, refuses overlaps)`.

### Task 12: Hands-on, budget and docs

**Files:**
- Modify: `build/verify/plan1-handson.ps1` (section `Widgets`), `docs/superpowers/spikes/2026-11-17-phase1-measurements.md` (Phase 5), `README.md`

- [ ] **Step 1: Hands-on checks** (the user answers y/n; the script reads the log where it can):
  - **DW.1:** all seven widgets on the desktop in the default layout, under app windows.
  - **DW.2:** after Win+D the widgets are visible and clickable (the timer starts on click).
  - **DW.3:** restarting Explorer (the user confirms first) brings the widgets back within 2 s (`widgets reattached in N ms`).
  - **DW.4:** **no widget in Alt+Tab.**
  - **DW.5:** the clock is legible on all four wallpapers (dark dots on light, light on dark).
  - **DW.6:** Snake works:
    - click, then arrows / WASD play; Space pauses and restarts;
    - clicking another window pauses it, and the arrows work normally there;
    - the best score survives a restart.
  - **DW.7:** the Timer widget and the notch timer are the same timer; it flashes at zero.
  - **DW.8:** Weather and Media match the notch tiles; offline shows "updated".
  - **DW.9:** Battery shows the laptop, and buds if connected; the dots / wheel switch; a newly connected device is jumped to.
  - **DW.10:** edit mode: the dot grid; dragging snaps; an overlap shows red and refuses; size chips; Done; the layout survives a restart.
  - **DW.11:** Settings widget size S / M / L applies live; wallpaper picking applies (the user picks one).
  - **DW.12:** a full-screen video: nothing of the widgets shows over it.
- [ ] **Step 2: Budget.**
  - Use the widgets (snake game, timer run, media playing), then idle 10 minutes with no music.
  - Measure with the Plan 1 method.
  - Pass: under 100 MB total working set and CPU under 0.5%.
- [ ] **Step 3: Docs.** README: widgets, edit mode, Settings size / wallpaper, snake keys. Measurements: Phase 5.
- [ ] **Step 4: Run all tests and commit.** Message: `docs: phase 5 measurements + README`. Ask the user before pushing `plan5-desktop-widgets` and opening a PR.

## User decisions (2026-11-19, while writing the plan)

1. Google Calendar sign-in is a separate, later **Plan 6** (guided Cloud Console setup). Plan 5's calendar shows dates only.

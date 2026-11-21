# Plan 4: Notch Dashboard, Timer and Settings Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Hovering the notch opens a dashboard of tiles in two pages, with an edit mode. Page 1 holds Timer, Media, Clipboard and Notes; page 2 holds the file shelf, Keep awake, Weather and the tool buttons. The plan also adds the shared timer with the notch's live Timer state, and the Settings window.

**Architecture:**
- **Pure logic in `NothingShell.Core`, unit-tested:**
  - the dashboard layout (pages, allowed sizes, dense packing, edit operations);
  - the timer engine and the grid timer's scatter order;
  - the hover open/close rules;
  - clipboard history and its privacy rule;
  - the shelf list;
  - weather parsing and code mapping;
  - region and colour helpers for the tools.
- **System access in `NothingShell` services:**
  - `TimerService`, which Plan 5's desktop timer widget will share;
  - `ClipboardWatcher` (`AddClipboardFormatListener`);
  - `KeepAwakeService` (`SetThreadExecutionState`);
  - `WeatherService` (Open-Meteo over HTTPS);
  - `ScreenCapture` plus `RegionPicker` for the tools;
  - OCR through `Windows.Media.Ocr`.
- **Surfaces:**
  - `NotchWindow` grows an Open layer. Its band becomes big enough for the open size and stays click-through except while open.
  - A `DashboardView` places tile views on the 8-column grid.
  - `SettingsWindow` is a new small, activatable window.
  - `RegionPicker` is a full-screen overlay used only while a tool is picking.

**Tech Stack:**
- .NET 10, WPF and CsWin32, as before.
- WinRT `Windows.Media.Ocr` and `Windows.Graphics.Imaging` from the existing TFM.
- `System.Net.Http` for Open-Meteo.
- **No new NuGet packages.** Scan QR was dropped (user decision 2026-11-19), so no decoder is needed.

**Spec:** `docs/superpowers/specs/2026-11-17-nothing-shell-design.md`. Read:
- §5 (palette, type, motion);
- §6.4 (open dashboard, edit mode, tile behaviour);
- §6.5 "Timer defaults" (25:00, ±1 min, minimum 1:00, time-up in full screen);
- §6.6 (one timer, one media session);
- §6.7 (Settings);
- §7 (storage, performance rules, clipboard via `AddClipboardFormatListener`).

Also read `docs/mockups/final-v2.html`:
- lines 68–96: open-notch CSS (`.l-open`, `.ngrid`, `.t`, `.hd`, `.clip`, `.tg`, `.drop`, `.nfoot`, `.pg`, `.nedit`, `.nchip`, `.nx`, `.add`);
- lines 233–262: the `I` icon table (SVG path data on a 24-unit box, stroke 1.6);
- line 265 `dotBar`, line 283 `gridTimer`;
- lines 383–416: `NR` tile renderers per size, `NP` default pages, `renderN`/`fitN`.

**Branch:** `plan4-notch-dashboard`, created from `plan3-panel-notch` (PRs #1–#3 are still open; rebase onto `main` when they merge). Ask the user before pushing.

**Out of scope (Plan 5):** desktop widgets (the timer widget will reuse `TimerService`), widget scale and wallpaper in Settings, Google Calendar, and the "devices" tile seen in `notch-v3.html` (not in `final-v2.html` or the spec).

## Global Constraints

- **Platform and DPI:** Windows 11, primary display only, per-monitor DPI aware. Sizes are in DIPs (spec §2, §7).
- **Memory:** under **100 MB** total working set at idle after 10 minutes; CPU about 0% when nothing is changing (spec §3).
- **No idle timers faster than 1 s** unless something is visible and animating. The timer's 1 s tick runs only while it is running; media progress ticks only while the open notch shows the Media tile. Nothing polls while the notch is closed (spec §7).
- **Notch open state** (spec §6.4):
  - **770 wide**, radius **34**; 14 padding; grid of **84 px cells with 10 px gaps, 8 columns**, dense packing; height = grid + 28 + 26 for the footer.
  - Opens on hover; closes when the mouse leaves, unless editing.
  - Page dots at the bottom right; ✎ Edit at the bottom left.
  - Hidden while a full-screen app is in front.
- **Pages** (spec §6.4, `NP` in the mockup):
  - **page 1:** Timer 2×2, Media 2×2, Clipboard 2×2, Notes 2×2;
  - **page 2:** Shelf 2×2, Keep awake 2×1, Weather 2×1, then 1×1 tools Grab text, Phone, Color, Snip, Draw, Settings;
  - **Scan QR is dropped** (user decision 2026-11-19).
- **Edit mode** (spec §6.4):
  - a size chip cycles the tile's allowed sizes, and ✕ removes;
  - "＋ Add" brings removed tiles back;
  - dense packing;
  - edit mode keeps the notch open until "Done".
- **Allowed sizes** (mockup `SIZES`): timer, media, clipboard, notes, shelf `2×2, 2×1, 1×1`; keep awake `2×1, 1×1`; weather `2×1, 1×1, 2×2`; tools `1×1, 2×1`.
- **Timer** (spec §6.4, §6.5):
  - one timer shared by the notch tile, the notch live state and (Plan 5) the desktop widget;
  - **25:00** default; − / + change it by **1 minute**, minimum **1:00**;
  - grid of dots that go out in a scattered order, with the next one red;
  - at zero it flashes red and sends "Time is up";
  - in full screen the notification is kept in the panel, and the notch stays hidden and does not replay it.
- **Live priority:** **notification > timer > music > resting**; Timer pill **270×34** (spec §6.4).
- **Clipboard:** the last **text** items only, newest first, capped at **50**, kept across restarts in `%AppData%\NothingShell\` (spec §6.4, §7).
- **Notes:** one plain-text note, saved as you type, kept across restarts.
- **Keep awake:** prevents sleep **and** screen-off while on.
- **Shelf:** holds references, not copies.
- **Weather:** Open-Meteo, no API key; the location is a city the user types in Settings; refreshes every **30 min**; offline shows the last data, dimmed, with "updated hh:mm" (spec §6.5).
- **Snip:** copies the image **and** saves a PNG to `Pictures\Screenshots` (user decision 2026-11-19).
- **Settings** (spec §6.7):
  - opened from the page-2 Settings tile and by right-clicking the notch;
  - Plan 4 contents: weather city, start with Windows, keep dock visible, and **Quit** (widget scale and wallpaper come in Plan 5);
  - no tray icon of our own.
- **Visuals:**
  - tile `#161616`, hover `#202020`, radius **22**, inner 1 px `#0FFFFFFF` edge; text `#F2F2F2`, dim `#8A8A8A`; red `#E8231F` as the only accent; on-state `#F2F2F2` fill with dark text;
  - Geist / Geist Mono (Mono 9–10 uppercase for small labels);
  - numbers in dot matrix only, never sentences;
  - motion 160–320 ms `cubic-bezier(.2,.8,.2,1)`, no bounce.
- **Privacy:**
  - never log clipboard text, note text, OCR text, file names on the shelf, media titles or the weather city;
  - skip clipboard items that password managers mark as private.
- **Live checks:** implementers never start, kill or message the user's running installed shell, and never weaken `SingleInstance`. Live checks happen in the hands-on (Task 14). Offscreen renders (a scratch WPF host that loads `NothingShell.dll`, as used for the charging card) are fine.

## Review Focus

1. **Passing the cursor over the notch** on the way to a browser tab must not open the dashboard. It opens only after the pointer rests on the pill for **150 ms**, and closes **300 ms** after leaving the open area. *(Task 3 tests `PassingThrough_DoesNotOpen`, `Leave_ClosesAfter300ms`.)*
2. **Typing in Notes while the mouse drifts away** must not close the notch mid-sentence. While a text box has keyboard focus the notch stays open, and closes on Esc or when it loses focus. *(Task 3 test `TextFocus_HoldsOpen`; Task 8 wires it.)*
3. **Secrets in the clipboard.** Copies marked by password managers (`ExcludeClipboardContentFromMonitorProcessing`, `CanIncludeInClipboardHistory` = 0, `Clipboard Viewer Ignore`) must never reach `clipboard.json`. *(Task 7 test `ShouldRecord_PrivateFormats_False`.)*
4. **Sleep, hibernate and clock changes during a running timer.** Remaining time comes from a monotonic clock. After waking past the end it shows "Time is up" once, never several times and never a negative time. *(Task 2 tests `Tick_AfterLongGap_FinishesOnce`, `Remaining_NeverNegative`.)*
5. **A broken or old saved layout** (hand-edited, a kind removed in a later version, duplicates, sizes no longer allowed, a page over 3 rows) must load as a valid layout. Missing tiles go to "＋ Add" instead of the dashboard crashing. *(Task 1 test `Sanitize_RepairsBadLayouts`.)*

---

## File Structure

```
src/NothingShell.Core/
  Notch/Dashboard.cs              TileKind, TileSize, TileSpec, TileRules, DashboardLayout (+Sanitize, edit ops)
  Notch/GridPacker.cs             dense 8-column packing, row count
  Notch/NotchHover.cs             open/close rules (dwell, leave delay, holds, full screen)
  Notch/NotchStateMachine.cs      (modify) Open state
  Timer/TimerModel.cs             countdown engine on a monotonic clock
  Timer/TimerGrid.cs              scatter order, lit set, next dot
  Clipboard/ClipboardHistory.cs   newest-first, dedupe, cap; ClipboardRules.ShouldRecord
  Shelf/ShelfList.cs              paths, dedupe, prune missing
  Weather/WeatherRules.cs         WMO code -> kind, Open-Meteo JSON parsing, staleness
  Tools/ToolMath.cs               region normalise, hex colour, snip file name, OCR line join
  ShellSettings.cs                (modify) NotchLayout, TimerMinutes, Weather place
src/NothingShell.Native/
  ScreenCaptureNative.cs          BitBlt of the primary monitor into an HBITMAP
  PowerRequestNative.cs           SetThreadExecutionState
src/NothingShell/
  Notch/NotchWindow.xaml(.cs)     (modify) Open layer, band size, click-through toggle, activation
  Notch/NotchController.cs        (modify) hover, open, live timer, local cards
  Notch/Dashboard/DashboardView.cs, TileChrome.cs, NotchIcons.xaml, GridTimerView.cs
  Notch/Dashboard/Tiles/{Timer,Media,Clipboard,Notes,KeepAwake,Shelf,Weather,Tool}Tile.cs
  Timer/TimerService.cs
  Clipboard/ClipboardWatcher.cs, Clipboard/ClipboardStore.cs
  Notes/NotesStore.cs
  Shelf/ShelfStore.cs
  Power/KeepAwakeService.cs
  Weather/WeatherService.cs
  Tools/RegionPicker.xaml(.cs), Tools/ToolRunner.cs
  Settings/SettingsWindow.xaml(.cs)
  Media/MediaSessionService.cs    (modify) timeline + transport
  Notifications/NotificationService.cs (modify) local items
build/verify/plan1-handson.ps1    (modify) Dash section
tests/NothingShell.Core.Tests/{Notch,Timer,Clipboard,Shelf,Weather,Tools}/*Tests.cs
tests/NothingShell.Tests/{MediaSessionServiceTests,NotificationServiceLocalTests}.cs
```

---

### Task 1: Dashboard layout model

**Files:**
- Create: `src/NothingShell.Core/Notch/Dashboard.cs`, `src/NothingShell.Core/Notch/GridPacker.cs`
- Modify: `src/NothingShell.Core/ShellSettings.cs`
- Test: `tests/NothingShell.Core.Tests/Notch/DashboardTests.cs`, `GridPackerTests.cs`, `SettingsStoreTests.cs` (add)

**Interfaces:**
- Produces:
  - `enum TileKind { Timer, Media, Clipboard, Notes, Shelf, KeepAwake, Weather, GrabText, Phone, Color, Snip, Draw, Settings }`
  - `enum TileSize { One, Wide, Big }` with `TileRules.Cells(TileSize) → (int W, int H)`: `One` = 1×1, `Wide` = 2×1, `Big` = 2×2.
  - `TileRules.Sizes(TileKind) → IReadOnlyList<TileSize>`, first entry = default, values from Global Constraints. The tool kinds are `GrabText…Settings`.
  - `public sealed record TileSpec(TileKind Kind, TileSize Size)`
  - `public sealed record DashboardLayout(IReadOnlyList<IReadOnlyList<TileSpec>> Pages, IReadOnlyList<TileSpec> Removed)` with:
    - `static DashboardLayout Default`
    - `const int MaxRows = 3`
    - `DashboardLayout CycleSize(int page, int index)`: next allowed size that still packs within `MaxRows`; unchanged if none fits
    - `DashboardLayout Remove(int page, int index)`: appends to `Removed`
    - `DashboardLayout AddBack(int page)`: first of `Removed` at its default size; unchanged if it doesn't fit
    - `static DashboardLayout Sanitize(DashboardLayout? loaded)`
  - `GridPacker.Pack(IReadOnlyList<TileSize> sizes, int columns = 8) → IReadOnlyList<(int Col, int Row)>` and `GridPacker.Rows(...) → int`.
  - `ShellSettings.NotchLayout` (`DashboardLayout?`, null = default).

- [ ] **Step 1: Create the branch.** Run `git switch -c plan4-notch-dashboard plan3-panel-notch`. Expected: on `plan4-notch-dashboard`.
- [ ] **Step 2: Write the failing tests.**
  - `Default_MatchesSpec`:
    - page 1 = `[Timer Big, Media Big, Clipboard Big, Notes Big]`;
    - page 2 = `[Shelf Big, KeepAwake Wide, Weather Wide, GrabText One, Phone One, Color One, Snip One, Draw One, Settings One]`;
    - `Removed` empty.
  - `Pack_FourBigTiles_TwoRows`: positions `(0,0),(2,0),(4,0),(6,0)`, `Rows == 2`.
  - `Pack_Page2_FitsTwoRows`: the default page 2 packs into 2 rows.
  - `Pack_Dense_FillsHoleWithLaterOne`: `[Big, Wide, Wide, Wide, Big, One]` with 8 columns gives `(0,0),(2,0),(4,0),(6,0),(2,1),(4,1)` and `Rows == 3`. Each item goes in the first free cell, scanning row by row and then column by column (CSS `grid-auto-flow: dense`).
  - `CycleSize_FollowsAllowedOrder`: Timer `Big → Wide → One → Big`; KeepAwake `Wide → One → Wide`.
  - `CycleSize_SkipsSizesThatOverflow`: a page already filling 3 rows refuses a cycle that would need a 4th.
  - `Remove_ThenAddBack_RestoresAtDefaultSize`.
  - `AddBack_RefusedWhenPageFull`.
  - `Sanitize_RepairsBadLayouts`:
    - null gives `Default`;
    - an unknown kind (cast an out-of-range int) is dropped;
    - a duplicate kind is dropped;
    - a size not allowed for its kind becomes its default;
    - a page over 3 rows has tiles from the end moved to `Removed`;
    - kinds missing from all pages and from `Removed` are appended to `Removed`;
    - an empty `Pages` gives `Default`.
  - `SaveThenLoad_RoundTripsNotchLayout`.
- [ ] **Step 3: Run them and confirm they fail.** Run `dotnet test tests/NothingShell.Core.Tests --filter "Dashboard|GridPacker|SettingsStore"`. Expected: compile errors.
- [ ] **Step 4: Implement.** `GridPacker` keeps an occupancy grid that grows by rows and places each item at the first (row, col) where its W×H fits. The layout is immutable; each operation returns a new record. Lists serialize through `JsonFile.Options` (enums as strings, as the existing settings do).
- [ ] **Step 5: Run the tests.** Expected: pass.
- [ ] **Step 6: Commit.** Message: `feat: notch dashboard layout model (pages, sizes, dense packing, edit ops)`.

### Task 2: Timer engine

**Files:**
- Create: `src/NothingShell.Core/Timer/TimerModel.cs`, `src/NothingShell.Core/Timer/TimerGrid.cs`
- Modify: `src/NothingShell.Core/ShellSettings.cs` (`int TimerMinutes = 25`)
- Test: `tests/NothingShell.Core.Tests/Timer/TimerModelTests.cs`, `TimerGridTests.cs`

**Interfaces:**
- Produces:
  - `enum TimerPhase { Idle, Running, Paused, Finished }`
  - `TimerModel(int minutes)`. `now` is a **monotonic** `TimeSpan` (the app passes `Stopwatch.GetElapsedTime(start)`).
    - `TimerPhase Phase`, `TimeSpan Duration`, `TimeSpan Remaining(TimeSpan now)`, `double Fraction(TimeSpan now)` (remaining / duration, 0..1)
    - `void Toggle(TimeSpan now)`: Idle→Running, Running→Paused, Paused→Running, Finished→Idle (reset to `Duration`)
    - `void Plus(TimeSpan now)`, `void Minus(TimeSpan now)`
    - `bool Tick(TimeSpan now)`: true exactly once, when it reaches zero, and moves to Finished
    - `int Minutes` (for saving)
  - `TimerGrid.Order(int n) → IReadOnlyList<int>`: the mockup's `scatter(N)`, a Fisher–Yates from the end with `s = 7; s = (s*9301 + 49297) % 233280; j = floor(s/233280*(i+1))`.
  - `TimerGrid.State(int n, double fraction) → (IReadOnlySet<int> Alive, int? Next)`: `lit = ceil(fraction*n*n)`; alive = the last `lit` of the order; `Next` = `order[n*n - lit]` when anything has gone out and something is still alive, else null.
  - `TimerModel` +/− rules:
    - Idle or Finished: `Duration` ±1 min, clamped to **1–99 min**, and `Remaining = Duration`.
    - Running or Paused: `Remaining` ±1 min, clamped to 1:00–99:00, but Minus does nothing when `Remaining` is under 1:00.
    - `Duration` becomes `max(Duration, Remaining)` so the fraction stays ≤ 1.

- [ ] **Step 1: Write the failing tests.**
  - `Default_25Minutes_Idle`: `new TimerModel(25)` gives Remaining 25:00, Fraction 1, Idle.
  - `Minus_NotBelowOneMinute`: from 2 min, Minus×3 gives 1:00.
  - `Toggle_RunsFromNow`: Toggle at t=0; Remaining at t=10 s is 24:50.
  - `Pause_FreezesRemaining`: pause at 10 s; Remaining at 100 s is still 24:50; resume at 100 s; at 110 s it is 24:40.
  - `Plus_WhileRunning_AddsToRemaining`.
  - `Tick_FinishesOnce`: 1-minute timer; `Tick(59 s)` false; `Tick(60 s)` true; `Tick(61 s)` false; Phase Finished.
  - `Tick_AfterLongGap_FinishesOnce`: `Tick(10 h)` true once; Remaining 0.
  - `Remaining_NeverNegative`.
  - `Toggle_AfterFinish_ResetsToDuration`.
  - `Order_MatchesMockup`: `Order(5)` = `[24,7,23,14,11,20,3,18,17,22,21,0,8,16,4,6,19,15,1,10,9,5,2,13,12]`; `Order(10)` starts `[32,59,43,1,27,77,91,44]`.
  - `State_Quarter_LeavesQuarterAlive_NextIsFirstToGo`: n=10, fraction 0.25 gives 25 alive; `Next` = `Order(10)[75]`.
  - `State_Full_NoNext`; `State_Zero_NoneAlive`.
- [ ] **Step 2: Run them and confirm they fail.** Run `dotnet test tests/NothingShell.Core.Tests --filter "TimerModel|TimerGrid"`. Expected: compile errors.
- [ ] **Step 3: Implement.** Running stores `_endsAt = now + remaining`; Paused and Idle store `_remaining`.
- [ ] **Step 4: Run the tests.** Expected: pass.
- [ ] **Step 5: Commit.** Message: `feat: timer engine and grid timer scatter order`.

### Task 3: Hover rules and the Open state

**Files:**
- Create: `src/NothingShell.Core/Notch/NotchHover.cs`
- Modify: `src/NothingShell.Core/Notch/NotchStateMachine.cs`
- Test: `tests/NothingShell.Core.Tests/Notch/NotchHoverTests.cs`, `NotchStateMachineTests.cs` (add)

**Interfaces:**
- Produces:
  - `NotchState.Open`.
  - `NotchStateMachine.SetOpen(bool open)`. `StateAt` returns `Open` whenever open and not full screen (it beats every other state). Transients keep their own clock: a notification that arrives while open shows after closing only if its 3.6 s have not run out.
  - `NotchHover`, with clock-injected `TimeSpan now`:
    - `static readonly TimeSpan OpenDwell = 150 ms, CloseDelay = 300 ms`
    - `bool IsOpen`
    - `void Pointer(bool overPill, bool overOpenArea, TimeSpan now)`
    - `void Hold(string reason, bool on)` (reasons: "edit", "text"; any active hold keeps it open)
    - `void SetFullscreen(bool fs)` (closes and blocks opening)
    - `void Close()` (Esc, a tool starting)
    - `bool Update(TimeSpan now)` (true when `IsOpen` changed)
    - `TimeSpan? NextCheckAt`

- [ ] **Step 1: Write the failing tests.**
  - `RestingOnPill_OpensAfter150ms`.
  - `PassingThrough_DoesNotOpen`: on the pill at 0 and off at 80 ms gives no open at 200 ms.
  - `Leave_ClosesAfter300ms`, and coming back within 300 ms keeps it open.
  - `EditHold_KeepsOpenOutside`; `TextFocus_HoldsOpen`; releasing the hold while outside closes it 300 ms later.
  - `Fullscreen_ClosesAndBlocksOpen`.
  - `Close_ClosesEvenWithPointerInside`: it reopens only after the pointer leaves the pill and comes back.
  - State machine: `Open_BeatsNotificationAndTimer`; `Open_HiddenInFullscreen`; `NotificationDuringOpen_ShowsAfterCloseIfStillDue`.
- [ ] **Step 2: Run them and confirm they fail.** Run `dotnet test tests/NothingShell.Core.Tests --filter "NotchHover|NotchStateMachine"`. Expected: compile errors.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run the tests.** Expected: pass.
- [ ] **Step 5: Commit.** Message: `feat: notch hover rules and Open state`.

### Task 4: The open notch surface

**Files:**
- Create:
  - `src/NothingShell/Notch/Dashboard/DashboardView.cs`, `TileChrome.cs`, `NotchIcons.xaml`
  - `src/NothingShell/Notch/Dashboard/ITileView.cs`
- Modify: `NotchWindow.xaml(.cs)`, `NotchController.cs`, `App.xaml.cs`

**Interfaces:**
- Consumes: `DashboardLayout`, `GridPacker` (Task 1); `NotchHover`, `NotchState.Open` (Task 3); `MouseEdgeHook.Moved` (physical pixels, UI thread).
- Produces:
  - `interface ITileView : IDisposable { TileKind Kind { get; } FrameworkElement Build(TileSize size); void Opened(); void Closed(); }`. `Opened`/`Closed` start and stop anything live, such as media progress.
  - `DashboardView(Func<TileKind, ITileView> factory)`, with:
    - `FrameworkElement Root`;
    - `void Show(DashboardLayout layout, int page, bool editing)`;
    - `event Action<int>? PageRequested`, `EditToggled`;
    - `int PageOf(TileKind kind)`.
  - `NotchWindow`:
    - `void SetOpenContent(FrameworkElement root, double height)`;
    - `void ActivateForTyping()`, which drops `WS_EX_NOACTIVATE` and calls `ForegroundNative.Activate`;
    - `void EndTyping()`;
    - `Rect PillScreenRect` (physical pixels);
    - `event Action? SettingsRequested` (right-click on the open pill);
    - `event Action? EscPressed`.
  - `NotchController`: `public event Action? SettingsRequested`, `public void Close()`.
  - `TileChrome`:
    - `Border Tile(bool center)` (`#161616`, hover `#202020`, radius 22, padding 12, inner 1 px `#0FFFFFFF`);
    - `Panel Header(string icon, string title, FrameworkElement? right)` (icon 16, Geist SemiBold 13, gap 8);
    - `TextBlock Label(string text)` (Geist Mono 9, dim, uppercase, letter-spaced);
    - `Border PlayButton(bool playing, double size)` (`#F2F2F2` circle, dark glyph);
    - `Border Toggle(bool on)` (`.tg`: 40×24).

- [ ] **Step 1: Band and click-through.**
  - The band grows to **800×360 DIP** (the open pill's 770×326 maximum for 3 rows, plus its shadow). The pill stays top-centre at 8 DIP from the top.
  - `WS_EX_TRANSPARENT` is set while not open and cleared while open. While open, only the pill and its shadow take clicks.
- [ ] **Step 2: Hover wiring.**
  - `NotchController` subscribes to `MouseEdgeHook.Moved`. It tests the point against `PillScreenRect` (the current state's pill) and against the open rect, then feeds `NotchHover`.
  - One `DispatcherTimer` fires at `NextCheckAt`. No timer runs while nothing is pending.
  - The hook is already paused in full screen; `NotchHover.SetFullscreen` follows `FullscreenMonitor`.
  - The App passes the `MouseEdgeHook` instance into the controller.
- [ ] **Step 3: Open layer.**
  - Pill 770 × (grid + 28 + 26), radius 34, resized with the existing 300 ms ease. The layer fades in after 100 ms, as the other layers do.
  - Footer: ✎ + "EDIT" / "DONE" (Geist Mono 10 dim; white when editing) on the left; page dots on the right (6 px, `#444`, current `#F2F2F2`). Clicking a dot switches the page.
  - Esc closes. Right-click on the pill raises `SettingsRequested`.
  - The mouse wheel over the grid switches pages (one notch per page).
- [ ] **Step 4: Icons.** `NotchIcons.xaml` holds `Geometry` resources keyed `Icon.<name>`, using the path data from the mockup's `I` table (24-unit box, stroke 1.6, round caps) for `timer, clip, notes, shelf, cup, text, phone, picker, region, draw, settings, prev, next, expand, pencil, cloud`. Add `sun, rain, snow, fog, storm, cloudsun` in the same style for Task 11.
- [ ] **Step 5: Placeholder tiles.** Until Tasks 5–13 land, `factory` returns a tile showing the kind's icon and label. Check the open notch with an offscreen render at 125% (pages 1 and 2, and edit mode off).
- [ ] **Step 6: Build and test.** Run `dotnet build NotWin.sln -c Release` and `dotnet test NotWin.sln`. Expected: green.
- [ ] **Step 7: Commit.** Message: `feat: notch opens on hover into a two-page dashboard`.

### Task 5: Timer tile, live Timer state and "Time is up"

**Files:**
- Create: `src/NothingShell/Timer/TimerService.cs`, `src/NothingShell/Notch/Dashboard/GridTimerView.cs`, `Tiles/TimerTile.cs`
- Modify: `NotchWindow` (Timer layer), `NotchController`, `Notifications/NotificationService.cs`, `App.xaml.cs`
- Test: `tests/NothingShell.Tests/NotificationServiceLocalTests.cs`

**Interfaces:**
- Consumes: `TimerModel`, `TimerGrid` (Task 2); `ShellSettings.TimerMinutes`.
- Produces:
  - `TimerService`:
    - `TimerModel Model`, `event Action? Changed`, `event Action? Finished`;
    - `Toggle()`, `Plus()`, `Minus()`;
    - one `DispatcherTimer` that runs **only while Running**, aimed at the next whole second of `Remaining`;
    - saves `TimerMinutes` on +/− while Idle.
  - `GridTimerView(double size, int n)`, with `void Show(double fraction, bool running, bool flash)`. It is pixel-snapped like `DotColumns`. Dot radius = pitch × 0.32; alive `#F2F2F2`, gone `#2C2C2C`, next (while running) red, all red when flashing.
  - `NotificationService.AddLocal(string appName, string text) → NotificationItem`:
    - ids count down from `uint.MaxValue`, and `AppId` is empty;
    - `GetAllAsync` returns local items too, even when the listener is unavailable;
    - `Remove` and `ClearAll` remove them;
    - `ListChanged` is raised.
  - `NotchController.NotifyLocal(string title, string text, string icon)`: a Notification-state card with an icon from `NotchIcons`.

- [ ] **Step 1: Write the failing test.** `AddLocal_ListedEvenWithoutListener_AndRemovable`. Run `dotnet test tests/NothingShell.Tests --filter NotificationServiceLocal` and confirm it fails. Implement, then confirm it passes.
- [ ] **Step 2: Timer tile** (`NR.timer` in the mockup):
  - **1×1:** `GridTimerView(56, 8)`. A click toggles.
  - **2×1:** header "Timer"; bottom row with dot `mm:ss` (dot 3 / gap 1.1) and a 30 px play/pause.
  - **2×2:** header "Timer" plus a right-aligned mono `mm:ss` label; centred `GridTimerView(92, 10)`; bottom row − / play-pause / + (− and + in `#AAAAAA`, 18).
- [ ] **Step 3: Live state.**
  - `NotchStateMachine.SetTimerRunning(Phase == Running)`.
  - Timer layer 270×34: `GridTimerView(22, 5)`, then the dot countdown (2.6 / 1, `#EEEEEE`).
  - Updates once a second only while visible or running.
- [ ] **Step 4: Time is up.**
  - On `Finished`, every visible grid flashes red: 6 toggles at 400 ms.
  - `NotifyLocal("Timer", "Time is up", "timer")`, and `NotificationService.AddLocal("Timer", "Time is up")`.
  - In full screen the state machine drops the notch card, as already tested, and the panel keeps the item.
- [ ] **Step 5: Build, test and render.** Render the three sizes and the live state offscreen.
- [ ] **Step 6: Commit.** Message: `feat: notch timer tile, live timer state and "Time is up"`.

### Task 6: Media tile

**Files:**
- Create: `src/NothingShell/Notch/Dashboard/Tiles/MediaTile.cs`, `src/NothingShell.Core/Notch/MediaProgress.cs`
- Modify: `src/NothingShell/Media/MediaSessionService.cs`
- Test: `tests/NothingShell.Tests/MediaSessionServiceTests.cs` (add), `tests/NothingShell.Core.Tests/Notch/MediaProgressTests.cs`

**Interfaces:**
- Produces:
  - `MediaSnapshot` gains `TimeSpan Position`, `TimeSpan Duration` and `DateTimeOffset PositionAt`, all from `GetTimelineProperties()`.
  - `ISessionSource` gains `Timeline` plus `Task<bool> PlayPauseAsync()`, `NextAsync()`, `PreviousAsync()`. The fakes in tests implement them.
  - `MediaSessionService.PlayPauseAsync()`, `NextAsync()`, `PreviousAsync()`. Each returns false without a session.
  - `MediaProgress.Dots(TimeSpan position, TimeSpan duration, int count) → int`, where a zero or unknown duration gives 0. `MediaProgress.Clock(TimeSpan) → "m:ss"` (or `"h:mm:ss"` from 1 hour).

- [ ] **Step 1: Write the failing tests.** `PlayPause_CallsCurrentSession`; `Snapshot_HasTimeline`; `NoSession_ControlsReturnFalse`; `Dots_HalfWay_IsHalf`; `Dots_UnknownDuration_Zero`; `Clock_Formats`. Run them and confirm they fail.
- [ ] **Step 2: Implement the service changes.** Run the tests. Expected: pass.
- [ ] **Step 3: Media tile** (`NR.media`):
  - **1×1:** art filling the tile, with a 28 px play/pause at the bottom right.
  - **2×1:** 40 px art, a 2-line title (12 SemiBold), the artist label, and a 30 px play.
  - **2×2:** 54 px art; source app name (from the `AppIndex` name for the AUMID, else "Media") and the `m:ss / m:ss` label; title (14 SemiBold); artist (11 dim); a 20-dot `DotBar` for progress (dot 4, gap 3); prev / play / next.
  - Progress is interpolated from `PositionAt` while playing, on a 1 s timer that runs only between `Opened` and `Closed`.
  - No session: "Nothing playing" (dim), with the controls at 40% opacity.
  - Titles are never logged.
- [ ] **Step 4: Build, test and render.**
- [ ] **Step 5: Commit.** Message: `feat: notch media tile (art, progress, transport)`.

### Task 7: Clipboard history

**Files:**
- Create:
  - `src/NothingShell.Core/Clipboard/ClipboardHistory.cs`
  - `src/NothingShell/Clipboard/ClipboardWatcher.cs`, `ClipboardStore.cs`
  - `Tiles/ClipboardTile.cs`
- Test: `tests/NothingShell.Core.Tests/Clipboard/ClipboardHistoryTests.cs`

**Interfaces:**
- Produces:
  - `ClipboardHistory(int cap = 50)`, with `IReadOnlyList<string> Items` (newest first), `bool Add(string text)` (false when ignored), `void Remove(int index)`, `void Clear()`, and `static ClipboardHistory From(IEnumerable<string>?)`.
  - `ClipboardRules.ShouldRecord(IReadOnlyCollection<string> formatNames, uint? canIncludeInHistory) → bool`.
  - `ClipboardWatcher`: `event Action<string>? TextCopied` (UI thread). It is a message-only `HwndSource` with `AddClipboardFormatListener`, reading on `WM_CLIPBOARDUPDATE` (0x031D).
  - `ClipboardStore(string dir)`: `Load()` / `Save(IReadOnlyList<string>)` to `clipboard.json` via `JsonFile.WriteAtomic`, debounced 1 s.

- [ ] **Step 1: Write the failing tests.**
  - `Add_NewestFirst`.
  - `Add_Duplicate_MovesToTop`.
  - `Add_Blank_Ignored`.
  - `Add_Over10000Chars_Ignored`.
  - `Cap_Drops_Oldest`.
  - `ShouldRecord_PrivateFormats_False`: a list containing `ExcludeClipboardContentFromMonitorProcessing` or `Clipboard Viewer Ignore` gives false; `canIncludeInHistory == 0` gives false; plain text with null gives true.
  - `From_Null_Empty`.
- [ ] **Step 2: Run them and confirm they fail.** Run `dotnet test tests/NothingShell.Core.Tests --filter ClipboardHistory`. Expected: compile errors. Implement, then confirm they pass.
- [ ] **Step 3: Watcher.**
  - Enumerate format names (`EnumClipboardFormats` + `GetClipboardFormatName`), read `CanIncludeInClipboardHistory` (DWORD) when present, and apply `ShouldRecord`.
  - Read the text with 3 tries 30 ms apart; a clipboard locked by another app is skipped quietly.
  - Never log the text.
- [ ] **Step 4: Tile** (`NR.clip`):
  - **1×1:** icon, the count in dots (3 / 1), and "CLIPBOARD".
  - **2×1:** header plus the newest item.
  - **2×2:** header with the expand icon; up to 3 items (Geist Mono 11, one line, ellipsis; the newest on `#222`, white; hover `#262626`).
  - Clicking an item puts it back on the clipboard; the watcher then moves it to the top.
  - **Expand:** inside the open notch the grid is replaced by a scrollable list of all items, with a "‹ Clipboard" back header, ✕ per row and "CLEAR". Expanded counts as a hold, so the notch doesn't close under the cursor's way out.
- [ ] **Step 5: Build, test and render.**
- [ ] **Step 6: Commit.** Message: `feat: notch clipboard history (text, private copies skipped, saved)`.

### Task 8: Notes

**Files:**
- Create: `src/NothingShell/Notes/NotesStore.cs`, `Tiles/NotesTile.cs`
- Test: `tests/NothingShell.Tests/NotesStoreTests.cs`

**Interfaces:**
- Produces: `NotesStore(string dir)`, with `string Load()` and `void Save(string text)`. It writes `notes.txt` (UTF-8) atomically through a temp file plus `File.Replace`.

- [ ] **Step 1: Write the failing tests.** `SaveThenLoad_RoundTrips` (with Unicode and newlines); `Load_Missing_Empty`. Run them, confirm they fail, implement, and confirm they pass.
- [ ] **Step 2: Tile** (`NR.notes`):
  - **1×1:** icon plus "NOTES". A click opens the 2×2 editor in place of the grid, like clipboard expand.
  - **2×1:** a single-line `TextBox`.
  - **2×2:** a multi-line `TextBox`, about 6 lines.
  - The text boxes have no border or background, Geist 13, and a "Write something" placeholder in `#666`.
- [ ] **Step 3: Focus and saving.**
  - On `GotKeyboardFocus` / mouse-down, call `ActivateForTyping()` and `Hold("text", true)`. On losing focus or deactivation, call `EndTyping()` and `Hold("text", false)`.
  - Esc closes the notch.
  - Save 500 ms after the last keystroke, on close, and on exit.
  - The note text is never logged.
- [ ] **Step 4: Build, test and render.**
- [ ] **Step 5: Commit.** Message: `feat: notch notes tile (saved as you type)`.

### Task 9: Edit mode

**Files:**
- Modify: `DashboardView.cs`, `NotchController.cs`, `Dock/DockController.cs` (settings writer)

**Interfaces:**
- Consumes: `DashboardLayout.CycleSize/Remove/AddBack` (Task 1), `NotchHover.Hold("edit", …)` (Task 3), `DockController.UpdateSettings`.

- [ ] **Step 1: Edit UI** (`.nediting`, `.nchip`, `.nx`, `.add`):
  - every tile gets a 1.5 px dashed `#8A8A8A` outline;
  - a size chip at the bottom right: `#F2F2F2` pill, Geist Mono 10 dark, text like "2×2 ▸";
  - a red 20 px ✕ at the top right;
  - "＋ ADD" as a dashed 1×1 tile when `Removed` is not empty;
  - tile contents ignore clicks while editing.
- [ ] **Step 2: Behaviour.**
  - "EDIT" toggles editing and `Hold("edit")`; "DONE" ends it.
  - Each change goes through `UpdateSettings(s => s with { NotchLayout = … })` and re-renders.
  - The pill height animates to the new row count.
  - Entering full screen ends edit mode; the layout is already saved.
- [ ] **Step 3: Build, test and render.** Render edit mode on page 1 with one tile removed.
- [ ] **Step 4: Commit.** Message: `feat: notch edit mode (resize, remove, add back, saved)`.

### Task 10: Keep awake and the file shelf

**Files:**
- Create:
  - `src/NothingShell.Native/PowerRequestNative.cs`, `src/NothingShell/Power/KeepAwakeService.cs`
  - `src/NothingShell.Core/Shelf/ShelfList.cs`, `src/NothingShell/Shelf/ShelfStore.cs`
  - `Tiles/KeepAwakeTile.cs`, `Tiles/ShelfTile.cs`
- Test: `tests/NothingShell.Core.Tests/Shelf/ShelfListTests.cs`

**Interfaces:**
- Produces:
  - `KeepAwakeService`: `bool On`, `void Set(bool on)`, `event Action? Changed`. On is `SetThreadExecutionState(ES_CONTINUOUS | ES_SYSTEM_REQUIRED | ES_DISPLAY_REQUIRED)`, called on the UI thread; off is `ES_CONTINUOUS`. It is also cleared in `OnExit`. It is **not** saved: it starts off at every launch.
  - `ShelfList(int cap = 20)`: `IReadOnlyList<string> Paths`, `bool Add(string path)` (dedupe ignoring case; ignores when full), `void Remove(int i)`, `static ShelfList From(IEnumerable<string>?, Func<string,bool> exists)` (drops missing paths).
  - `ShelfStore(string dir)`: `shelf.json`.

- [ ] **Step 1: Write the failing tests.** `Add_DedupesCaseInsensitive`; `Add_FullIgnored`; `From_DropsMissing`. Run them, confirm they fail, implement, and confirm they pass.
- [ ] **Step 2: Keep awake tile** (`NR.awake`):
  - **1×1:** on-state fill `#F2F2F2` with a dark cup and "AWAKE", or a white cup and "SLEEP OK".
  - **2×1:** cup, "Keep awake" plus "ON"/"OFF", and the `.tg` toggle.
- [ ] **Step 3: Shelf tile** (`NR.shelf`):
  - **1×1:** icon plus "SHELF · N".
  - **2×1 / 2×2:** header "Shelf" plus "N FILES"; empty = the dashed `.drop` area ("Drop files here" / "DRAG OUT TO USE"); otherwise file chips (shell icon 24 + name, ✕ on hover).
  - Dropping files (`DataFormats.FileDrop`) adds their paths.
  - Dragging a chip out runs `DragDrop.DoDragDrop` with a `FileDrop` of that path, `Copy | Link`. The shelf keeps the reference.
  - When a drag enters the open notch on another page, switch to `PageOf(Shelf)`.
  - File names are never logged.
- [ ] **Step 4: Build, test and render.**
- [ ] **Step 5: Commit.** Message: `feat: notch keep-awake and file shelf tiles`.

### Task 11: Weather

**Files:**
- Create: `src/NothingShell.Core/Weather/WeatherRules.cs`, `src/NothingShell/Weather/WeatherService.cs`, `Tiles/WeatherTile.cs`
- Modify: `src/NothingShell.Core/ShellSettings.cs` (`WeatherPlace? Weather`), `src/NothingShell.Core/Notch/DotFont.cs` (add `'-'`: `["00000","00000","00000","11111","00000","00000","00000"]`)
- Test: `tests/NothingShell.Core.Tests/Weather/WeatherRulesTests.cs`, `Notch/DotFontTests.cs` (add)

**Interfaces:**
- Produces:
  - `record WeatherPlace(string Name, string Country, double Lat, double Lon)`.
  - `enum WeatherKind { Clear, PartlyCloudy, Cloudy, Fog, Drizzle, Rain, Snow, Storm }`.
  - `record WeatherDay(DateOnly Date, int High, int Low, WeatherKind Kind)`.
  - `record WeatherReport(int Temp, int High, int Low, WeatherKind Kind, bool IsDay, IReadOnlyList<WeatherDay> Days, DateTimeOffset FetchedAt)`.
  - `WeatherRules.Kind(int wmoCode)`, mapping 0 Clear; 1–2 PartlyCloudy; 3 Cloudy; 45, 48 Fog; 51–57 Drizzle; 61–67 and 80–82 Rain; 71–77 and 85–86 Snow; 95–99 Storm.
  - `WeatherRules.ParseGeocode(string json) → WeatherPlace?`.
  - `WeatherRules.ParseForecast(string json, DateTimeOffset fetchedAt) → WeatherReport?`.
  - `WeatherRules.IsStale(WeatherReport, DateTimeOffset now)` (older than 1 h).
  - `WeatherRules.GeocodeUrl(string city)` = `https://geocoding-api.open-meteo.com/v1/search?name={Uri.EscapeDataString(city)}&count=1&language=en&format=json`.
  - `WeatherRules.ForecastUrl(WeatherPlace p)` = `https://api.open-meteo.com/v1/forecast?latitude={lat}&longitude={lon}&current=temperature_2m,weather_code,is_day&daily=weather_code,temperature_2m_max,temperature_2m_min&timezone=auto&forecast_days=5`, with invariant-culture numbers.
  - `WeatherService`:
    - `WeatherReport? Current`, `event Action? Changed`;
    - `Task<WeatherPlace?> FindAsync(string city)`;
    - `void Start(WeatherPlace? place)`, which refreshes now and then every 30 min;
    - caches `weather.json` and loads it at start;
    - one `HttpClient` with a 10 s timeout and User-Agent `NothingShell` (no personal details).

- [ ] **Step 1: Write the failing tests.**
  - `Kind_MapsWmoCodes` (a theory over the table).
  - `ParseForecast_ReadsCurrentAndFiveDays`, from a trimmed real response embedded in the test.
  - `ParseForecast_Garbage_Null`.
  - `ParseGeocode_FirstResult`; `ParseGeocode_NoResults_Null`.
  - `ForecastUrl_UsesInvariantDecimals` (run under `de-DE`).
  - `IsStale_After1Hour`.
  - `DotFont_HasMinus`.
- [ ] **Step 2: Run them and confirm they fail.** Then implement and confirm they pass.
- [ ] **Step 3: Tile** (`NR.weather`):
  - **1×1:** the temperature in dots (3 / 1, like `24°`) plus the city label.
  - **2×1:** kind icon 20, temperature dots (4 / 1.4), city label, and "↑hi° ↓lo°" (11, `#CCCCCC`) on the right.
  - **2×2:** the 2×1 content on top, then a 5-day row (mono day initial, 16 icon, high).
  - No place: "SET A CITY" label; a click opens Settings.
  - Stale: 50% opacity plus "UPDATED HH:MM".
  - The city is never logged.
- [ ] **Step 4: Build, test and render.**
- [ ] **Step 5: Commit.** Message: `feat: notch weather tile (Open-Meteo, cached, 30 min)`.

### Task 12: Region tools: Grab text, Color, Snip

**Files:**
- Create:
  - `src/NothingShell.Core/Tools/ToolMath.cs`
  - `src/NothingShell.Native/ScreenCaptureNative.cs`
  - `src/NothingShell/Tools/RegionPicker.xaml(.cs)`, `ToolRunner.cs`
  - `Tiles/ToolTile.cs`
- Test: `tests/NothingShell.Core.Tests/Tools/ToolMathTests.cs`

**Interfaces:**
- Produces:
  - `ToolMath.Normalize(PointI a, PointI b, RectI bounds) → RectI?`: any drag direction, clamped to bounds, null under 4×4 px.
  - `ToolMath.Hex(byte r, byte g, byte b) → "#RRGGBB"`.
  - `ToolMath.SnipFileName(DateTime local) → "Nothing Shell 2026-11-19 143005.png"`.
  - `ToolMath.JoinOcrLines(IEnumerable<string>) → string` (newline-joined, blank lines dropped).
  - `ToolMath.WordCount(string)`.
  - `ScreenCaptureNative.CapturePrimary() → (nint HBitmap, RectI Bounds)`: `GetDC(null)` + `BitBlt` with `SRCCOPY | CAPTUREBLT`. The caller deletes the bitmap.
  - `RegionPicker.PickAsync(PickMode mode) → Task<PickResult?>`, where `enum PickMode { Region, Point }` and `record PickResult(RectI Rect, BitmapSource Shot, Color? Color)`.
  - `ToolRunner.Run(TileKind tool)`.

- [ ] **Step 1: Write the failing tests.** `Normalize_AnyDirection`; `Normalize_Tiny_Null`; `Normalize_ClampsToBounds`; `Hex_Formats`; `SnipFileName_Format`; `JoinOcrLines_DropsBlank`; `WordCount_Counts`. Run them, confirm they fail, implement, and confirm they pass.
- [ ] **Step 2: Region picker.**
  - Before capture, close the notch and wait 200 ms so it isn't in the shot.
  - A topmost window over the primary monitor shows the frozen shot under a 40% black veil.
  - Drag to select: the selection shows unveiled, with a 1 px white outline and a mono "W × H" label.
  - Point mode: a 9×9 px magnifier (11× zoom) follows the cursor, with the hex below it.
  - Esc or right-click cancels. The window is activatable so Esc works.
- [ ] **Step 3: Tools.**
  - **Grab text:** OCR with `OcrEngine.TryCreateFromUserProfileLanguages()` on a `SoftwareBitmap` of the crop. Copy the joined text and show the card "Grab text" / "Copied N words", or "No text found". Never log the text.
  - **Color:** copy the hex and show the card "Color" / "#RRGGBB".
  - **Snip:** copy the crop image and save a PNG to `Pictures\Screenshots` (`KnownFolders` path; create it if missing). Show the card "Snip" / "Saved to Screenshots".
  - Each card goes through `NotifyLocal` (Task 5).
- [ ] **Step 4: Tool tiles** (`NR.tool`): **1×1** icon 20 plus a mono label; **2×1** a 36 px `#222` circle icon plus the name (13 SemiBold).
- [ ] **Step 5: Build, test and render.** Render the picker on a still image.
- [ ] **Step 6: Commit.** Message: `feat: notch tools: grab text (OCR), color picker, snip`.

### Task 13: Phone, Draw and the Settings window

**Files:**
- Create: `src/NothingShell/Settings/SettingsWindow.xaml(.cs)`
- Modify: `ToolRunner.cs`, `Dock/DockController.cs`, `App.xaml.cs`

**Interfaces:**
- Consumes: `DockController.UpdateSettings`; `WeatherService.FindAsync/Start` (Task 11).
- Produces:
  - `DockController.SetKeepVisible(bool)` and `SetStartWithWindows(bool)` become public.
  - `SettingsWindow.ShowOrActivate()` (a single instance).

- [ ] **Step 1: Phone and Draw.**
  - **Phone:** `ShellLinkNative.Open(@"shell:AppsFolder\Microsoft.YourPhone_8wekyb3d8bbwe!App")`. If it fails, show the card "Phone Link isn't installed".
  - **Draw:** open Snipping Tool (`shell:AppsFolder\Microsoft.ScreenSketch_8wekyb3d8bbwe!App`), falling back to `ms-screenclip:`.
- [ ] **Step 2: Settings window.**
  - 420 wide, `Brush.GlassDense`, radius 28, Geist, centred on the primary display, activatable, Esc closes.
  - **Weather city:** a text box plus "Set". `FindAsync` either shows "Delhi, India" and saves `Weather`, or shows "City not found" / "Offline".
  - **Start with Windows** and **Keep dock visible** toggles (`.tg`).
  - **Quit Nothing Shell**, in red text.
  - Opened from the Settings tile, by right-clicking the notch, and from a new first row "Settings…" in the dock start button's right-click menu.
- [ ] **Step 3: Build and test.** Run `dotnet build NotWin.sln -c Release` and `dotnet test NotWin.sln`. Expected: green. Render the window.
- [ ] **Step 4: Commit.** Message: `feat: Settings window; Phone and Draw tools`.

### Task 14: Hands-on, budget and docs

**Files:**
- Modify: `build/verify/plan1-handson.ps1` (section `Dash`), `docs/superpowers/spikes/2026-11-17-phase1-measurements.md` (Phase 4), `README.md`

- [ ] **Step 1: Add the hands-on checks** (the user answers y/n; the script reads the log where it can).
  - **ND.1:** resting the pointer on the notch opens the dashboard; sweeping across it to a browser tab does not; leaving closes it.
  - **ND.2:** page dots and the mouse wheel switch pages; right-click opens Settings.
  - **ND.3 (timer):**
    - set it to 1:00 and start it: the notch shows the Timer state with the countdown;
    - at zero the grid flashes, "Time is up" shows in the notch and is listed in the panel;
    - − and + work;
    - the length is still there after a restart.
  - **ND.4:** the Media tile shows the playing track with progress, and prev / play / next work.
  - **ND.5:**
    - copying text adds it to Clipboard, and clicking an item copies it again;
    - a password copied from a password manager is not added;
    - the list survives a restart.
  - **ND.6:** Notes keeps typed text with the mouse moved away, Esc closes, and the note survives a restart.
  - **ND.7:** edit mode resizes, removes and adds back tiles, Done saves, and the layout survives a restart.
  - **ND.8:** Keep awake on means `powercfg /requests` lists Nothing Shell; off means it doesn't.
  - **ND.9 (shelf):**
    - dropping a file from Explorer adds it;
    - dragging it out to the desktop copies it;
    - after a restart the shelf still holds it.
  - **ND.10:** Weather shows after setting a city in Settings; offline, it shows dimmed with "updated".
  - **ND.11:** Grab text copies the text of a selected region; Color copies a hex; Snip copies and saves a PNG in `Pictures\Screenshots`.
  - **ND.12:** Phone opens Phone Link (or shows the card); Draw opens Snipping Tool.
  - **ND.13:** with a full-screen video the notch doesn't open on hover, and a timer ending in full screen shows only in the panel.
  - Re-run the Notch and Panel sections as regression.
- [ ] **Step 2: Budget.**
  - Use the dashboard: open it 10 times, run a timer, play music, copy 20 items, write a note.
  - Close it, stop the music, and leave the PC idle for 10 minutes.
  - Measure with the Plan 1 method.
  - Pass: total working set under 100 MB and CPU under 0.5%. If it fails, profile and raise it with the user before closing the plan.
- [ ] **Step 3: Docs.**
  - README: the dashboard, the tiles, edit mode, Settings, and where the notes, clipboard and shelf are stored.
  - Measurements doc: Phase 4 numbers.
- [ ] **Step 4: Run all tests.** Run `dotnet test NotWin.sln`. Expected: all pass.
- [ ] **Step 5: Commit.** Message: `docs: phase 4 measurements + README`. Ask the user before pushing `plan4-notch-dashboard` and opening a PR.

## User decisions (2026-11-19, while writing the plan)

1. Scan QR: **dropped** (no QR library). Page 2 has six tool buttons.
2. Snip: **copy to the clipboard and save a PNG** in `Pictures\Screenshots`.

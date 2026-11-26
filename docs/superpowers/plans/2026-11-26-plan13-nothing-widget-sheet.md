# Plan 13: Nothing widget sheet — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Eight new desktop widgets in the style of Nothing OS's widget sheet:
- Toggle
- Analogue clock
- World clock
- Stacked clock
- Stopwatch
- Screen time
- System meters
- Sun compass

**Architecture:** The pure logic goes in `NotWin.Core` with xUnit tests:
- the kinds and rules, and the per-widget choices;
- time-zone labels and clock-hand angles;
- the stopwatch, the screen-time log, meter maths and the solar position.

Each widget is an `IDesktopWidget` in `src/NotWin/Widgets`, built in `App.CreateWidget`. Two shared app pieces support them:
- card surfaces (dark, light or red; square or round);
- a choice popup that edit mode opens for Toggle, World clock and Analogue clock.

**Tech Stack:** C# / WPF on .NET 10, xUnit, P/Invoke (`DllImport`, like `PowerRequestNative`), `Windows.Devices.Radios`.

**Spec:** `docs/superpowers/specs/2026-11-26-nothing-widget-sheet-design.md`

## Global Constraints
- New kinds are appended to `WidgetKind`, numbered from 13: `Toggle = 13, AnalogClock, WorldClock, StackedClock, Stopwatch, ScreenTime, SystemMeters, SunCompass`.
- Colours come only from the tokens:
  - Ink `#101010`, card `#1A1A1A`, text `#E8E8E8`;
  - red `#D71921`, used only for "on / notice this";
  - Light surface `#E8E8E8`; Red surface `#D71921` with `#FFFFFF` content.
- Fonts are Space Grotesk / Space Mono (`TileChrome.Font` / `TileChrome.Mono`). Dot numerals use `DotMatrix`.
- **Personal data:** never show or log an SSID, Bluetooth device name, city, adapter name, window or app title. Logs read like `toggle wifi -> on`.
- No setting changes without a click on that tile.
- **Timers:** stop in `SetPaused(true)` and in `Dispose`. Intervals are fixed:
  - analogue clock with the light face: 1 s;
  - other clocks: on the minute;
  - stopwatch: on whole elapsed seconds, only while running;
  - meters: 2 s;
  - sun compass: 5 min;
  - screen-time sampling: 60 s.
- **Commits:** use the repo's configured no-reply email. No `Co-Authored-By` trailer and no "Generated with Claude Code" line.
- **Don't touch the user's machine:**
  - Don't start, stop or reinstall the installed NotWin, its watchdog or Explorer.
  - Don't flip the user's mic, camera, Wi-Fi, Bluetooth, theme or keep-awake from tests. Tests use fakes or registry keys under a test-only HKCU path.
- **Test commands:**
  - `dotnet test NotWin.sln` for everything;
  - `dotnet test tests/NotWin.Core.Tests --filter <Class>` for one Core test class;
  - `dotnet test tests/NotWin.Tests --filter <Class>` for one app test class.

## Review Focus
1. **Layouts saved by older builds and by other PCs through sync.** Unknown `Choice` values (a time zone this PC lacks, a toggle id from a newer build) must fall back to the default and not crash. The test goes in Task 1: `Sanitize_FixesUnknownChoices`.
2. **Screen time across midnight and after sleep:**
   - a sample that spans midnight is credited to the new day;
   - a gap over 5 minutes (sleep, NotWin closed) adds nothing.
   
   Tests in Task 3: `Sample_AfterGap_AddsNothing` and `Sample_AtMidnight_GoesToNewDay`.
3. **Meter counters going backwards** (an adapter reset, or 32-bit wrap) must give 0 B/s, never a huge or negative rate. The test goes in Task 4: `NetRate_CounterWentBack_IsZero`.
4. **The sun compass at polar latitudes, and the date line.** Polar day or night has no sunrise; longitudes near ±180 must still work. Tests in Task 5: `SunTimes_PolarNight_IsNone` and `Position_DateLine`.
5. **A world clock in a half-hour zone across a daylight-saving change** must give the right label and difference. The test goes in Task 2: `GmtLabel_HalfHourAndDst`.

---

### Task 1: Kinds, sizes, round rule and per-widget choices (Core)

**Files:**
- Modify: `src/NotWin.Core/Widgets/WidgetLayout.cs`
- Create: `src/NotWin.Core/Widgets/WidgetChoices.cs`
- Test: `tests/NotWin.Core.Tests/Widgets/SheetRulesTests.cs`
- Modify: `tests/NotWin.Core.Tests/Widgets/WidgetLayoutTests.cs:166` (picker order now has the new kinds)

**Interfaces:**
- Produces:
  - `WidgetKind.Toggle … SunCompass` (13–20);
  - `WidgetConfig.Choice` (a `string?` added as the last record parameter, default null);
  - `WidgetRules.IsRound(WidgetPlacement p) → bool`;
  - `WidgetChoices.Options(WidgetKind) → IReadOnlyList<string>` (ids);
  - `WidgetChoices.Default(WidgetKind kind, IEnumerable<WidgetPlacement> onDesktop) → string?`;
  - `WidgetChoices.Normalize(WidgetKind, string?) → string?`;
  - `WidgetChoices.Toggles` = `["wifi","bluetooth","dark","awake","mic","camera"]`;
  - `WidgetChoices.Faces` = `["light","dark"]`;
  - `WidgetChoices.Zones`: the 26 Windows zone ids below.

- [ ] **Step 1: Write the failing tests** in `SheetRulesTests`:
  - `Rules_NewKindSizes`. Each kind's sizes, default first:

    | Kind | Sizes |
    |---|---|
    | Toggle | `[(1,1),(2,1)]` |
    | AnalogClock | `[(1,1),(2,2)]` |
    | WorldClock | `[(1,1),(2,1)]` |
    | StackedClock | `[(1,2),(1,1)]` |
    | Stopwatch | `[(1,1),(2,1)]` |
    | ScreenTime | `[(2,1),(2,2)]` |
    | SystemMeters | `[(2,1),(2,2)]` |
    | SunCompass | `[(1,1),(2,2)]` |

    It also asserts `(int)WidgetKind.Toggle == 13` and `(int)WidgetKind.SunCompass == 20`.
  - `Rules_Repeatable`:
    - true for Photo, Folder, Toggle and WorldClock;
    - false for AnalogClock, StackedClock, Stopwatch, ScreenTime, SystemMeters and SunCompass.
  - `Rules_PickerOrder`: the exact list `[Photo, Folder, Toggle, Clock, AnalogClock, StackedClock, WorldClock, Snake, TicTacToe, Timer, Stopwatch, Calendar, Agenda, Notes, Alarm, Weather, Media, Battery, ScreenTime, SystemMeters, SunCompass]`. Update the old assertion at `WidgetLayoutTests.cs:166` to the same list.
  - `IsRound`:
    - true for Toggle, AnalogClock, WorldClock, StackedClock, Stopwatch and SunCompass at every size;
    - true for a Photo with `Shape="circle"`;
    - false for a Photo with `"square"`, for ScreenTime and for Clock.
  - `Default_Toggle_FirstUnused`:
    - desktop has toggles `wifi` and `bluetooth` → `"dark"`;
    - all six used → `"wifi"`;
    - WorldClock → `"UTC"`; AnalogClock → `"light"`; Clock → null.
  - `Normalize`:
    - `(Toggle,"bogus")` → `"wifi"`;
    - `(WorldClock,"Mars Standard Time")` → `"UTC"`;
    - `(AnalogClock,null)` → `"light"`;
    - `(Toggle,"mic")` → `"mic"`;
    - `(Photo,"x")` → `"x"` (other kinds are untouched).
  - `Sanitize_FixesUnknownChoices`: `DesktopLayout.Sanitize` on `[Toggle with Choice "zzz", WorldClock with Config null]` gives Choice `"wifi"` and `"UTC"`.
  - `Sanitize_KeepsTallSize`: `StackedClock (1,2)` survives Sanitize. `WidgetSize.Px(2)` is 284, so `PxH` is 284.
  - `Add_Toggle_TakesDefaultChoice`: `new DesktopLayout([]).Add(Toggle, 1600, 900, () => "t", WidgetChoices.ConfigFor(Toggle, []))` gives Choice `"wifi"`.

- [ ] **Step 2: Run `dotnet test tests/NotWin.Core.Tests --filter SheetRulesTests`.** Expected: a compile FAIL (the kinds don't exist yet).

- [ ] **Step 3: Implement the rules.**
  - Append the kinds.
  - Add `S12 = new(1, 2)` and the `Sizes` arms.
  - `IsRepeatable` also covers `Toggle or WorldClock`.
  - Replace `PickerOrder`.
  - Add `IsRound(WidgetPlacement p)`.
  - `WidgetChoices` also exposes `ConfigFor(WidgetKind kind, IEnumerable<WidgetPlacement> onDesktop) → WidgetConfig?`: null when `Default` is null, else `new WidgetConfig(Choice: …)`.
  - In `Sanitize`, after the size fix, set `Config = (p.Config ?? new()) with { Choice = WidgetChoices.Normalize(p.Kind, p.Config?.Choice) }` **only for** the three choice kinds.
  - `Zones`, in this order:
    `"Hawaiian Standard Time","Alaskan Standard Time","Pacific Standard Time","Mountain Standard Time","Central Standard Time","Eastern Standard Time","SA Eastern Standard Time","UTC-02","Azores Standard Time","UTC","GMT Standard Time","W. Europe Standard Time","E. Europe Standard Time","Arab Standard Time","Arabian Standard Time","Pakistan Standard Time","India Standard Time","Nepal Standard Time","Bangladesh Standard Time","SE Asia Standard Time","China Standard Time","Tokyo Standard Time","Cen. Australia Standard Time","AUS Eastern Standard Time","New Zealand Standard Time","Tonga Standard Time"`.
  - `Normalize` for WorldClock accepts any id in `Zones`.

- [ ] **Step 4: Run `dotnet test tests/NotWin.Core.Tests`.** Expected: all PASS, including the updated `WidgetLayoutTests`.

- [ ] **Step 5: Commit:** `feat(widgets): kinds, sizes and choices for the Nothing widget sheet`.

---

### Task 2: Clock maths, dot ring, stopwatch model, dot glyphs (Core)

**Files:**
- Create: `src/NotWin.Core/Widgets/ClockMath.cs`
  - `ClockHands`, `ZoneLabels`
- Create: `src/NotWin.Core/Widgets/DotRing.cs`
- Create: `src/NotWin.Core/Widgets/StopwatchModel.cs`
- Modify: `src/NotWin.Core/Notch/DotFont.cs`
  - add `'H'` and `'+'` glyphs, 5×7 like the digits
- Test: `tests/NotWin.Core.Tests/Widgets/ClockMathTests.cs`
- Test: `tests/NotWin.Core.Tests/Widgets/StopwatchModelTests.cs`

**Interfaces:**
- Produces:
  - `ClockHands.Angles(TimeOnly t) → (double Hour, double Minute, double Second)`: degrees clockwise from 12. The hour hand includes minutes; the minute hand includes seconds.
  - `ZoneLabels.GmtLabel(TimeSpan offset) → string`
  - `ZoneLabels.Difference(TimeSpan local, TimeSpan zone) → string`
  - `ZoneLabels.Offset(TimeZoneInfo zone, DateTime utcNow) → TimeSpan`
  - `DotRing.Points(int n, double radius) → IReadOnlyList<(double X, double Y)>`: the first point at 12 o'clock, going clockwise, centred on (0,0).
  - `StopwatchModel(Func<TimeSpan> clock)`:
    - members `Start()`, `Pause()`, `Reset()`;
    - `bool Running`;
    - `TimeSpan Elapsed`;
    - `TimeSpan UntilNextSecond`;
    - `static string Format(TimeSpan)`.

- [ ] **Step 1: Write the failing tests:**
  - `Angles_ThreeFifteen`: 03:15:00 → Hour 97.5, Minute 90, Second 0.
  - `Angles_Seconds`: 00:00:30 → Minute 3, Second 180.
  - `GmtLabel`:
    - `0` → `"GMT"`;
    - `+1h` → `"GMT+1"`;
    - `−3h` → `"GMT−3"` (U+2212);
    - `+5:30` → `"GMT+5:30"`;
    - `+5:45` → `"GMT+5:45"`.
  - `Difference`:
    - local +1h, zone +5:30 → `"+4H 30M"`;
    - equal → `"SAME TIME"`;
    - local +1h, zone 0 → `"−1H"`.
  - `GmtLabel_HalfHourAndDst`:
    - `Offset(FindSystemTimeZoneById("Cen. Australia Standard Time"), 2026-01-15 00:00Z)` → +10:30 → `"GMT+10:30"`;
    - the same zone at 2026-07-15 → +9:30.
  - `DotRing_Points`: `Points(4, 10)` ≈ `[(0,-10),(10,0),(0,10),(-10,0)]`, within 1e-9.
  - `DotFont_HasHAndPlus`: `DotFont.ColumnsFor("6H20")` equals `ColumnsFor("6020")`.
  - Stopwatch, using a fake clock you advance:
    - `Starts_Pauses_Resumes`: start, +1.5 s, pause, +10 s, start, +1 s → `Elapsed` 2.5 s.
    - `Reset_OnlyWhenPaused`: Reset while running does nothing; Reset while paused → 0.
    - `UntilNextSecond`: running at 2.25 s elapsed → 0.75 s.
    - `Format`:
      - 0 → `"00:00"`;
      - 75 s → `"01:15"`;
      - 3725 s → `"1:02"` (H:MM once past an hour).

- [ ] **Step 2: Run** `dotnet test tests/NotWin.Core.Tests --filter "ClockMathTests|StopwatchModelTests"`. Expected: FAIL.

- [ ] **Step 3: Implement** the signatures above.
  - `Offset` is `zone.GetUtcOffset(utcNow)`.
  - Labels drop `:00`.

- [ ] **Step 4: Run the same filter.** Expected: PASS.

- [ ] **Step 5: Commit:** `feat(widgets): clock maths, dot ring and stopwatch model`.

---

### Task 3: Screen-time log (Core)

**Files:**
- Create: `src/NotWin.Core/Widgets/ScreenTimeLog.cs`
- Test: `tests/NotWin.Core.Tests/Widgets/ScreenTimeLogTests.cs`

**Interfaces:**
- Produces `sealed class ScreenTimeLog`:
  - `ScreenTimeLog(IReadOnlyDictionary<string,int>? saved)`: keys are `"yyyy-MM-dd"`, values are minutes.
  - `bool Sample(DateTime localNow, bool unlocked, TimeSpan idle)`: returns true when today's total changed.
  - `int Today(DateTime localNow)`
  - `int Average7(DateTime localNow)`: the mean over the 7 days before today that have data; 0 when none.
  - `IReadOnlyList<(DateOnly Day, int Minutes)> Week(DateTime localNow)`: 7 entries, oldest first, ending today; missing days are 0.
  - `IReadOnlyDictionary<string,int> ToSave(DateTime localNow)`: only the last 8 days.
  - `static string Format(int minutes)`: `"6H20"`, `"0H05"`, `"12H00"`.

- [ ] **Step 1: Write the failing tests:**
  - `Sample_CountsActiveMinutes`: three samples 1 min apart, unlocked, idle 10 s → Today 2. The first sample only sets the baseline.
  - `Sample_LockedOrIdle_AddsNothing`: a locked sample, or one with idle 5 min 1 s → no minutes added.
  - `Sample_AfterGap_AddsNothing`: two samples 20 min apart → +0 (sleep, or NotWin closed).
  - `Sample_AtMidnight_GoesToNewDay`: samples at 23:59:30 and 00:00:30 → the new day gets 1, the old day is unchanged.
  - `Week_FillsMissingDaysWithZero`, `Average7_IgnoresToday`, `ToSave_KeepsEight`, `Format_Examples`.

- [ ] **Step 2: Run** `--filter ScreenTimeLogTests`. Expected: FAIL.

- [ ] **Step 3: Implement.** A sample credits the whole minutes between the previous sample and now when all of these hold:
  - the gap is ≤ 2 min;
  - `unlocked`;
  - `idle < 5 min`.

  The credit goes to `localNow`'s date. Fractions carry over in a seconds remainder.

- [ ] **Step 4: Run the filter.** Expected: PASS.

- [ ] **Step 5: Commit:** `feat(widgets): screen-time log`.

---

### Task 4: Meter maths (Core)

**Files:**
- Create: `src/NotWin.Core/Widgets/MeterMath.cs`
- Test: `tests/NotWin.Core.Tests/Widgets/MeterMathTests.cs`

**Interfaces:**
- Produces:
  - `MeterMath.CpuPercent((ulong Idle, ulong Kernel, ulong User) before, (ulong Idle, ulong Kernel, ulong User) after) → int`: 0–100. Kernel time includes idle, as `GetSystemTimes` reports it.
  - `MeterMath.NetRate(long bytesBefore, long bytesAfter, TimeSpan dt) → double` (bytes/s)
  - `MeterMath.FormatRate(double bytesPerSec) → string`
  - `MeterMath.Lit(int percent, int dots) → int`
  - `MeterMath.Hot(int percent) → bool`: true when > 90.

- [ ] **Step 1: Write the failing tests:**
  - `Cpu_Half`: idle +50, kernel +60, user +40 → 50. The busy time is (kernel − idle) + user = 50 out of a total of 100.
  - `Cpu_NoTime_IsZero`.
  - `NetRate_CounterWentBack_IsZero`.
  - `NetRate` 2 000 000 B over 2 s → 1e6.
  - `FormatRate`:
    - 0 → `"0 KB/s"`;
    - 340 000 → `"332 KB/s"` (1024-based, rounded);
    - 1 258 291 → `"1.2 MB/s"`.
  - `Lit`: (0,10) → 0; (5,10) → 1; (100,10) → 10. A non-zero value shows at least 1 dot (round up).
  - `Hot`: 91 → true; 90 → false.

- [ ] **Step 2: Run the filter.** Expected: FAIL.

- [ ] **Step 3: Implement.**

- [ ] **Step 4: Run the filter.** Expected: PASS.

- [ ] **Step 5: Commit:** `feat(widgets): meter maths`.

---

### Task 5: Solar position (Core)

**Files:**
- Create: `src/NotWin.Core/Widgets/SolarPosition.cs`
- Test: `tests/NotWin.Core.Tests/Widgets/SolarPositionTests.cs`

**Interfaces:**
- Produces:
  - `SolarPosition.Position(DateTime utc, double lat, double lon) → (double Azimuth, double Elevation)`: degrees; azimuth is clockwise from north.
  - `SolarPosition.SunTimes(DateOnly date, double lat, double lon, TimeZoneInfo zone) → (TimeOnly Rise, TimeOnly Set)?`: null for polar day or night.

- [ ] **Step 1: Write the failing tests.** Expected values come from the NOAA calculator, with tolerance ±1° and ±2 min:
  - `Position_London_Noon`: 2026-06-21 12:00Z, (51.5074, −0.1278) → elevation ≈ 61.9, azimuth ≈ 178.
  - `Position_Sydney`: 2026-12-21 02:00Z, (−33.8688, 151.2093) → elevation ≈ 79.5.
  - `SunTimes_Delhi`: 2026-03-20, (28.6139, 77.2090), `India Standard Time` → rise ≈ 06:23, set ≈ 18:30.
  - `SunTimes_PolarNight_IsNone`: 2026-12-21, (78.22, 15.65) → null.
  - `Position_DateLine`: 2026-03-20 00:00Z at lon 179.9 and at −179.9 → elevations within 0.5° of each other.

- [ ] **Step 2: Run the filter.** Expected: FAIL.

- [ ] **Step 3: Implement the NOAA general solar position algorithm.** Use the fractional year γ, the equation of time, declination, hour angle and the zenith; sunrise/sunset use zenith 90.833°. Convert to `zone` with `TimeZoneInfo.ConvertTimeFromUtc`. Write it from the published equations; don't copy anyone's code.

- [ ] **Step 4: Run the filter.** Expected: PASS.

- [ ] **Step 5: Commit:** `feat(widgets): solar position and sun times`.

---

### Task 6: Card surfaces, round cards, dot ring view, choice popup (app)

**Files:**
- Modify: `src/NotWin/Widgets/WidgetCard.cs`
  - `Surface` enum
  - `IDesktopWidget.Surface`, `IDesktopWidget.Round`
  - `Wrap` overload
- Modify: `src/NotWin/Widgets/EditBackdrop.cs:103-104`
  - the outline radius uses `WidgetRules.IsRound`
- Modify: `src/NotWin/Widgets/DesktopWidgets.cs`
  - wrap with surface / round;
  - a still click in edit mode on a choice kind opens `ChoicePicker`;
  - ＋ Add passes `WidgetChoices.ConfigFor`
- Modify: `src/NotWin/Widgets/AddPicker.cs`
  - `KindName` / `KindIcon` for the new kinds
- Create: `src/NotWin/Widgets/ChoicePicker.cs`
- Create: `src/NotWin/Notch/DotRingView.cs`
- Test: `tests/NotWin.Tests/WidgetSurfaceTests.cs`

**Interfaces:**
- Consumes: `WidgetRules.IsRound`, `WidgetChoices.*`, `DotRing.Points` (Tasks 1–2).
- Produces:
  - `enum Surface { Dark, Light, Red }`
  - `IDesktopWidget.Surface Surface => Surface.Dark`; `bool Round => false`
  - `WidgetCard.Wrap(FrameworkElement content, Surface surface, bool round, WidgetSize size, bool bare) → FrameworkElement`. The old two-argument `Wrap` stays and forwards (Dark, square).
  - `WidgetCard.Ink(Surface)` → the content brush: Light → `#101010`, Dark → `#E8E8E8`, Red → `#FFFFFF`.
  - `WidgetCard.Dim(Surface)`: a 40% alpha version of the ink.
  - `DotRingView : FrameworkElement`: `int Count`, `int Lit`, `double Radius`, `double Dot`, `Brush LitFill`, `Brush DimFill`.
  - `ChoicePicker.Show(IReadOnlyList<(string Id, string Label)> options, string? current, RectI below, Action<string> pick)`: the same window behaviour as `AddPicker`, with "●" before the current option.
  - `ChoicePicker.Labels(WidgetKind) → IReadOnlyList<(string Id, string Label)>`:
    - toggles: "Wi-Fi", "Bluetooth", "Dark mode", "Keep awake", "Mic access", "Camera access";
    - faces: "Light face", "Dark face";
    - zones: the `TimeZoneInfo.DisplayName` of each `WidgetChoices.Zones` id found on this PC, in order; missing ids are skipped.
  - `IDesktopWidget` also gains `event Action? CardChanged { add { } remove { } }` (a default no-op). DesktopWidgets subscribes when it creates a slot and re-wraps that one slot when it fires. A toggle changes surface when it flips.

- [ ] **Step 1: Write the failing STA tests** (copy the `OnSta` helper from `WidgetRootTests`):
  - `Wrap_Light_UsesLightBackgroundAndInk`: the Border background is `#E8E8E8`.
  - `Wrap_Red_NoEdge`: background `#D71921`, `BorderThickness` 0.
  - `Wrap_Round_RadiusIsHalfShorterSide`:
    - 1×1 → 68;
    - 2×1 → 68;
    - 1×2 → 68;
    - 2×2 → 142.
  - `Wrap_Square_Radius26`.
  - `ChoiceLabels_Toggle`: the six labels, in order.
  - `ChoiceLabels_Zones_SkipMissing`: every returned id is in `WidgetChoices.Zones`, and the count is ≥ 20.
  - `DotRingView_Measures`: `Radius` 50, `Dot` 4 → desired size 104×104.

- [ ] **Step 2: Run `dotnet test tests/NotWin.Tests --filter WidgetSurfaceTests`.** Expected: FAIL.

- [ ] **Step 3: Implement.**
  - Round cards use `Padding = 10`.
  - In `DesktopWidgets.EndDrag`, the still-click branch that today handles only Folder also handles `Toggle`, `WorldClock` and `AnalogClock`: it opens `ChoicePicker` under the widget's screen rect. A pick runs `Apply(_layout.WithConfig(id, config with { Choice = picked }))`.
  - `ShowPicker` passes `WidgetChoices.ConfigFor(kind, _layout.Widgets)` for non-folder kinds.
  - `KindName`: "Toggle", "Analogue clock", "World clock", "Stacked clock", "Stopwatch", "Screen time", "System meters", "Sun compass".

- [ ] **Step 4: Run `dotnet test NotWin.sln`.** Expected: all PASS.

- [ ] **Step 5: Commit:** `feat(widgets): card surfaces, round cards and the choice popup`.

---

### Task 7: Toggle widget and its backends (app + Core state rule)

**Files:**
- Create: `src/NotWin.Core/Widgets/PrivacyConsent.cs`
- Create: `src/NotWin.Native/SystemNative.cs`
  - `BroadcastSettingChange(string area)`: `SendMessageTimeout(HWND_BROADCAST, WM_SETTINGCHANGE, 0, area, SMTO_ABORTIFHUNG, 200)`
  - `RegistryWatch : IDisposable`: `RegNotifyChangeKeyValue` with an event, plus `ThreadPool.RegisterWaitForSingleObject`; it raises `Changed` and re-arms.
  - `GetSystemTimes`, `GlobalMemoryStatusEx`, `IdleTime()` (`GetLastInputInfo` vs `Environment.TickCount64`), for Tasks 9–10.
- Create: `src/NotWin/Widgets/ToggleBackends.cs`
  - `IToggleBackend`, and the implementations `RadioToggle`, `DarkModeToggle`, `KeepAwakeToggle`, `PrivacyToggle`
- Create: `src/NotWin/Widgets/ToggleWidget.cs`
- Modify: `src/NotWin/App.xaml.cs`
  - `CreateWidget` gets `case Toggle`
- Test: `tests/NotWin.Core.Tests/Widgets/PrivacyConsentTests.cs`
- Test: `tests/NotWin.Tests/ToggleWidgetTests.cs`

**Interfaces:**
- Consumes:
  - `WidgetConfig.Choice`, `WidgetChoices.Toggles` (Task 1);
  - `Surface`, `CardChanged`, `ChoicePicker.Labels` (Task 6);
  - `RadioService.IsOnAsync` / `SetAsync`;
  - `KeepAwakeService.On` / `Set` / `Changed`.
- Produces:
  - `enum ToggleState { Off, On, Blocked, Unavailable }`
  - `PrivacyConsent.State(string? machine, string? user, string? nonPackaged) → ToggleState`:
    - machine `"Deny"` → Blocked;
    - user and nonPackaged both `"Allow"` → On;
    - otherwise Off. Missing values count as `"Allow"`, which is the Windows default.
  - `PrivacyConsent.CanChange(string? machine) → bool`
  - `interface IToggleBackend : IDisposable`:
    - `Task<ToggleState> ReadAsync()`
    - `Task FlipAsync()`
    - `string SettingsUri`
    - `event Action Changed`
  - `PrivacyToggle(string capability, string userRoot = @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore")`: tests pass a throwaway HKCU root.
  - `ToggleWidget(Func<string, IToggleBackend> backendFor)`

- [ ] **Step 1: Write the failing tests:**
  - Core `PrivacyConsentTests`:
    - `(Allow, Allow, Allow)` → On;
    - `(null, null, null)` → On;
    - `(Allow, Deny, Allow)` → Off;
    - `(Allow, Allow, Deny)` → Off;
    - `(Deny, Allow, Allow)` → Blocked.
    - `CanChange("Deny")` → false.
  - App `ToggleWidgetTests`:
    - `PrivacyToggle_FlipsBothValues`: under `HKCU\Software\NotWin.Tests\{guid}\ConsentStore\microphone`, Value and NonPackaged\Value start as Allow. `FlipAsync` makes both Deny, and `ReadAsync` reports Off; flipping again makes both Allow. Delete the key in `finally`. The machine root is read from HKLM, which is read-only here.
    - `ToggleWidget_Surfaces`, with a fake backend: On → `Surface.Light`, Off → `Dark`, `mic` Blocked → `Red`, Unavailable → `Dark`.
    - `ToggleWidget_BuildsBothSizes`: 1×1 and 2×1 build on STA without throwing. The 2×1 text shows only the name and the state words "On" / "Off" / "Blocked" / "Unavailable".
    - `ToggleWidget_ClickFlips_UnavailableOpensSettings`: the fake backend records the flip; when Unavailable, a `launch(uri)` delegate gets `SettingsUri`.

- [ ] **Step 2: Run** `dotnet test tests/NotWin.Core.Tests --filter PrivacyConsentTests` and `dotnet test tests/NotWin.Tests --filter ToggleWidgetTests`. Expected: FAIL.

- [ ] **Step 3: Implement.** The backends:
  - **Radio:** `Radio.StateChanged` raises `Changed`. Settings URIs `ms-settings:network-wifi` / `ms-settings:bluetooth`.
  - **DarkMode:**
    - reads HKCU `…\Themes\Personalize\AppsUseLightTheme`;
    - writes both values, 0 or 1;
    - calls `SystemNative.BroadcastSettingChange("ImmersiveColorSet")`;
    - `Changed` comes from `SystemEvents.UserPreferenceChanged`.
  - **KeepAwake:** wraps the app's single `_awake` service.
  - **Privacy:** `RegistryWatch` on the user key; it won't flip when `CanChange` is false.
  
  Every flip logs `toggle <id> -> <state>`. The widget also needs:
  - **Icons:** `Path` geometries, 24-unit box, stroke 1.6 — Wi-Fi, BT (reuse the `TilesSection` glyph strings), half-filled circle, cup, mic, camera; slashed variants for Blocked.
  - **Clicks:** a click flips through the backend; a click on an Unavailable tile opens `SettingsUri` via `Process.Start` with `UseShellExecute`.

- [ ] **Step 4: Run `dotnet test NotWin.sln`.** Expected: all PASS.

- [ ] **Step 5: Commit:** `feat(widgets): toggle tiles (Wi-Fi, Bluetooth, dark mode, keep awake, mic, camera)`.

---

### Task 8: Analogue, world and stacked clocks (app)

**Files:**
- Create: `src/NotWin/Widgets/AnalogClockWidget.cs`
- Create: `src/NotWin/Widgets/WorldClockWidget.cs`
- Create: `src/NotWin/Widgets/StackedClockWidget.cs`
- Create: `src/NotWin/Widgets/MinuteTicker.cs`: the on-the-minute `DispatcherTimer` that `ClockWidget.Tick` uses today, pulled out so it can be shared. `ClockWidget` switches to it.
- Modify: `src/NotWin/App.xaml.cs`, `CreateWidget`
- Test: `tests/NotWin.Tests/ClockWidgetsTests.cs`

**Interfaces:**
- Consumes: `ClockHands`, `ZoneLabels`, `WidgetChoices.Faces` / `Zones`, `Surface` (Tasks 1, 2, 6).
- Produces:
  - `MinuteTicker(Action tick)` with `Start()`, `Stop()`, `Dispose()`.
  - `AnalogClockWidget(Func<DateTime> now)`: its `Surface` is Light for `light` and Dark for `dark`.
  - `WorldClockWidget(Func<DateTime> utcNow)`
  - `StackedClockWidget(Func<DateTime> now)`

- [ ] **Step 1: Write the failing STA tests:**
  - `Analog_LightFace_HasSecondsHand_DarkFaceDoesNot`. The hand elements are named `Hour`, `Minute`, `Second`; find them by `Name`.
  - `Analog_Paused_StopsSecondTimer`: expose `internal bool Ticking` and use `InternalsVisibleTo NotWin.Tests` (already set up if `AssemblyInfo` has it; otherwise add it).
  - `World_ShowsZoneTimeAndLabel`: with utcNow 2026-01-15 10:00Z and zone `India Standard Time`, the dot text is `"15:30"` and a TextBlock contains `"GMT+5:30"`.
  - `Stacked_SplitsHoursAndMinutes`: at 22:10, two `DotMatrix` show `"22"` and `"10"`.
  - `All_BuildEverySize`.

- [ ] **Step 2: Run** `--filter ClockWidgetsTests`. Expected: FAIL.

- [ ] **Step 3: Implement.**
  - **Analogue face:** drawn in `OnRender` of a small `FrameworkElement`: 12 ticks, the hands as rounded lines, the red dot or red seconds hand.
  - **World clock day text:** `ddd` at 1×1 and `dddd` at 2×1, in InvariantCulture, upper-case.

- [ ] **Step 4: Run `dotnet test NotWin.sln`.** Expected: PASS.

- [ ] **Step 5: Commit:** `feat(widgets): analogue, world and stacked clocks`.

---

### Task 9: Stopwatch and system meters (app)

**Files:**
- Create: `src/NotWin/Widgets/StopwatchWidget.cs`: owns a `StopwatchModel`, which lives as long as the widget object does.
- Create: `src/NotWin/Widgets/SystemMetersWidget.cs`
- Create: `src/NotWin/Widgets/MeterSampler.cs`
- Modify: `src/NotWin/App.xaml.cs`
- Test: `tests/NotWin.Tests/StopwatchAndMetersTests.cs`

**Interfaces:**
- Consumes: `StopwatchModel`, `DotRingView`, `MeterMath`, and `SystemNative.GetSystemTimes` / `GlobalMemoryStatusEx` (Tasks 2, 4, 6, 7).
- Produces:
  - `MeterSampler : IDisposable`, with an injectable source `Func<MeterReading>`:
    - `record MeterReading((ulong,ulong,ulong) Cpu, int RamPercent, long NetBytes, TimeSpan At)`
    - `event Action<int Cpu, int Ram, double NetRate> Sampled`
    - `Start()` and `Stop()`, on a 2 s `DispatcherTimer`
    - `IReadOnlyList<int> CpuHistory`: the last 20 samples.
  - `StopwatchWidget(Func<TimeSpan> clock)`
  - `SystemMetersWidget(Func<MeterSampler> sampler)`

- [ ] **Step 1: Write the failing STA tests:**
  - **Stopwatch:**
    - `Stopwatch_Click_StartsAndPauses`;
    - `Stopwatch_DoubleClickWhilePaused_Resets`;
    - `Stopwatch_RingLitEqualsSecondsOfMinute`: with 75 s elapsed, `DotRingView.Lit` is 15;
    - `Stopwatch_Paused_NoTimer`.
  - **Meters:**
    - `Meters_FirstSampleShowsNothingHot`;
    - `Meters_History_KeepsTwenty`;
    - `Meters_StopWhenPaused`: after `SetPaused(true)`, the sampler isn't running.
    - `Meters_NoAdapterNames`: the visual tree's TextBlocks contain only "CPU", "RAM", "NET", numbers and units.

- [ ] **Step 2: Run** `--filter StopwatchAndMetersTests`. Expected: FAIL.

- [ ] **Step 3: Implement.**
  - **Network bytes:** sum `GetIPv4Statistics().BytesReceived + BytesSent` over `NetworkInterface.GetAllNetworkInterfaces()` where the type isn't Loopback or Tunnel and the status is Up. The 2×2 shows down and up separately.
  - **Stopwatch redraws** are scheduled with `UntilNextSecond`.

- [ ] **Step 4: Run `dotnet test NotWin.sln`.** Expected: PASS.

- [ ] **Step 5: Commit:** `feat(widgets): stopwatch and system meters`.

---

### Task 10: Screen time and sun compass (app)

**Files:**
- Create: `src/NotWin/Widgets/ScreenTimeTracker.cs`: app-wide. Started in `App` startup, flushed in `OnExit`.
- Create: `src/NotWin/Widgets/ScreenTimeWidget.cs`
- Create: `src/NotWin/Widgets/SunCompassWidget.cs`
- Modify: `src/NotWin.Core/Widgets/DotArt.cs`: add an `"arrow"` 11×11 picture, which is **not** added to `Pictures`. The folder list is unchanged.
- Modify: `src/NotWin/App.xaml.cs`
  - `_screenTime` field;
  - start it after widgets;
  - `Dispose` in `OnExit`;
  - the factory cases.
- Test: `tests/NotWin.Tests/ScreenTimeAndSunTests.cs`

**Interfaces:**
- Consumes: `ScreenTimeLog`, `SolarPosition`, `DotRingView`, `SystemNative.IdleTime`, `WeatherService.Place` / `Changed`, `JsonFile.WriteAtomic` (Tasks 3, 5, 6, 7).
- Produces:
  - `ScreenTimeTracker(string dataDir, Func<DateTime> now, Func<TimeSpan> idle)`:
    - `ScreenTimeLog Log`;
    - `event Action Changed`;
    - `Start()`, `Flush()`, `Dispose()`.
    - The file is `screentime.json`, written at most every 5 min.
  - `ScreenTimeWidget(ScreenTimeTracker tracker)`
  - `SunCompassWidget(Func<WeatherPlace?> place, Action<Action> subscribe, Action<Action> unsubscribe, Func<DateTime> utcNow)`. `WeatherService` is sealed, so App passes `() => _weather.Place`, `h => _weather.Changed += h` and `h => _weather.Changed -= h`.
  - `SunCompassWidget.ArrowAngle → double`: internal, for tests.

- [ ] **Step 1: Write the failing tests:**
  - `Tracker_WritesAndReloads`: a temp dir, a fake clock and idle; 3 samples → `Flush` → a new tracker reads Today = 2.
  - `Tracker_LockedSamplesIgnored`: raise the session-lock handler through an internal `OnSessionSwitch(bool locked)`.
  - `ScreenTime_2x2_HasSevenColumns_TodayRed`.
  - `Sun_NoPlace_ShowsHint`: the text is `"SET A CITY IN SETTINGS"`, and no city text appears anywhere.
  - `Sun_ArrowFollowsAzimuth`: London at 2026-06-21 12:00Z → `ArrowAngle` within 1° of `SolarPosition.Position(...).Azimuth`.
  - `Sun_BelowHorizon_Dim_2x2Text`: London at 2026-12-21 00:00Z → `"BELOW HORIZON"`.

- [ ] **Step 2: Run** `--filter ScreenTimeAndSunTests`. Expected: FAIL.

- [ ] **Step 3: Implement.**
  - The tracker samples every 60 s with `Log.Sample(now, !_locked, idle())`. `SystemEvents.SessionSwitch` sets `_locked`, and the handler is detached in `Dispose`.
  - The screen-time widget redraws on `Changed`.
  - The sun compass redraws every 5 min and on `WeatherService.Changed`.

- [ ] **Step 4: Run `dotnet test NotWin.sln`.** Expected: PASS.

- [ ] **Step 5: Commit:** `feat(widgets): screen time and sun compass`.

---

### Task 11: Verify, the live mic check, install, on-screen check

- [ ] **Step 1: Run `dotnet test NotWin.sln`.** Expected: 0 failed.
- [ ] **Step 2: Run `pwsh build\build.ps1` unelevated.** Expected: it builds without errors. This only builds; it doesn't install.
- [ ] **Step 3: Ask the user** before reinstalling. Reinstalling is `build\install.ps1` elevated, which raises an admin prompt and restarts the installed NotWin.
- [ ] **Step 4: On-screen check, after the user's OK and install.**
  - The user adds each widget from ＋ Add and checks sizes, the choice lists, the round shapes and the surfaces.
  - **Mic / camera live check (spec):** only if the user agrees, they click the Mic tile themselves. Confirm:
    - Windows Settings › Privacy › Microphone shows it off;
    - an app such as Voice Recorder loses the mic;
    - a second click restores it.
- [ ] **Step 5: Wrap-up.**
  - Report the results and the `nothing-widgets` branch.
  - Ask before any push or PR.

---

## Follow-up tasks (spec "Follow-up": dark mode wallpaper, widget theme, red toggles)

### Task 12: Wallpaper decision and the saved path (Core)
- `DarkModeWallpaper.OnFlip(bool turningDark, string? current, string? remembered, string darkPath, Func<string, bool> exists) → (string? Apply, string? Remember)`:
  - turning dark: apply `darkPath` when it exists; remember `current` (unless `current` already is `darkPath`, then keep `remembered`);
  - turning light: apply `remembered` only when it exists **and** the wallpaper is still `darkPath` (a wallpaper the user picked meanwhile is kept); remember null.
- `ShellSettings.WallpaperBeforeDark` (string?), per-PC: `SyncRule.Merge` keeps mine, `SyncRule.Shared` blanks it.
- Tests: `DarkModeWallpaperTests` (each rule above, missing files), `SyncRule` keeps / blanks the field.

### Task 13: Widget theme brushes (app)
- `WidgetTheme` (Widgets): mutable `Card`, `Edge`, `Text`, `Dim`, `Unlit` brushes; `Light` flag; `Apply(bool light)` recolours them in place; `FollowWindows()` reads HKCU `AppsUseLightTheme` now and on `UserPreferenceChanged`.
- `WidgetCard`: `Surface.Dark` uses the theme's card / edge; `Ink(Dark)` / `Dim(Dark)` return the theme brushes. Widget files use `WidgetTheme.Text` / `Dim` / `Label(...)` instead of `TileChrome.Text` / `Dim` / `Label(...)`.
- Tests: `WidgetThemeTests` — after `Apply(true)` a wrapped Dark card's background is `#E8E8E8` and `Ink(Dark)` is `#101010`; `Apply(false)` restores; red cards unchanged.

### Task 14: Red-on toggles and the dark-mode wallpaper (app)
- `ToggleWidget.Surface`: On → Red; everything else → Dark (theme card); privacy Off / Blocked slashed.
- `DarkModeToggle(Func<string?> current, Action<string> apply, Func<string?> remembered, Action<string?> remember, string darkPath)` uses `DarkModeWallpaper.OnFlip` after writing the theme.
- Tests: surfaces; dark-mode flip applies / restores through fakes (no real wallpaper or theme change in tests — the theme write goes through an injectable key root).

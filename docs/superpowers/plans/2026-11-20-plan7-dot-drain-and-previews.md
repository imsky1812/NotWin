# Plan 7: Dot drain minimize + dock window previews — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Minimizing a window plays a Nothing-style "dot drain" into its dock icon (reverse on restore), and hovering a running app in the dock shows live window previews.

**Architecture:** Pure maths in Core (dot sampling, motion, the animation cap, preview sizing, hover timing) with tests; thin Win32 wrappers in Native (a minimize WinEvent hook, DWM thumbnails, a screen-region capture); WPF pieces in the app (a shared click-through overlay that draws the dots, a preview card with DWM thumbnails) wired through the dock controller and two Settings switches.

**Tech Stack:** C# / WPF on .NET 10 (`net10.0-windows10.0.22621.0`), xUnit; user32 / dwmapi / gdi32 P/Invoke.

**Spec:** `docs/superpowers/specs/2026-11-20-dot-drain-and-window-previews-design.md`. Branch: `plan5-desktop-widgets`.

## Global Constraints

- Dot pitch **14 px at 100 %** (× DPI scale); radius from luminance (brighter = larger), max radius `pitch × 0.42`; dots white with a thin dark rim.
- Minimize: **480 ms** total, stagger **0–180 ms** nearest-to-icon first, ease-in, arrival scale **30 %**, then a **160 ms** red dot blink (`#E8231F`) on the icon. Restore: **420 ms** reverse.
- Snapshot black test: over **97 %** of samples near-black (luminance < 12) → grey-outline fallback (B).
- At most **4** animations at once; only windows on the dock's monitor; none while a full-screen app is in front or the launcher / panel covers the dock.
- Overlay: one transparent, topmost, click-through, no-activate window over the dock's monitor, created on demand, destroyed when the last animation ends.
- Previews: open after **400 ms** hover on an item with ≥ 1 window, close **300 ms** after leaving item and card; thumbnails fit **220 × 140** DIP, aspect kept; up to **6** in a row, then the row scrolls; card `#1A1A1A`, radius **22**, 1.5 px edge.
- Settings > Shell: **"Dot drain when minimizing"** and **"Window previews in the dock"**, both on by default; hint under the first: "Turn off Windows' own minimize animation for the best look". The shell never changes that Windows setting.
- Never log window titles or captured content; snapshots are never written to disk.

## Review Focus

1. **A window that minimizes while its picture is unavailable** (DRM video, a game, a window that closes in the same instant): grey-outline fallback or no animation, never a stuck overlay. *(Task 4 `Controller_ThumbnailFails_UsesFallback`, `Controller_OverlayGoneAfterLastEnds`.)*
2. **Win+M / minimize-all bursts**: at most 4 animate, the rest minimize plainly, no stutter. *(Task 1 `Gate_AllowsFourAtOnce`.)*
3. **A window on a second monitor or off-screen restore rectangle**: no animation, nothing drawn off the dock's monitor. *(Task 4 `Controller_OtherMonitor_Skips`.)*
4. **Hover flicker between dock items and the card**: moving from item to card keeps it open; item to item switches without closing. *(Task 2 `Hover_ItemToCard_StaysOpen`, `Hover_ItemToItem_Switches`.)*
5. **A previewed window that closes while the card is open**: its preview disappears, the card closes when empty, no DWM handle leaks. *(Task 5 `Card_WindowGone_RemovesPreview`.)*

---

## File Structure

| File | Responsibility |
|---|---|
| `src/NothingShell.Core/Dock/DotDrain.cs` (create) | sampling a grey image into dots, near-black test, outline fallback, motion at time t, animation gate |
| `src/NothingShell.Core/Dock/PreviewRules.cs` (create) | thumbnail size, hover timing state machine |
| `src/NothingShell.Native/MinimizeHook.cs` (create) | WinEvent hook for 0x0016 / 0x0017 |
| `src/NothingShell.Native/DwmThumbnail.cs` (create) | register / place / unregister a DWM thumbnail; source size |
| `src/NothingShell.Native/ScreenCaptureNative.cs` (modify) | `CaptureGray(RectI)` → grey bytes of a screen region |
| `src/NothingShell/Dock/DotDrainOverlay.cs` (create) | the shared overlay window and its drawing |
| `src/NothingShell/Dock/DotDrainController.cs` (create) | minimize / restore → snapshot → dots → overlay |
| `src/NothingShell/Dock/WindowPreviewCard.cs` (create) | the preview popup with DWM thumbnails |
| `src/NothingShell/Dock/DockWindow.xaml(.cs)`, `DockController.cs` (modify) | icon rectangles, hover events, card wiring, tooltip only for non-running items |
| `src/NothingShell.Core/ShellSettings.cs`, `Settings/SettingsWindow.cs`, `App.xaml.cs` (modify) | the two switches and wiring |

---

### Task 1: Dot drain maths (Core)

**Files:** Create `src/NothingShell.Core/Dock/DotDrain.cs`; Test `tests/NothingShell.Core.Tests/Dock/DotDrainTests.cs`

**Interfaces — Produces:**
- `readonly record struct DrainDot(double X, double Y, double R, double Delay)` (Delay in ms, 0–180).
- `static class DotDrain`:
  - `const double TotalMs = 480, RestoreMs = 420, MaxStaggerMs = 180, ArrivalScale = 0.3, BlinkMs = 160;`
  - `static bool IsNearBlack(byte[] gray)` — over 97 % of bytes < 12.
  - `static IReadOnlyList<DrainDot> FromImage(byte[] gray, int w, int h, double originX, double originY, double pitch, double targetX, double targetY)` — one dot per grid cell (cell centre sampled), `R = pitch × 0.42 × lum/255`, dots with `R < 0.6` dropped; Delay by distance to target, nearest 0, farthest 180.
  - `static IReadOnlyList<DrainDot> Outline(double x, double y, double w, double h, double pitch, double targetX, double targetY)` — fallback B: every cell, `R = pitch × 0.30`.
  - `static (double X, double Y, double Scale, double Alpha) At(DrainDot d, double targetX, double targetY, double tMs, bool restore)` — minimize: before Delay at its spot; then over `TotalMs − MaxStaggerMs` it moves along a quadratic Bézier (control point = (d.X, targetY), i.e. pulled down) with ease-in `p²`, scale 1→0.3; restore = the same path run backwards over `RestoreMs`.
  - `sealed class DrainGate { bool TryStart(); void End(); int Running { get; } }` — max 4.
- [ ] **Step 1: Tests:** `IsNearBlack_97Percent` (970 of 1000 zeros → true; 960 → false), `FromImage_BrightIsBigger_DarkDropped` (a 28×14 image, left half 255 / right half 0, pitch 14 → one dot, R = 14×0.42), `FromImage_NearestStartsFirst` (target at the bottom-left: the bottom-left dot has Delay 0, the farthest 180), `At_EndsOnTheIconSmall` (`At(d, tx, ty, 480, false)` → (tx, ty, 0.3)), `At_BeforeDelay_Stays`, `Restore_IsTheReverse` (`At(..., 0, true)` at the icon, `At(..., 420, true)` at the dot's spot, scale 1), `Outline_FillsEveryCell`, `Gate_AllowsFourAtOnce` (4 TryStart true, 5th false, after End true).
- [ ] **Step 2: Run, expect FAIL** — `dotnet test tests/NothingShell.Core.Tests -nologo -v q --filter DotDrainTests` → compile errors.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run, expect PASS**, then `dotnet test NotWin.sln -nologo -v q`.
- [ ] **Step 5: Commit** `feat: dot drain maths (dots from a picture, motion, cap)`.

### Task 2: Preview rules (Core)

**Files:** Create `src/NothingShell.Core/Dock/PreviewRules.cs`; Test `tests/NothingShell.Core.Tests/Dock/PreviewRulesTests.cs`

**Interfaces — Produces:**
- `static (double W, double H) PreviewRules.Fit(double sourceW, double sourceH)` — inside 220 × 140, aspect kept; 0-size source → (220, 140).
- `sealed class PreviewHover` — `void Pointer(string? itemId, bool overCard, TimeSpan now)`; `bool Update(TimeSpan now)` (true when `OpenFor` changed); `string? OpenFor { get; }`; `TimeSpan? NextCheckAt { get; }`. Open after 400 ms on one item; close 300 ms after neither item nor card; item → card keeps it; item → other item switches at once once open; `Close()` for clicks.
- [ ] **Step 1: Tests:** `Fit_KeepsAspect` (1920×1080 → 220×123.75; 800×1200 → 93.3×140), `Hover_OpensAfter400`, `Hover_SweepDoesNotOpen` (item for 300 ms then off), `Hover_ItemToCard_StaysOpen`, `Hover_ClosesAfter300`, `Hover_ItemToItem_Switches`, `Close_ClosesNow`.
- [ ] **Step 2–4:** fail, implement, pass, whole suite.
- [ ] **Step 5: Commit** `feat: dock preview sizing and hover timing`.

### Task 3: Native pieces

**Files:** Create `src/NothingShell.Native/MinimizeHook.cs`, `DwmThumbnail.cs`; Modify `ScreenCaptureNative.cs`; Test `tests/NothingShell.Tests/DwmThumbnailTests.cs`

**Interfaces — Produces:**
- `sealed class MinimizeHook : IDisposable` — `MinimizeHook(Action<nint, bool> onMinimize)` (bool = true start, false end), out-of-context, skips own process, OBJID_WINDOW only, delegate kept alive (pattern of `ForegroundChangeHook`).
- `sealed class DwmThumbnail : IDisposable` — `static DwmThumbnail? Register(nint destination, nint source)`; `void Place(RectI destinationClientRect, byte opacity = 255)`; `(int W, int H) SourceSize { get; }`.
- `static byte[]? ScreenCaptureNative.CaptureGray(RectI screen, int stepPx)` — `BitBlt` the region, one grey byte per `stepPx` cell (cell centre), null on failure.
- `static RectI? WindowListNative.NormalRect(nint hwnd)` — `GetWindowPlacement.rcNormalPosition` in screen px (workspace offset applied), null on failure.
- [ ] **Step 1: Tests:** `Register_BadHandles_ReturnsNull` (`Register(0, 0)` is null), `CaptureGray_SizeMatchesGrid` (a 140×70 region at step 14 → 50 bytes, on the real screen, read-only).
- [ ] **Step 2–4:** fail, implement, pass, whole suite.
- [ ] **Step 5: Commit** `feat: minimize hook, DWM thumbnails, grey screen capture`.

### Task 4: Dot drain overlay and controller

**Files:** Create `src/NothingShell/Dock/DotDrainOverlay.cs`, `DotDrainController.cs`; Modify `DockWindow.xaml.cs` (icon rectangles), `App.xaml.cs`; Test `tests/NothingShell.Tests/DotDrainControllerTests.cs`

**Interfaces:**
- Consumes: Task 1 `DotDrain`, `DrainGate`; Task 3 natives.
- Produces:
  - `RectI? DockWindow.IconRect(string appId)` — the item's icon in screen px, null when not shown.
  - `interface IDrainEnvironment { bool Enabled { get; } RectI Monitor { get; } RectI? IconRect(nint hwnd); bool Suppressed { get; } RectI? NormalRect(nint hwnd); Picture? Snapshot(nint hwnd, RectI normal); }` with `sealed record Picture(byte[] Gray, int W, int H, double Pitch)`; the real one uses DWM thumbnail + `CaptureGray`.
  - `sealed class DotDrainController(IDrainEnvironment env, Func<IDrainOverlay> overlay)`: `void OnMinimize(nint hwnd, bool start)`; `interface IDrainOverlay { void Play(IReadOnlyList<DrainDot> dots, double tx, double ty, bool restore, Action done); void Close(); }`.
  - `DotDrainOverlay : Window, IDrainOverlay` — transparent, topmost, click-through (`WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW`), over the monitor; draws every running animation each frame from `DotDrain.At`; ends with the red blink at the icon.
- [ ] **Step 1: Tests** (fake env and overlay): `Controller_PlaysTowardTheIcon`, `Controller_ThumbnailFails_UsesFallback` (Snapshot null → Outline dots), `Controller_NearBlack_UsesFallback`, `Controller_OtherMonitor_Skips` (normal rect outside Monitor → nothing played), `Controller_Suppressed_Skips`, `Controller_Disabled_Skips`, `Controller_FifthAtOnce_Skips`, `Controller_OverlayGoneAfterLastEnds` (two plays, both `done` → `Close()` once), `Controller_Restore_PlaysReverse`.
- [ ] **Step 2–4:** fail, implement, pass, whole suite; wire in App (one `MinimizeHook`, environment from the dock and `_fullscreen` / launcher / panel cover state, `Enabled` from settings).
- [ ] **Step 5:** render offscreen: a frame at t = 0, 200, 400 ms over a sample picture.
- [ ] **Step 6: Commit** `feat: dot drain when minimizing (reverse on restore)`.

### Task 5: Window preview card

**Files:** Create `src/NothingShell/Dock/WindowPreviewCard.cs`; Modify `DockWindow.xaml(.cs)` (item hover events, tooltip only when not running), `DockController.cs`; Test `tests/NothingShell.Tests/WindowPreviewCardTests.cs`

**Interfaces:**
- Consumes: Task 2 `PreviewRules`, `PreviewHover`; Task 3 `DwmThumbnail`.
- Produces: `event Action<DockItemViewModel?, FrameworkElement?> DockWindow.ItemHover` (null on leave); `WindowPreviewCard : Window` with `void Show(IReadOnlyList<AppWindow> windows, RectI itemRect)`, `void Refresh(IReadOnlyList<AppWindow> windows)` (removes gone windows; closes when empty), `event Action<nint> Activate`, `event Action<nint> CloseWindow`; `bool IsOver(PointI p)`.
- Behaviour: built like `DockPopup` (topmost, owned by the dock, keeps the dock up while open); one cell per window: thumbnail area `PreviewRules.Fit` + title (ellipsis) + ✕ on hover; DWM thumbnails placed in the card's client rectangles after layout; clicks call `WindowListNative.Activate` / `RequestClose`; any dock click closes the card first.
- [ ] **Step 1: Tests (STA):** `Card_OneCellPerWindow_UpToSixVisible`, `Card_WindowGone_RemovesPreview` (Refresh without one window → one cell fewer; Refresh with none → closed), `Card_CellShowsTheTitle` (the cell's text is the window title, trimmed with an ellipsis).
- [ ] **Step 2–4:** fail, implement, pass, whole suite; wire hover through `PreviewHover` in `DockController` with the setting.
- [ ] **Step 5: Commit** `feat: dock window previews on hover`.

### Task 6: Settings, hands-on, docs

**Files:** Modify `ShellSettings.cs` (`DotDrain` = true, `WindowPreviews` = true), `SettingsWindow.cs` (two `ToggleRow`s + the hint label), `App.xaml.cs`, `build/verify/plan1-handson.ps1` (checks **DD.1** minimize drains into the icon with a red blink; **DD.2** restore reverses; **DD.3** a DRM / game window gets grey dots; **DD.4** Win+M animates at most 4; **DD.5** hover previews, click switches, ✕ closes; **DD.6** both switches off → plain behaviour), `README.md`.
- [ ] **Step 1: Test** `Settings_DotDrainAndPreviews_DefaultOn` (Core: `new ShellSettings()` both true; a file without them loads both true).
- [ ] **Step 2–4:** fail, implement, pass, whole suite.
- [ ] **Step 5: Commit** `feat: settings for dot drain and previews; docs`. Ask before reinstalling or pushing.

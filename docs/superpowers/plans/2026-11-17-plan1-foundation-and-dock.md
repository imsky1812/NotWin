# Plan 1 — Foundation, Spikes and Dock — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A signed, installed Nothing Shell that starts with Windows, hides the Windows taskbar (and always gives it back if the app dies), and shows the Nothing-style dock on bottom-edge hover. It also produces the phase-0 spike findings that later plans depend on.

**Architecture:** One WPF process (`NothingShell`) plus a tiny Native-AOT watchdog (`NothingShell.Watchdog`) that restores the taskbar if the main process exits for any reason. Pure decision logic (reveal timing, grouping, fullscreen rule, settings) lives in `NothingShell.Core` and is unit-tested. Win32 interop lives in `NothingShell.Native`, shared by app and watchdog. Spikes are throwaway projects under `spikes/`, outside the solution.

**Tech Stack:** .NET 10 SDK (10.0.404 via winget `Microsoft.DotNet.SDK.10`), WPF, C# 14, CommunityToolkit.Mvvm, Microsoft.Windows.CsWin32 (P/Invoke generator), xUnit, Windows SDK (signtool, makeappx), PowerShell 7 build scripts.

**Spec:** `docs/superpowers/specs/2026-11-17-nothing-shell-design.md` (read §2, §3, §5, §6.1, §6.7, §7, §8, §9 before starting).

**This plan covers spec build-order steps 0 and 1.** Later plans (written after this one ships, using its spike findings): 2 Launcher · 3 Quick settings + notifications + notch live states · 4 Notch dashboard · 5 Desktop widgets + wallpaper.

## Global Constraints

- Windows 11 only; primary display only (spec §2 non-goals). Per-monitor DPI aware; all sizes in DIPs.
- Memory: under **100 MB** total working set at idle after 10 minutes; CPU ≈ 0% idle (spec §3).
- **If Nothing Shell dies for any reason (crash, kill, logoff), the Windows taskbar must be usable again within a few seconds** (spec §2 anti-goals, §7).
- One-time admin install under `C:\Program Files\NothingShell\`, signed, `uiAccess=true`; after install, starts at logon with **no prompts** (spec §3).
- Palette only: card `#1A1A1A`, tile `#161616`/`#202020`, glass `rgba(16,16,16,0.90)` + blur, panel line `rgba(255,255,255,0.10)`, text `#F2F2F2`, dim `#8A8A8A`, red `#E8231F`. No other accent colors (spec §5).
- Motion 160–320 ms, `cubic-bezier(.2,.8,.2,1)`, no bounce (spec §5).
- Fonts bundled, never loaded from the network at runtime: Space Grotesk, Space Mono, Geist, Geist Mono, DM Serif Display (all OFL).
- Do not copy code from `jaxparrow07/nothing-kde-widgets` (GPL-3.0).
- Data folder: `%AppData%\NothingShell\` (`settings.json`, `taskbar-restore.json`, `log.txt`).
- Every installer download or admin action: tell the user what will be installed (name, source, size) and wait for a yes. Fenn is running on this PC; ask the user to quit it before testing anything that hides the taskbar.
- Commits go to `origin` = `https://github.com/imsky1812/NotWin.git`. Work on branch `plan1-foundation`; ask the user before pushing.

## Review Focus

1. **Explorer restarts while Nothing Shell runs**: the taskbar reappears unless we re-hide it. Expect it re-hidden within 2 s and the dock still working. *(Task 8 manual check 4.)*
2. **Restarting after a crash**: `taskbar-restore.json` already exists and auto-hide is currently *our* value. The original state in the file must win, otherwise the user's real preference is lost forever. *(Task 6 test `ResolveOriginal_PrefersSavedStateFromCrashedSession`.)*
3. **Resolution, scale or DPI change, or a docked/undocked laptop**: the edge trigger and dock position must follow the new primary-monitor bounds. *(Task 10 test `IsAtBottomEdge_UsesCurrentMonitorBounds`; Task 11 manual check 5.)*
4. **Odd windows**: UWP apps (`ApplicationFrameHost`), untitled or cloaked windows, tool windows and apps with many windows. Expect one dock item per app, no ghosts, and a window list when there are several. *(Task 12 tests `Build_GroupsWindowsByAppId`, `Build_SkipsUntitledWindows`; Task 13 manual check 3.)*
5. **Two shells at once**: Fenn running, or a second Nothing Shell launched. Expect a clear message and no taskbar fight, and never two docks. *(Task 16 tests `FindConflict_DetectsFenn`, manual check 2.)*

---

## File Structure

```
NotWin.sln
Directory.Build.props                      net10.0, Nullable, LangVersion, warnings-as-errors
build/install.ps1                          admin: cert, sign, copy to Program Files, Run key
build/uninstall.ps1                        restore taskbar, remove Run key + files
src/NothingShell.Core/                     pure logic (no Windows APIs) — unit tested
  Geometry.cs                              RectI, PointI
  ShellSettings.cs, SettingsStore.cs
  TaskbarRestoreState.cs, TaskbarSession.cs
  DockRevealMachine.cs, EdgeGeometry.cs
  FullscreenRule.cs
  DockModel.cs                             AppWindow, PinnedApp, DockItem, DockItemsBuilder, DockClickAction
  ConflictDetector.cs
src/NothingShell.Native/                   CsWin32 interop, AOT-safe, no WPF
  NativeMethods.txt, TaskbarNative.cs, AppBarNative.cs
src/NothingShell.Watchdog/Program.cs       Native AOT WinExe
src/NothingShell/                          WPF app
  app.manifest, app.uiaccess.manifest
  App.xaml(.cs), Log.cs, SingleInstance.cs
  Theme/Tokens.xaml, Theme/Fonts/*.ttf
  Shell/TaskbarController.cs, Shell/WatchdogLauncher.cs, Shell/FullscreenMonitor.cs, Shell/MouseEdgeHook.cs
  Dock/DockWindow.xaml(.cs), Dock/DockViewModel.cs, Dock/WindowTracker.cs, Dock/PinnedAppsImporter.cs,
  Dock/AppIconCache.cs, Dock/TrayStatusService.cs, Dock/OverflowOpener.cs, Dock/DockAppBar.cs
  Startup/StartupRegistration.cs, Startup/ConflictGate.cs
tests/NothingShell.Core.Tests/             xUnit
spikes/{KeySpike,OverflowSpike,DesktopLayerSpike,NotifySpike}/   throwaway, not in sln
docs/superpowers/spikes/2026-11-17-phase0-findings.md
```

---

### Task 1: Toolchain and solution scaffold

**Files:** Create `NotWin.sln`, `Directory.Build.props`, the four `src/*` project files, `tests/NothingShell.Core.Tests/NothingShell.Core.Tests.csproj`, `tests/NothingShell.Core.Tests/SmokeTests.cs`.

**Interfaces:** Produces: project names and target frameworks: Core `net10.0`; Native `net10.0-windows` with `<IsAotCompatible>true</IsAotCompatible>`; Watchdog `net10.0-windows`, `<OutputType>WinExe</OutputType>`, `<PublishAot>true</PublishAot>`; app `net10.0-windows10.0.22621.0`, `<UseWPF>true</UseWPF>`. References: Native → Core; Watchdog → Native; app → Core + Native; tests → Core.

- [ ] **Step 1:** Ask the user before installing anything, then run `winget install --id Microsoft.DotNet.SDK.10 -e` and `winget install --id Microsoft.WindowsSDK.10.0.26100 -e` (signtool + makeappx).
- [ ] **Step 2:** Create the solution and projects with the layout above. `Directory.Build.props`: `<Nullable>enable</Nullable>`, `<LangVersion>latest</LangVersion>`, `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`, `<ImplicitUsings>enable</ImplicitUsings>`. App csproj: `<ConcurrentGarbageCollection>false</ConcurrentGarbageCollection>`, `<TieredPGO>true</TieredPGO>`.
- [ ] **Step 3:** Write `SmokeTests.CoreAssemblyLoads` asserting `typeof(NothingShell.Core.RectI).Assembly` is not null (add an empty `public readonly record struct RectI(int Left, int Top, int Right, int Bottom);` in `Geometry.cs`).
- [ ] **Step 4:** Run `dotnet build NotWin.sln -c Debug` and then `dotnet test`. Expected: build succeeds with 0 warnings, 1 test passes.
- [ ] **Step 5:** `git checkout -b plan1-foundation`, then commit with "chore: scaffold solution".

### Task 2: Signed uiAccess install pipeline

**Files:** Create `src/NothingShell/app.manifest`, `src/NothingShell/app.uiaccess.manifest`, `src/NothingShell/Log.cs`, `build/install.ps1`, `build/uninstall.ps1`. Modify the app csproj.

**Interfaces:**
- Produces `Log.Info(string message)` and `Log.Error(string message, Exception? ex = null)`, which append to `%AppData%\NothingShell\log.txt`. The file rolls over at 1 MB to `log.1.txt`.
- Produces the MSBuild property `UiAccess`. When it's `true`, the build uses `app.uiaccess.manifest`; otherwise it uses `app.manifest`.

Both manifests use `requestedExecutionLevel level="asInvoker"`, PerMonitorV2 DPI awareness and Windows 10/11 `supportedOS`. Only `app.uiaccess.manifest` sets `uiAccess="true"`. Debug builds must run from `bin/` without signing.

- [ ] **Step 1:** In `App.OnStartup`, log `"startup uiAccess=<0|1> pid=<pid>"`. Read the value with `GetTokenInformation(TokenUIAccess)` on the process token (CsWin32: `GetTokenInformation`, `OpenProcessToken`). For now, show a 200×80 window with the text "Nothing Shell"; Task 11 replaces it.
- [ ] **Step 2:** Write `build/install.ps1`, which must be run as admin and does the following in order:
  1. Create the code-signing cert `CN=NothingShell Dev` (`New-SelfSignedCertificate -Type CodeSigningCert -CertStoreLocation Cert:\CurrentUser\My`), or reuse it if it already exists.
  2. Import it into `Cert:\LocalMachine\Root` and `Cert:\LocalMachine\TrustedPublisher`.
  3. Run `dotnet publish src/NothingShell -c Release -p:UiAccess=true -o out/app`, then `dotnet publish src/NothingShell.Watchdog -c Release -o out/app`.
  4. Stop any running `NothingShell` process.
  5. Run `signtool sign /fd SHA256 /sha1 <thumb> /tr http://timestamp.digicert.com /td SHA256` on every `.exe` and `.dll` in `out/app`.
  6. Mirror `out/app` to `C:\Program Files\NothingShell\` with robocopy `/MIR`.

  Add a `-NoTimestamp` switch for offline runs.
- [ ] **Step 3:** Write `build/uninstall.ps1`, which must be run as admin and does the following:
  1. Stop `NothingShell` and the watchdog.
  2. Run `NothingShell.Watchdog.exe --restore-now --state "$env:APPDATA\NothingShell"` (from Task 7). Skip this step if the file is missing.
  3. Remove the HKCU Run value `NothingShell`.
  4. Delete `C:\Program Files\NothingShell\`.
  5. Leave the cert and the data folder in place.
- [ ] **Step 4: Verify.** Run `build/install.ps1` in an elevated terminal (ask the user to approve the UAC prompt), then start `C:\Program Files\NothingShell\NothingShell.exe`. Expected: the window appears and `log.txt` contains `uiAccess=1`. Then run the Debug build from `bin/`. Expected: it launches and logs `uiAccess=0`.
- [ ] **Step 5:** Commit with "build: signed uiAccess install pipeline".

### Task 3: Spike: lone Win key and tray overflow (throwaway)

**Files:** Create `spikes/KeySpike/`, `spikes/OverflowSpike/` (console or WPF, not in the sln), and start `docs/superpowers/spikes/2026-11-17-phase0-findings.md` (one section per spike: method tried, result, pass/fail, numbers).

- [ ] **Step 1, KeySpike:** Install a `WH_KEYBOARD_LL` hook that uses the **mask-key technique**:
  - Let Win keydown pass through.
  - On a Win keyup with no other key in between, inject `VK 0xE8` down and up *before* letting the keyup pass. This stops the Start menu from opening. Then print `OPEN LAUNCHER`.
  - On Win+A or Win+N, swallow the letter, print `OPEN PANEL`, and mask the following Win keyup the same way.
  - Every other key and combination passes through untouched.

  Pass criteria:
  - (a) A lone Win press never opens Start.
  - (b) Win+E, Win+D, Win+L, Win+Shift+S, Win+Tab and Win+arrows all behave normally.
  - (c) With the spike installed via the Task 2 pipeline (signed, Program Files, uiAccess), all of the above holds while Task Manager is focused.
- [ ] **Step 2, OverflowSpike:** Hide the taskbar with `ShowWindow(Shell_TrayWnd, SW_HIDE)`, then try each strategy in order and record which ones open Windows' own tray overflow flyout with working icons and right-click menus:
  1. UI Automation: find the tray "Show hidden icons" chevron under `Shell_TrayWnd` and call `Invoke`.
  2. Show the taskbar, invoke the chevron, then hide the taskbar again once the overflow window (class `TopLevelWindowForOverflowXamlIsland`) closes.
  3. Send `Win+B`, then `Enter`.

  Pass criteria: at least one strategy works with no visible taskbar flash longer than 300 ms.
- [ ] **Step 3:** Write both results into the findings doc and commit with "spike: win key + overflow findings".

### Task 4: Spike: clickable widget window on the desktop layer (throwaway)

**Files:** `spikes/DesktopLayerSpike/` and the findings doc.

- [ ] **Step 1:** Build a 300×300 WPF window with a button that counts clicks. Try the following in order:
  1. **Option A:** send `0x052C` to `Progman`, then parent the window to the desktop host **above** `SHELLDLL_DefView`. Check the Windows 11 26xxx desktop layout, where the WorkerW/Progman tree differs from older builds.
  2. **Option B:** use a top-level `WS_EX_TOOLWINDOW` window kept at `HWND_BOTTOM` by handling `WM_WINDOWPOSCHANGING`, and re-show it after Win+D. Detect Win+D via `EVENT_SYSTEM_FOREGROUND` landing on the desktop.

  Pass criteria:
  - (a) Clicks register.
  - (b) It always sits under normal app windows.
  - (c) It stays visible after Win+D.
  - (d) It survives `Stop-Process -Name explorer` (re-attach within 2 s).
  - (e) It doesn't show in Alt-Tab or the dock's window list.
- [ ] **Step 2:** Record which option passes in the findings doc and commit with "spike: desktop layer findings".

### Task 5: Spike: notification listener with package identity (throwaway)

**Files:** `spikes/NotifySpike/` (exe plus `AppxManifest.xml`) and the findings doc.

- [ ] **Step 1:** Create a sparse package manifest that:
  - names the publisher `CN=NothingShell Dev`
  - uses `<Package ... IgnorableNamespaces="uap uap10 rescap">`
  - sets `<uap10:AllowExternalContent>true</uap10:AllowExternalContent>`
  - declares the `userNotificationListener` capability
  - points the Application `Executable` at the spike exe

  Pack it with `makeappx pack /nv` and sign it with the Task 2 cert. Register it with `Add-AppxPackage -Path NotifySpike.msix -ExternalLocation <exe dir>`.

  Fallback: Developer Mode is **on** on this PC, so `Add-AppxPackage -Register AppxManifest.xml -ExternalLocation <dir>` is also allowed.

  Embed the sparse-package identity (`<msix ... publisher= packageName= applicationId=>`) in the exe's manifest.
- [ ] **Step 2:** In the spike, call `UserNotificationListener.Current.RequestAccessAsync()`, list `GetNotificationsAsync(NotificationKinds.Toast)`, and print the app name and text on `NotificationChanged`. Ask the user to send themselves a Discord message or any toast. Then turn **off "Show notification banners"** for that app in Windows Settings and repeat.

  Pass criteria:
  - (a) The text prints.
  - (b) It still prints with banners off.
  - (c) It works from the signed `uiAccess` exe under Program Files.

  **If (a) fails, stop and tell the user.** Plan 3 depends on it.
- [ ] **Step 3:** Record the results and commit with "spike: notification listener findings".

### Task 6: Settings and taskbar-restore state (Core)

**Files:** Create `src/NothingShell.Core/ShellSettings.cs`, `SettingsStore.cs`, `TaskbarRestoreState.cs`, `TaskbarSession.cs` and `DockModel.cs` (only `PinnedApp` for now; Task 12 adds the rest), plus the tests `SettingsStoreTests.cs` and `TaskbarSessionTests.cs`.

**Interfaces (Produces):**
- `public sealed record ShellSettings` with init properties: `bool KeepDockVisible = false`, `bool StartWithWindows = true`, `string WidgetScale = "M"`, `IReadOnlyList<PinnedApp> PinnedApps = []`, `bool PinsImported = false`.
- `public sealed class SettingsStore(string directory)` with `ShellSettings Load()` and `void Save(ShellSettings settings)`:
  - uses `settings.json` and System.Text.Json with camelCase
  - writes atomically to a temp file in the same folder, then replaces
  - if the file is missing, `Load` returns the defaults
  - if the file is corrupt, `Load` renames it to `settings.json.bad` and returns the defaults
- `public sealed record TaskbarRestoreState(bool WasAutoHide)`.
- `public sealed class TaskbarRestoreStore(string directory)` with `bool TryLoad(out TaskbarRestoreState state)`, `void Save(TaskbarRestoreState state)` and `void Delete()`, using the file `taskbar-restore.json`.
- `public static class TaskbarSession` with `static TaskbarRestoreState ResolveOriginal(TaskbarRestoreState? leftoverFromLastRun, bool currentAutoHide)`.

- [ ] **Step 1:** Write the tests:
  - `Load_MissingFile_ReturnsDefaults`
  - `SaveThenLoad_RoundTrips` (with KeepDockVisible=true and one pinned app)
  - `Load_CorruptFile_ReturnsDefaultsAndKeepsBadCopy`
  - `ResolveOriginal_NoLeftover_UsesCurrent`: `(null, false)` → `WasAutoHide == false`
  - `ResolveOriginal_PrefersSavedStateFromCrashedSession`: `(new(false), true)` → `WasAutoHide == false`
  - `RestoreStore_DeleteThenTryLoad_ReturnsFalse`
- [ ] **Step 2:** Run `dotnet test --filter "SettingsStoreTests|TaskbarSessionTests"`. Expected: FAIL (the types don't exist yet).
- [ ] **Step 3:** Implement, including `public sealed record PinnedApp(string AppId, string DisplayName, string LaunchPath)` in `DockModel.cs`.
- [ ] **Step 4:** Run the tests. Expected: all pass. Commit with "feat(core): settings and taskbar restore state".

### Task 7: Native taskbar control and watchdog

**Files:** Create `src/NothingShell.Native/NativeMethods.txt`, `TaskbarNative.cs` and `src/NothingShell.Watchdog/Program.cs`.

**Interfaces:**
- Consumes: `TaskbarRestoreState` and `TaskbarRestoreStore` (Task 6).
- Produces `public static class TaskbarNative` with these members:
  - `bool GetAutoHide()`: reads `ABM_GETSTATE`.
  - `void SetAutoHide(bool on)`: sets `ABM_SETSTATE` with `ABS_AUTOHIDE`.
  - `void HideAll()`: runs `ShowWindow(SW_HIDE)` on `Shell_TrayWnd` and every `Shell_SecondaryTrayWnd`.
  - `void ShowAll()`
  - `void Restore(TaskbarRestoreState s)`: runs `ShowAll()`, then `SetAutoHide(s.WasAutoHide)`.
- The watchdog CLI has two modes:
  - `--parent <pid> --state <dir>`: waits for that process to exit, then restores and deletes the state file.
  - `--restore-now --state <dir>`: restores immediately, then exits with code 0.
  - Invalid arguments mean exit code 2. The watchdog has no window.

- [ ] **Step 1:** Implement `TaskbarNative` with CsWin32: `SHAppBarMessage`, `FindWindow`, `FindWindowEx`, `ShowWindow`.
- [ ] **Step 2:** Implement the watchdog. Use `Process.GetProcessById(pid).WaitForExit()`. If the process is already gone, restore right away. In both cases, restore only when `TryLoad` returns true. Publish with Native AOT.
- [ ] **Step 3, manual verification:** `dotnet publish src/NothingShell.Watchdog -c Release`, then check `out` with `Get-Item *.exe`. Expected: a single exe under 5 MB. Also confirm its working set while it waits is under 5 MB (`(Get-Process NothingShell.Watchdog).WorkingSet64`).
- [ ] **Step 4:** Commit with "feat(native): taskbar control + watchdog".

### Task 8: TaskbarController in the app

**Files:** Create `src/NothingShell/Shell/TaskbarController.cs` and `Shell/WatchdogLauncher.cs`, and modify `App.xaml.cs`.

**Interfaces:**
- Consumes: `TaskbarNative`, `TaskbarSession.ResolveOriginal` and `TaskbarRestoreStore` (Tasks 6–7).
- Produces `public sealed class TaskbarController : IDisposable` with:
  - `void Start()`. It runs these steps in order:
    1. Resolve the original state: `ResolveOriginal(leftover, GetAutoHide())`.
    2. Save it.
    3. `SetAutoHide(true)`.
    4. `HideAll()`.
    5. Start the watchdog.
    6. Register for the `TaskbarCreated` message via `RegisterWindowMessage("TaskbarCreated")`, on an `HwndSource` message-only window. When it arrives, re-run `SetAutoHide(true)` and `HideAll()`.
  - `void Dispose()`: restores the taskbar, deletes the state file, and stops the watchdog.
- Produces `WatchdogLauncher.Start(int parentPid, string stateDir) : Process`.
- In `App`: call `Dispose` on `Exit`, on `SessionEnding`, and inside the `AppDomain.UnhandledException` / `DispatcherUnhandledException` handlers (log, restore, then let the crash continue).

- [ ] **Step 1:** Implement, then install with `build/install.ps1`. **Ask the user to quit Fenn first.**
- [ ] **Step 2, manual verification.** Each check must pass:
  1. Start the app. The taskbar disappears, and a maximized Notepad fills the whole screen height.
  2. Quit through the window's close button. The taskbar returns with the user's original auto-hide setting.
  3. `Stop-Process -Name NothingShell -Force`. The taskbar returns within 3 s.
  4. While the app is running, `Stop-Process -Name explorer -Force` (Explorer restarts). The taskbar is hidden again within 2 s.
  5. Kill the app twice in a row, without the watchdog getting a chance to run (kill the watchdog first, then the app). Start the app and quit it normally. The user's *original* auto-hide setting comes back (this is Review Focus 2).
- [ ] **Step 3:** Commit with "feat: hide and always-restore the Windows taskbar".

### Task 9: Fullscreen rule and monitor

**Files:** Create `src/NothingShell.Core/FullscreenRule.cs` and its tests, and `src/NothingShell/Shell/FullscreenMonitor.cs`.

**Interfaces:**
- Produces `public readonly record struct FullscreenProbe(RectI Foreground, RectI Monitor, string ClassName, bool IsMaximized, bool QunsSaysFullscreen)`. `Foreground` is the DWM extended frame bounds (`DWMWA_EXTENDED_FRAME_BOUNDS`), and `IsMaximized` comes from `IsZoomed`.
- Produces `public static class FullscreenRule` with `bool IsFullscreen(FullscreenProbe p)`. It returns true when `QunsSaysFullscreen`, or when `Foreground == Monitor`, `!IsMaximized` and `ClassName` is not one of `Progman`, `WorkerW` or `Shell_TrayWnd`. With the taskbar auto-hidden, maximized windows also cover the whole monitor, so maximized ≠ full-screen.
- Produces `FullscreenMonitor` with an `event Action<bool> Changed` and `bool IsFullscreen`. It updates on `EVENT_SYSTEM_FOREGROUND` and on `EVENT_OBJECT_LOCATIONCHANGE` for the foreground window only. `QunsSaysFullscreen` means `SHQueryUserNotificationState` returns `QUNS_RUNNING_D3D_FULL_SCREEN`, `QUNS_BUSY` or `QUNS_PRESENTATION_MODE`.

- [ ] **Step 1:** Write the tests:
  - `BorderlessGameCoveringMonitor_IsFullscreen`
  - `MaximizedWindowCoveringMonitor_IsNotFullscreen`: foreground `{0,0,1920,1080}` against monitor `{0,0,1920,1080}`, `IsMaximized = true`, QUNS false → false
  - `DesktopCoveringMonitor_IsNot` (class `Progman`)
  - `QunsD3D_IsFullscreenEvenIfRectDiffers`
- [ ] **Step 2:** Run the tests and watch them fail. Implement. Run them again and watch them pass. Commit with "feat: fullscreen detection".

### Task 10: Dock reveal state machine and edge geometry (Core)

**Files:** Create `src/NothingShell.Core/DockRevealMachine.cs`, `EdgeGeometry.cs` and their tests.

**Interfaces:**
- Produces `public readonly record struct DockInputs(bool CursorAtEdge, bool CursorOverDock, bool PanelOpen, bool KeepVisible, bool Fullscreen)`.
- Produces `public sealed class DockRevealMachine(TimeSpan revealDelay, TimeSpan hideDelay)` with:
  - `static DockRevealMachine CreateDefault()`: 150 ms reveal delay, 500 ms hide delay.
  - `bool Visible { get; }`
  - `void Update(DockInputs input, TimeSpan now)`
  - `TimeSpan? NextDeadline { get; }`: the time at which the caller should call `Update` again; `null` if nothing is pending.
- Produces `public static class EdgeGeometry` with `bool IsAtBottomEdge(PointI cursor, RectI monitor)`. This returns true when `cursor.Y >= monitor.Bottom - 1` and X is inside the monitor. Also `RectI DockBounds(RectI monitor, double width, double height = 60, double bottomGap = 12)`: centered, bottom edge 12 DIP above `monitor.Bottom`. Define `public readonly record struct PointI(int X, int Y)` in `Geometry.cs`.

Rules, in priority order:
1. `Fullscreen && !KeepVisible` → hidden immediately, and edge input is ignored.
2. `KeepVisible` → visible.
3. Edge held for ≥150 ms → visible. Leaving the edge before 150 ms cancels the reveal.
4. Visible, and the cursor is neither over the dock nor at the edge, and no panel is open, for ≥500 ms → hidden. Re-entering the dock cancels the hide.
5. `PanelOpen` keeps the dock visible.

- [ ] **Step 1:** Write the tests, using explicit `now` values:
  - `EdgeFor149ms_StaysHidden`
  - `EdgeFor150ms_Reveals`
  - `LeaveDock_HidesAfter500ms`
  - `ReenterBefore500ms_CancelsHide`
  - `PanelOpen_KeepsVisible`
  - `Fullscreen_HidesAndIgnoresEdge`
  - `KeepVisible_BeatsFullscreen`
  - `IsAtBottomEdge_UsesCurrentMonitorBounds`: the same point is at the edge for `{0,0,1920,1080}` but not for `{0,0,2560,1440}`
  - `DockBounds_CentersAboveBottomGap`
- [ ] **Step 2:** Run the tests and watch them fail. Implement. Run them again and watch them pass. Commit with "feat(core): dock reveal state machine".

### Task 11: Theme, fonts and the dock window shell

**Files:** Create `src/NothingShell/Theme/Tokens.xaml`, `Theme/Fonts/` (download the 5 OFL font families from `github.com/google/fonts`, `ofl/*`, after asking the user), `Dock/DockWindow.xaml(.cs)`, `Dock/DockViewModel.cs` and `Shell/MouseEdgeHook.cs`. Modify `App.xaml(.cs)` to remove the temporary window.

**Interfaces:**
- Consumes: `DockRevealMachine`, `EdgeGeometry`, `FullscreenMonitor` and `SettingsStore`.
- Produces these resource keys in `Tokens.xaml`:
  - Colors: `Brush.Card`, `Brush.Tile`, `Brush.TileHover`, `Brush.Glass`, `Brush.Line`, `Brush.Text`, `Brush.Dim`, `Brush.Red`.
  - Radii: `Radius.Dock`=30, `Radius.Panel`=28, `Radius.Widget`=26, `Radius.Tile`=22.
  - Font families: `Font.Grotesk`, `Font.Mono`, `Font.Geist`, `Font.GeistMono`, `Font.Serif`.
  - Easing: `Ease.Standard`, a `KeySpline` equal to `0.2,0.8 0.2,1`.
- Produces `MouseEdgeHook` with an `event Action<PointI> Moved`. It's a `WH_MOUSE_LL` hook; when the move rate exceeds 60 Hz it throttles and keeps only the latest point. It's paused while fullscreen.
- Produces `DockViewModel` with `bool IsVisible`, plus `Items` (filled in Task 12) and `Tray` (filled in Task 14).
- Produces `DockWindow`, which has this styling:
  - A borderless, transparent, topmost, no-activate (`WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW`) window.
  - A 60 DIP tall pill with corner radius `Radius.Dock`.
  - Background `Brush.Glass` with a DWM acrylic/blur backdrop.
  - A 1 px `Brush.Line` border.
  - Showing slides it up from +90 DIP and fades it in over 240 ms with `Ease.Standard`. Hiding runs the same animation in reverse.

- [ ] **Step 1:** Implement. Drive `DockRevealMachine.Update` from the hook events, dock mouse enter/leave and `FullscreenMonitor.Changed`. Use a `DispatcherTimer` set to `NextDeadline` only while a deadline is pending; there's no idle timer otherwise.
- [ ] **Step 2, manual verification.** Install, then check each of these:
  1. Touching the bottom edge shows the dock after a short pause.
  2. Moving the cursor away hides it about half a second later.
  3. Flicking the cursor quickly past the edge doesn't show it.
  4. A full-screen YouTube video or game: the edge does nothing.
  5. Changing display scale from 100% to 125% (Settings › Display): the dock stays centered, 12 DIP above the bottom, and the edge still triggers it.
  6. Task Manager › Details shows no CPU use while idle.
- [ ] **Step 3:** Commit with "feat: dock window with edge reveal".

### Task 12: Running windows → dock items

**Files:** Modify `src/NothingShell.Core/DockModel.cs` (from Task 6) with tests `DockItemsBuilderTests.cs`; create `src/NothingShell/Dock/WindowTracker.cs` and `Dock/AppIconCache.cs`. Modify `DockWindow.xaml` and `DockViewModel.cs`.

**Interfaces:**
- Produces `public sealed record AppWindow(nint Handle, string AppId, string Title, string ExePath, bool IsForeground)`.
- Produces `public sealed record DockItem(string AppId, string DisplayName, string? LaunchPath, bool Pinned, IReadOnlyList<AppWindow> Windows)`, with `bool Running => Windows.Count > 0` and `bool Focused => Windows.Any(w => w.IsForeground)`.
- Produces `public enum DockClickAction { Launch, Activate, Minimize, ShowWindowList }`.
- Produces `public static class DockItemsBuilder`:
  - `IReadOnlyList<DockItem> Build(IReadOnlyList<PinnedApp> pinned, IReadOnlyList<AppWindow> windows)`. Pinned items come first, in pinned order, with their windows attached. Running unpinned apps follow in order of first appearance. Windows with an empty `Title` are skipped.
  - `DockClickAction DecideClick(DockItem item)`. Not running → `Launch`. More than one window → `ShowWindowList`. One window that's focused → `Minimize`. Otherwise → `Activate`.
- Produces `WindowTracker` with `IReadOnlyList<AppWindow> Current` and `event Action Changed`. It builds the list from `EnumWindows`, keeping windows that are visible, unowned, not `WS_EX_TOOLWINDOW` and not DWM-cloaked. It excludes our own process. `AppId` is the window's `PKEY_AppUserModel_ID` (via `SHGetPropertyStoreForWindow`); if that's empty, it uses the lower-case exe path. For `ApplicationFrameHost` windows, `AppId` comes from the hosted `CoreWindow` child's process. It's updated through `SetWinEventHook` on create, destroy, show, hide, foreground and name-change events, debounced to 100 ms.
- Produces `AppIconCache.Get(string exePathOrAumid, int sizePx) : ImageSource`. Icons are greyscale (luminance only, alpha kept) and cached by key.

- [ ] **Step 1:** Write the tests:
  - `Build_GroupsWindowsByAppId`: 2 windows with the same `AppId` → 1 item with 2 windows.
  - `Build_PinnedFirstInPinnedOrder`
  - `Build_RunningUnpinnedAfterPinned_InFirstSeenOrder`
  - `Build_SkipsUntitledWindows`
  - `DecideClick_NotRunning_Launch`
  - `DecideClick_SingleFocused_Minimize`
  - `DecideClick_SingleUnfocused_Activate`
  - `DecideClick_Multiple_ShowWindowList`
- [ ] **Step 2:** Run the tests and watch them fail. Implement `DockModel.cs`. Run them again and watch them pass.
- [ ] **Step 3:** Implement `WindowTracker`, `AppIconCache` and the dock item template:
  - Each item is a 44×44 hit area with a 22 px greyscale icon.
  - A running app shows a 4 px `#BBBBBB` dot under its icon.
  - A focused app gets a 4 px `Brush.Red` dot and a `#1FFFFFFF` background.
  - Clicking uses `DecideClick`. To activate, call `SetForegroundWindow` (allowed because of uiAccess) and restore the window if it's minimized. To minimize, call `ShowWindow(SW_MINIMIZE)`. `ShowWindowList` shows a small glass popup above the item listing the window titles; clicking a title activates that window.
  - The right-click menu has "Pin" / "Unpin" (Task 13 persists the choice) and "Close window(s)" (`WM_CLOSE`).
- [ ] **Step 4:** Commit with "feat: running apps in the dock".

### Task 13: Pinned apps (import, persist, launch)

**Files:** Create `src/NothingShell/Dock/PinnedAppsImporter.cs` and modify `DockViewModel.cs`.

**Interfaces:**
- Consumes: `SettingsStore`, `ShellSettings.PinnedApps`, `PinsImported` and `PinnedApp`.
- Produces `PinnedAppsImporter.Import(string userPinnedDir) : IReadOnlyList<PinnedApp>`. It resolves each `.lnk` in `%AppData%\Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar` with `IShellLinkW` + `IPropertyStore`:
  - `DisplayName` is the shortcut name.
  - `LaunchPath` is the `.lnk` path.
  - `AppId` is the shortcut's `System.AppUserModel.ID`; if that's missing, the lower-case target exe path.

  On this PC the folder contains `Google Chrome.lnk` and a `Tombstones` subfolder, which must be ignored.

- [ ] **Step 1:** On first run (`PinsImported == false`), import the pins, save them and set `PinsImported = true`. Launch an item with `ShellExecute` on `LaunchPath`. Pin and unpin from the context menu, then save.
- [ ] **Step 2, manual verification:**
  1. A fresh install shows Chrome pinned.
  2. Pin VS Code from its running item, restart the app, and VS Code is still pinned.
  3. Open 3 File Explorer windows and 1 Settings window (UWP). File Explorer shows one item with a 3-window list, Settings shows one item, and no item appears for `ApplicationFrameHost`.
- [ ] **Step 3:** Commit with "feat: pinned apps".

### Task 14: Tray (status icons, clock, overflow)

**Files:** Create `src/NothingShell/Dock/TrayStatusService.cs` and `Dock/OverflowOpener.cs`; modify `DockWindow.xaml`.

**Interfaces:**
- Produces `TrayStatusService` with these observable properties:
  - `string Time`: "HH:mm".
  - `string Day`: "ddd d", for example "Tue 17".
  - `bool WifiConnected`, `bool WifiEnabled`.
  - `int VolumePercent`, `bool Muted`.
  - `int BatteryPercent`, `bool Charging`.

  How each value updates:
  - The clock ticks once at the next whole minute, then every 60 s.
  - Wi-Fi uses `NetworkInformation.NetworkStatusChanged` and `GetInternetConnectionProfile().IsWlanConnectionProfile`.
  - Volume uses Core Audio `IAudioEndpointVolume` change notifications on the default render device (CsWin32 COM).
  - Battery uses `PowerManager` `RemainingChargePercentChanged` and `BatteryStatusChanged` events.
- Produces `OverflowOpener.Open()`, which uses the strategy that passed in Task 3. If none passed, Task 3 must already have raised that with the user; don't invent one.

- [ ] **Step 1:** Lay out the tray, left to right after a divider:
  - the ⌃ overflow button (16 px chevron)
  - Wi-Fi (connected / off glyph)
  - volume (level / muted glyph)
  - battery (16×9 outline with a proportional fill; the fill is `Brush.Red` when below 20% and not charging)
  - the time in `Font.Mono` 12 px, with the day in `Font.Mono` 10 px `Brush.Dim` underneath

  Use a 10 px gap between elements. The whole group is one 44 px tall hover area.
- [ ] **Step 2:** Clicking the tray (excluding ⌃) sends `Win+A` for now, which opens Windows' quick settings until Plan 3 replaces it. Clicking ⌃ calls `OverflowOpener.Open()`.
- [ ] **Step 3, manual verification:**
  1. Toggle Wi-Fi off, change volume and mute, and unplug the charger. Each glyph updates within 2 s.
  2. ⌃ opens the overflow showing Discord and Steam, and their right-click menus work.
  3. The clock rolls over at the minute.
- [ ] **Step 4:** Commit with "feat: dock tray + overflow".

### Task 15: Keep-dock-visible (reserved space), start button and interim menu

**Files:** Create `src/NothingShell/Dock/DockAppBar.cs`; modify `DockWindow.xaml(.cs)` and `DockViewModel.cs`.

**Interfaces:**
- Consumes: `ShellSettings.KeepDockVisible` and `DockRevealMachine` (`KeepVisible` input).
- Produces `DockAppBar` with `void Register(Window dock, RectI monitor)` and `void Unregister()`. It uses `SHAppBarMessage` `ABM_NEW` / `ABM_QUERYPOS` / `ABM_SETPOS` with `ABE_BOTTOM`, reserving height 60 + 12 + 12 DIP (in physical pixels at the current DPI). It handles `ABN_POSCHANGED` and re-registers after `TaskbarCreated`.
- The start button is the 3×3 dot glyph with dots 4 px wide on a 6 px pitch, and the last dot `Brush.Red`. The search button is a 22 px magnifier. Both currently send nothing on left-click: they're placeholders that Plan 2 binds to the launcher. Give them no click handler, not a TODO.
- The start button's right-click menu is the interim settings, until Plan 3's notch provides Settings:
  - "Keep taskbar visible" (checkable, saved)
  - "Start with Windows" (checkable, Task 16)
  - "Quit Nothing Shell"

- [ ] **Step 1:** Implement.
- [ ] **Step 2, manual verification:**
  1. Turn on "Keep taskbar visible". A maximized window now ends above the dock. Turn it off and the window fills the full height again.
  2. With it on, restart Explorer. The space is still reserved.
  3. "Quit" restores the Windows taskbar (it calls `TaskbarController.Dispose`).
- [ ] **Step 3:** Commit with "feat: reserved-space dock mode + interim menu".

### Task 16: Startup, single instance, Fenn guard

**Files:** Create `src/NothingShell.Core/ConflictDetector.cs` with tests, `src/NothingShell/SingleInstance.cs`, `Startup/StartupRegistration.cs` and `Startup/ConflictGate.cs`; modify `App.xaml.cs` and `build/install.ps1`.

**Interfaces:**
- Produces `public static class ConflictDetector` with `string? FindConflict(IEnumerable<string> processNames)`. The match is case-insensitive on `fenn` and returns `"Fenn"`; anything else returns `null`.
- Produces `SingleInstance.TryAcquire() : bool`, using a named mutex `Local\NothingShell.Instance`. A second instance exits silently.
- Produces `StartupRegistration.SetEnabled(bool enabled, string exePath)` and `bool IsEnabled()`, using the HKCU `Software\Microsoft\Windows\CurrentVersion\Run` value `NothingShell` = `"<exePath>"`, quoted. Use the per-user Run entry unless Task 3 or Task 5 found that `uiAccess` or package identity needs `StartupTask`; if so, follow the findings doc.
- Produces `ConflictGate.CheckOrWait() : bool`. If a conflict is found, show a small Nothing-styled dialog: "Fenn is running. Quit Fenn to use Nothing Shell's taskbar and notch." with buttons **Check again** and **Quit Nothing Shell**. No taskbar change happens until the conflict is gone.
- Startup order in `App.OnStartup`:
  1. single instance
  2. conflict gate
  3. `TaskbarController.Start`
  4. the dock
  5. apply `StartWithWindows`

  `install.ps1` gains a final step that launches the app once without elevation (`explorer.exe "C:\Program Files\NothingShell\NothingShell.exe"`) so the Run value gets written by the user process.

- [ ] **Step 1:** Write the tests `FindConflict_DetectsFenn` (`["explorer","FENN"]` → `"Fenn"`) and `FindConflict_NoneWhenAbsent`.
- [ ] **Step 2:** Run the tests and watch them fail. Implement. Run them again and watch them pass.
- [ ] **Step 3, manual verification:**
  1. Sign out and back in. The dock works and the taskbar is hidden, with no prompts.
  2. With Fenn running, start Nothing Shell. The dialog appears and the taskbar is unchanged. Quit Fenn, click Check again, and Nothing Shell takes over.
  3. Launch a second instance. Nothing visible happens.
- [ ] **Step 4:** Commit with "feat: startup, single instance, Fenn guard".

### Task 17: Budget check and hand-off

**Files:** Create `docs/superpowers/spikes/2026-11-17-phase1-measurements.md` and `README.md` (covering install, uninstall, dev run and the data folder).

- [ ] **Step 1:** After logon, leave the PC idle for 10 minutes. Record the `WorkingSet64` and `PrivateMemorySize64` of `NothingShell` and `NothingShell.Watchdog`, and their CPU % over 60 s (`Get-Counter '\Process(NothingShell*)\% Processor Time'`).

  Pass criteria: total working set under **100 MB** and CPU under 0.5% average. If it fails, profile with `dotnet-counters` and fix before closing the plan.
- [ ] **Step 2:** Run the full test suite: `dotnet test`. Expected: all pass.
- [ ] **Step 3:** Commit with "docs: phase 1 measurements + README". Ask the user before pushing `plan1-foundation` and opening a PR to `main`.

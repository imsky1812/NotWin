# Phase 0 spike findings (2026-11-17)

Environment: Windows 11 build 26300, 1920x1080, .NET 10. **Fenn** (third-party taskbar/notch app, fenn.exe) was running during all tests. The explorer-owned `Shell_TrayWnd` reported `IsWindowVisible=False` at the start of every overflow run (what hid it was not measured). Spike code: `spikes/KeySpike`, `spikes/OverflowSpike` (throwaway, not in `NotWin.sln`). Install pipeline for the uiAccess run: `spikes/install-spikes.ps1` (elevated; publishes each spike that has a `spike-install.txt` marker, signs with `CN=NothingShell Dev`, copies to `C:\Program Files\NothingShell.Spikes\<Name>\`).

Tests ran unattended on the live desktop with synthetic `SendInput`. Caveat: synthetic keys travel the same low-level hook chain as physical keys, but the shell could in principle treat them differently (e.g. Start's Win-keyup logic). Physical confirmation is in the manual lists below.

Gotcha: while the session was locked (LogonUI running) `SendInput` never reaches the user desktop, so every key test falsely fails and UIA Invoke was slow (~2 s). Both spikes now abort when LogonUI is running. Results from that period are discarded; everything below was measured on an unlocked desktop.

## Spike: lone Win key

Method: `WH_KEYBOARD_LL` hook (`KeySpike`). Win keydown passes through. On Win keyup with no other key in between: inject VK 0xE8 down+up (marked with `dwExtraInfo` so the hook ignores them) *before* the keyup is passed on, print `OPEN LAUNCHER`. Win+A / Win+N (no Shift/Ctrl/Alt): swallow the letter keydown/up, print `OPEN PANEL`, mask the Win keyup the same way. Everything else passes through. `KeySpike.exe --selftest` drives this and detects Start via a visible, non-cloaked `StartMenuExperienceHost` window or a foreground process of `StartMenuExperienceHost`/`SearchHost`.

Observed (Debug build, non-uiAccess, `--selftest`, final run):

| Check | Result |
|---|---|
| Control: lone Win tap with hook NOT installed opens Start | Start detected (detector valid) |
| (a) lone Win tap with hook: Start stays closed, `OPEN LAUNCHER` logged | PASS |
| Win+A: `OPEN PANEL (Win+A)` logged, no `OPEN LAUNCHER`, no Start window detected | PASS for that. Absence of Quick Settings was NOT measured (no detector); manual |
| Win+N: `OPEN PANEL (Win+N)` logged, no `OPEN LAUNCHER`, no Start window detected | PASS for that. Absence of the Notification Center was NOT measured; manual |
| Win+E: passes through, new Explorer window opened (closed by harness) | PASS |
| Win+D: hook logged pass-through and no `OPEN LAUNCHER`; asserted foreground class Progman/WorkerW after the 1st press (observed `explorer:Progman`) and not Progman/WorkerW after the 2nd (observed `claude:Chrome_WidgetWin_1`) | PASS (assertions added in fix round 1, re-run) |
| (b) Win+L, Win+Shift+S, Win+Tab, Win+arrows | NOT TESTED (lock / overlay / window moves; manual) |
| (c) uiAccess, signed, Program Files, Task Manager focused | NOT TESTED (needs elevated install; manual) |

Verdict: the mask-key technique satisfies (a) with synthetic input on this build. Injecting the mask via `SendInput` from inside the hook callback works; no recursion because of the `dwExtraInfo` mark.

### Manual checks pending (user)
1. Elevated PowerShell 7: `D:\NothingLauncher\spikes\install-spikes.ps1` (build\install.ps1 must have run once so the cert exists and is trusted).
2. From a normal (non-elevated) terminal: `& 'C:\Program Files\NothingShell.Spikes\KeySpike\KeySpike.exe'`.
3. Physical keyboard: tap Win alone several times (LWin and RWin), also with Task Manager focused (normal, and once "Run as administrator"). Expect `OPEN LAUNCHER` printed and Start never opens. This is pass (a) and (c).
4. Press Win+A and Win+N: expect `OPEN PANEL`, no Quick Settings/Notifications, and no Start on release.
5. Press Win+E, Win+D, Win+L, Win+Shift+S, Win+Tab, Win+Left/Right/Up/Down, Win+R, Win+V: all behave normally (b), also with Task Manager focused (c).
6. Hold Win, press and release another key, release Win: no `OPEN LAUNCHER`. Tap Win twice quickly. Report any case where Start still opens.
7. With the elevated Task Manager focused the hook must still fire (this is what uiAccess buys). Ctrl+C in the console to quit.

## Spike: tray overflow

Method (`OverflowSpike --run [--early-hide]`; `--dump` prints the UIA tree of the tray): strategies tried in order against the explorer-owned taskbar. Success = a visible, non-cloaked top-level window of class `TopLevelWindowForOverflowXamlIsland`.

Environment findings:
- **Fenn registers its own decoy `Shell_TrayWnd`** (owned by fenn.exe), so `FindWindow("Shell_TrayWnd")` can return the wrong window. The spike picks the one owned by `explorer`. Production code must do the same.
- Fenn re-hides the real taskbar about 30 ms after any `ShowWindow`. The spike therefore re-issues `ShowWindow(SW_SHOWNA)` every 3 ms on the tray and on its `Windows.UI.Composition.DesktopWindowContentBridge` child while it searches for the chevron. Without Fenn this pinning is probably unnecessary (not tested).
- The tray chevron in UIA: `ControlType.Button`, `AutomationId='SystemTrayIcon'`, `Name='Show Hidden Icons'`, class `SystemTray.NormalButton`, supports `InvokePattern`.
- With the tray hidden, UIA exposes no XAML children (the content bridge child is hidden). The tray window and its bridge child must both be shown before the chevron can be found.

Results, fix-round re-run (on the unlocked desktop; 3 runs of `--run --early-hide`, 1 run of `--run`; tray initially hidden, restored to hidden every time). Timing definitions: t=0 is the first `ShowWindow`; "visible time" = t=0 to the moment the hide call returns; "flyout latency" = Invoke start to the flyout window being seen (5 ms polling).

| Strategy | Result |
|---|---|
| 1. taskbar hidden, UIA Invoke | FAIL in every run (8/8): chevron not found (no UIA children). Observed only with Fenn running; the root cause may be Fenn rather than Windows, not tested without it |
| 2. show, find chevron (must expose InvokePattern), Invoke, hide | Worked 4/4 in the re-run (flyout survived the taskbar hide; still open 600 ms later). Early-hide runs (hide ~15 ms after Invoke starts): visible time 102, 74, 66 ms; flyout latency 27, 25, 26 ms. Non-early run: flyout 23 ms after Invoke start, hide issued after it, visible time 96 ms. All under 300 ms. Caveat: in the first fix-round attempt (before requiring InvokePattern) 3 of 4 runs found a chevron element at 44-57 ms that had no InvokePattern yet and failed; earlier unlocked runs had visible times of 116-377 ms (those figures used the older, differently reset timers and are superseded). So the find step is flaky unless the pattern check is included. |
| 3. taskbar hidden, Win+B then Enter | Flyout appeared 13-14 ms after Enter in 4/4 re-runs. During Win+B/Enter the tray window was sampled every 5 ms: `IsWindowVisible` was false in every sample (0 of 23-28 per run, 8 runs). The window rect is a separate matter. Only two numbers were recorded per run: the lowest `rect.top` seen during sampling, and the top read at run start (the full sample series was not kept). First batch of 4 runs, per run "lowest top seen / initial top read at run start": 1020 / 1078, 1078 / 1020, 1020 / 1020, 1078 / 1020. Final batch of 4 runs: 1020 / 1020 in all four. No measurement here ties 1078 or 1020 to being painted or hidden. What the data shows: `IsWindowVisible` was false in all samples; the rect values differ between runs, and the lowest top seen was 1020 in 6 of 8 runs. It does not establish whether anything was painted on screen during Win+B/Enter. Visual flash is unverified; manual check. Enter is only sent if Win+B put focus on `Shell_TrayWnd` (guard added to avoid stray Enter) |

Overall status of the overflow criterion: PENDING. Mechanically, strategies 2 and 3 open Windows' own flyout (`TopLevelWindowForOverflowXamlIsland`) with the taskbar hidden within 300 ms by `IsWindowVisible`/timing, but the criterion also needs working icons and right-click menus, and a visual check for flash. Neither was verified; see manual checks. Taskbar painting on screen was not observed visually.

Not verified: flyout contents (icons present), right-click menus, Discord/Steam icons, flyout position when the real taskbar is hidden normally, and behavior when Fenn is NOT running. Esc does not reliably close a flyout opened this way (focus never lands in it); the spike falls back to `SW_HIDE` on the flyout window, so stale flyout state is possible.

Taskbar state: restored to its initial hidden state after every run (verified: both `Shell_TrayWnd` windows `IsWindowVisible=False`, no overflow/Explorer/Start windows left). The spike also records and restores the XAML bridge child (`DesktopWindowContentBridge`) that `PinShow` forces visible; final check reports its WS_VISIBLE against the initial value (False/False in all 4 re-runs; an earlier build that did not restore it showed it left True right after a run, which the shell later reset). Checked on the machine afterwards: bridge child WS_VISIBLE=False.

### Manual checks pending (user)
1. Run `spikes\OverflowSpike\bin\Debug\net10.0-windows10.0.22621.0\OverflowSpike.exe --run --early-hide` while watching the bottom/right of the screen. Note any taskbar flash and its duration.
2. Separately, with the taskbar hidden, press Win+B then Enter yourself: confirm the flyout shows the correct hidden icons (Discord, Steam, etc.).
3. Right-click an icon in that flyout: the context menu must appear and its items must work.
4. Repeat 1-3 with Fenn closed and the Windows taskbar set to auto-hide (and once always visible) to see behavior without the decoy `Shell_TrayWnd`/re-hide. Report whether the 300 ms criterion holds.
5. Confirm the taskbar returns to its original state after each run.

## Spike: desktop layer

Code: `spikes/DesktopLayerSpike` (`--option A|B [--selftest] [--stay] [--no-reshow]`). Raw console output of every self-test run: `spikes/DesktopLayerSpike/runs/` (runA1-3, runB1-3, runBnr1-2). Environment: Windows 11 build 26300, 1920x1080, Fenn running. Runs happened on the unlocked desktop (no LogonUI). N: Option A 3 runs; Option B with re-show 3 runs; Option B without re-show (`--no-reshow`) 2 runs. Window: 300x300 WPF window at screen (100,300) with a click-counting button. This spike does not need a signed/Program Files install (no `spike-install.txt` marker): nothing here uses uiAccess.

Self-test method: (1) `ZTest`: open an own top-level window over the spike in the normal state; (2) send Win+D with `SendInput`; (3) measure; (4) click test; (5) Win+D again and verify the foreground window is the same as before. Clicks: `SendInput` is sent only if `WindowFromPoint` at the button centre returns the spike's own HWND (checked before and again after moving the cursor); otherwise skipped. `UIA InvokePattern` is a secondary check and involves no mouse, so it does not prove a real click can reach the window.

Desktop tree observed (all runs): `Progman` (explorer, rect 0,0,1920,1080) has children `SHELLDLL_DefView` (-> `SysListView32`) and a `WorkerW` (all child windows of Progman). No top-level `WorkerW` had a `SHELLDLL_DefView` child. So Option A parented the spike to Progman (the DefView host) and put it at HWND_TOP among Progman's children (index 0 of 3 siblings).

Option A: send 0x052C to Progman, `SetParent` to Progman, `HWND_TOP`.

| Criterion | Measured | Result |
|---|---|---|
| (a) click registers | With the desktop shown (after Win+D), `WindowFromPoint(button centre)` = spike HWND; `SendInput` left click: clicks 0 -> 1 and a mouse-up event seen, in 3/3 runs. UIA `InvokePattern` also raised the count (1 -> 2) in 3/3 | PASS (N=3) |
| (b) under normal windows | In the normal state a top-level test window of mine was placed over the spike: `WindowFromPoint(spike centre)` = test window; walking `GW_HWNDNEXT` from the test window reaches Progman (the spike's root) below it, 3/3. Progman's index among top-level windows was 234 of 235 and 237 of 238 in run 1 (0 = topmost). In the normal state `WindowFromPoint` at the spike centre returned the Claude app's Chrome window (isSpike=False) in all runs, i.e. the user's app was hit-tested above the spike. Only my own test window and the user's Claude window were tested, not arbitrary apps | PASS (N=3), limited to those windows |
| (c) visible after Win+D | After Win+D #1: `IsWindowVisible`=True, `IsIconic`=False, cloaked=False, `WindowFromPoint(centre)` = spike HWND, foreground = Progman. Run 1 shown in full; the click result (a) in runs 2-3 implies the same hit result. After the restoring Win+D #2 the user's foreground window was unchanged (3/3) | PASS (N=3) |
| (d) survives Explorer restart | Not run (controller decision: Explorer must not be restarted). Watcher exists (`--stay`, 250 ms timer, recreates the window when its HWND is gone or Progman changed; logs ms since loss detected). The code-path check (calling `Attach()` again on a live window) kept it parented/visible; that is not an Explorer restart | NOT MEASURED (manual) |
| (e) not in Alt-Tab / dock list | Spike is a child window (style has WS_CHILD, parent=Progman), `EnumWindows` did not list it (False in all states), `WS_EX_TOOLWINDOW`=False, cloaked=False. Alt-Tab heuristic (visible, unowned, top-level, not toolwindow) computed False because it is not top-level. `GetWindow(GW_OWNER)` returned a non-zero handle for the child; not investigated. Alt-Tab was not pressed and no dock exists yet to check its list | Heuristic only, not observed in Alt-Tab: NOT MEASURED (manual) |

Option B: top-level `WS_EX_TOOLWINDOW|WS_EX_NOACTIVATE`, `HWND_BOTTOM` via `WM_WINDOWPOSCHANGING`, re-show on `EVENT_SYSTEM_FOREGROUND` -> Progman/WorkerW.

| Criterion | Measured | Result |
|---|---|---|
| (a) click registers | After Win+D, `WindowFromPoint(button centre)` = `SysListView32` (explorer's desktop FolderView), not the spike, in 3/3 re-show runs and 2/2 no-reshow runs. The `SendInput` click was therefore skipped by the safety rule every time, so no real click was delivered to it. UIA Invoke raised the count (0 -> 1) in 5/5, which proves only that the button's handler works | FAIL for real clicks in the measured state (hit-testing at the button goes to the desktop list view; 5/5 runs). Real click never delivered, so "the click would not register" is inferred from `WindowFromPoint`, not measured |
| (b) under normal windows | Top-level index 234-237 of 236-239 (near the bottom; user's windows above), and the own test window was above it (`WindowFromPoint` = test window in 3/5 runs; in the other 2 the Claude window was hit instead, so that check was inconclusive in those runs) | PASS for "stays near bottom" (N=5); not tested against every app |
| (c) visible after Win+D | `IsWindowVisible`=True, `IsIconic`=False after Win+D #1 in all 5 runs, **including no-reshow** (re-show was therefore not needed to keep the flag True). The re-show hook did fire in the 3 re-show runs. But `WindowFromPoint(centre)` returned the explorer `SysListView32`, not the spike, in 5/5; whether the spike is actually painted above or below the desktop icons was not measured | Flag-level PASS; actual on-screen visibility NOT MEASURED (manual) |
| (d) Explorer restart | Not run. `TaskbarCreated` handler exists (code path exercised only by posting the message to itself) | NOT MEASURED (manual) |
| (e) Alt-Tab / dock | Top-level, `WS_EX_TOOLWINDOW`=True, no `WS_EX_APPWINDOW`, visible, not cloaked; Alt-Tab heuristic computed False (5/5). `GW_OWNER` non-zero in all runs, not investigated. Alt-Tab not pressed; no dock yet | Heuristic only: NOT MEASURED (manual) |

Conclusion (from the measurements above only): Option A is the one that passed every criterion that could be measured here ((a), (b), (c), 3/3 runs); Option B failed (a) because the desktop list view was the hit-test target over the spike after Win+D. (d), (e) in Alt-Tab and any visual check remain manual. Notes: Explorer's pid changed between my first and later runs (11924 -> 11472); not caused by this spike (nothing here restarts Explorer) and the cause was not investigated. Cleanup: no DesktopLayerSpike process remained after the runs; each run ended with Win+D toggled back and the original foreground window restored (see "foreground same as before Win+D: True" in each log).

### Manual checks pending (user/controller)
1. `cd spikes\DesktopLayerSpike; .\bin\Debug\net10.0-windows10.0.22621.0\DesktopLayerSpike.exe --option A --stay` (log also at `%TEMP%\DesktopLayerSpike.log`). Click the button with the mouse (desktop shown and with apps open).
2. Criterion (d): in a terminal run `Stop-Process -Name explorer` (Explorer normally auto-restarts; with Fenn running expect side effects). Read the `WATCH:` lines in the log: need re-attach within 2 s of Progman reappearing. Confirm the widget is visible and clickable afterwards.
3. Criterion (e): press Alt-Tab and check the window list; confirm no `DesktopLayerSpike` entry. Check the dock window list once it exists.
4. Visually confirm the window is above the wallpaper/icons and under normal windows, and that it stays visible after Win+D.
5. Ctrl+C to quit.

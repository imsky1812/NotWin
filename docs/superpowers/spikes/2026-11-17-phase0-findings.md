# Phase 0 spike findings (2026-11-17)

Environment: Windows 11 build 26300, 1920x1080, .NET 10. **Fenn** (third-party taskbar/notch app, fenn.exe) was running during all tests, and the real taskbar was already hidden/auto-hidden by it. Spike code: `spikes/KeySpike`, `spikes/OverflowSpike` (throwaway, not in `NotWin.sln`). Install pipeline for the uiAccess run: `spikes/install-spikes.ps1` (elevated; publishes each spike that has a `spike-install.txt` marker, signs with `CN=NothingShell Dev`, copies to `C:\Program Files\NothingShell.Spikes\<Name>\`).

Tests ran unattended on the live desktop with synthetic `SendInput`. Caveat: synthetic keys travel the same low-level hook chain as physical keys, but the shell could in principle treat them differently (e.g. Start's Win-keyup logic). Physical confirmation is in the manual lists below.

Gotcha: while the session was locked (LogonUI running) `SendInput` never reaches the user desktop, so every key test falsely fails and UIA Invoke was slow (~2 s). Both spikes now abort when LogonUI is running. Results from that period are discarded; everything below was measured on an unlocked desktop.

## Spike: lone Win key

Method: `WH_KEYBOARD_LL` hook (`KeySpike`). Win keydown passes through. On Win keyup with no other key in between: inject VK 0xE8 down+up (marked with `dwExtraInfo` so the hook ignores them) *before* the keyup is passed on, print `OPEN LAUNCHER`. Win+A / Win+N (no Shift/Ctrl/Alt): swallow the letter keydown/up, print `OPEN PANEL`, mask the Win keyup the same way. Everything else passes through. `KeySpike.exe --selftest` drives this and detects Start via a visible, non-cloaked `StartMenuExperienceHost` window or a foreground process of `StartMenuExperienceHost`/`SearchHost`.

Observed (Debug build, non-uiAccess, `--selftest`, final run):

| Check | Result |
|---|---|
| Control: lone Win tap with hook NOT installed opens Start | Start detected (detector valid) |
| (a) lone Win tap with hook: Start stays closed, `OPEN LAUNCHER` logged | PASS |
| Win+A: `OPEN PANEL (Win+A)`, no Control Center, no Start | PASS |
| Win+N: `OPEN PANEL (Win+N)`, no notification center, no Start | PASS |
| Win+E: passes through, new Explorer window opened (closed by harness) | PASS |
| Win+D: passes through, foreground became `explorer:Progman`; second Win+D undid it | PASS |
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

Results (3 runs of `--run --early-hide` plus 1 earlier run on the unlocked desktop; initial state tray hidden, restored to hidden each time):

| Strategy | Result |
|---|---|
| 1. taskbar hidden, UIA Invoke | FAIL: chevron not found while hidden (no UIA children) |
| 2. show, find chevron, Invoke, hide again | WORKS 4/4: flyout window appeared 0-55 ms after Invoke started and survived hiding the taskbar (still open 600 ms later). Taskbar visible show-to-hide: 116, 296, 348, 377 ms. Dominated by the UIA chevron search (75-352 ms, first search is slowest). |
| 3. taskbar hidden, Win+B then Enter | WORKS 4/4: flyout appeared 6-15 ms after Enter; we never show the taskbar |

Criterion "at least one strategy with no visible taskbar flash longer than 300 ms": strategy 3 passes by construction (the taskbar is never shown by us). Strategy 2 is borderline (116 to 377 ms) and could be tightened by caching the chevron element. Not observed visually: whether the bar is painted on screen in those windows (only `IsWindowVisible` and timing were measured). Fenn's placement of the bar (rect y=1078..1138 on a 1080 px screen, i.e. mostly off-screen) may hide it anyway.

Not verified: flyout contents (icons present), right-click menus, Discord/Steam icons, flyout position when the real taskbar is hidden normally, and behavior when Fenn is NOT running. Esc does not reliably close a flyout opened this way (focus never lands in it); the spike falls back to `SW_HIDE` on the flyout window, so stale flyout state is possible.

Taskbar state: restored to its initial hidden state after every run (verified: both `Shell_TrayWnd` windows `IsWindowVisible=False`, no overflow/Explorer/Start windows left).

### Manual checks pending (user)
1. Run `spikes\OverflowSpike\bin\Debug\net10.0-windows10.0.22621.0\OverflowSpike.exe --run --early-hide` while watching the bottom/right of the screen. Note any taskbar flash and its duration.
2. Separately, with the taskbar hidden, press Win+B then Enter yourself: confirm the flyout shows the correct hidden icons (Discord, Steam, etc.).
3. Right-click an icon in that flyout: the context menu must appear and its items must work.
4. Repeat 1-3 with Fenn closed and the Windows taskbar set to auto-hide (and once always visible) to see behavior without the decoy `Shell_TrayWnd`/re-hide. Report whether the 300 ms criterion holds.
5. Confirm the taskbar returns to its original state after each run.

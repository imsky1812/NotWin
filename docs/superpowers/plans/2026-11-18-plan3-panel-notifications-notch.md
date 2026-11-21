# Plan 3: Quick Settings, Notifications and Notch Live States Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Plan 3 adds three things:
- A quick-settings and notifications panel, opened with Win+A, Win+N or the dock tray. It holds Wi-Fi, Bluetooth, volume with an output switch, brightness, and notifications.
- The notch: a black pill at the top centre of the screen with its live states (resting, music, a new notification, and "Opening ‹app›…").
- Package identity, so Nothing Shell can read Windows' notifications.

**Architecture:**
- **Pure logic in `NothingShell.Core`, unit-tested:**
  - Win-key routing for A and N;
  - the notch state machine and the 5×7 dot font;
  - notification grouping;
  - Wi-Fi list ordering and join actions;
  - output-device cycling;
  - panel-open tracking.
- **System access in `NothingShell` services**, each started lazily and stopped when not needed:
  - WinRT: media sessions, the `UserNotificationListener`, radios, Bluetooth enumeration;
  - native code: the WLAN API, Core Audio with `IPolicyConfig`, Bluetooth KS reconnect;
  - WMI: brightness.
- **Two new surfaces:**
  - `NotchWindow`: always present, topmost, does not take focus, hidden in full screen;
  - `QuickPanelWindow`: created on first open, activatable, closes when it loses focus.
- **Package identity:** `NothingShell.exe` gets a sparse package with an external location, signed with the existing `CN=NothingShell Dev` certificate. This is the approach the Phase 0 spike proved with uiAccess, identity and banners off (findings N.1–N.4, run 2).

**Tech Stack:**
- .NET 10, WPF and CsWin32, as before.
- WinRT projections from the `net10.0-windows10.0.22621.0` TFM (`Windows.Media.Control`, `Windows.UI.Notifications.Management`, `Windows.Devices.Radios`, `Windows.Devices.Bluetooth`, `Windows.Devices.Enumeration`).
- `System.Management` (NuGet, Microsoft, MIT; latest 10.x) for WMI brightness.
- `makeappx` and `signtool` from `Microsoft.Windows.SDK.BuildTools` (already used by `build/install.ps1`).

**Spec:** `docs/superpowers/specs/2026-11-17-nothing-shell-design.md`. Read:
- §3 (notification access);
- §5 (palette, type: **Geist / Geist Mono everywhere**, decided 2026-11-18);
- §6.1 (the tray click opens the panel);
- §6.3, §6.4 (states table and priority only; the open dashboard is Plan 4), §6.6, §6.7;
- §7 (surfaces, data sources, performance rules), §9.

Also read:
- `docs/superpowers/spikes/2026-11-17-phase0-findings.md`: "Spike: notification listener" and the hands-on N.1–N.4 rows;
- `spikes/NotifySpike/` (`AppxManifest.xml`, `app.uiaccess.manifest`, `register.ps1`, `Program.cs`);
- `docs/mockups/final-v2.html`: `.notch*` lines 58–69, `.qsp`/`.tile`/`.list`/`.sl`/`.track`/`.outchip`/`.nhead`/`.nc` lines 139–160, and the `G` 5×7 glyph table plus `eqSVG` at lines 262–268.

**Branch:** `plan3-panel-notch`, created from `plan2-launcher` (PRs #1 and #2 are still open; rebase onto `main` when they merge). Ask the user before pushing.

**Out of scope (Plan 4):** the notch's open hover dashboard (tiles, pages, edit mode), the timer engine and the notch timer state, and the Settings window. In Plan 3, hovering the notch does nothing, and the timer state exists in the state machine but nothing feeds it.

## Global Constraints

- **Platform and DPI:** Windows 11, primary display only; per-monitor DPI aware; sizes in DIPs (spec §2, §7).
- **Memory:** under **100 MB** total working set at idle after 10 minutes, measured after the panel and notch have been used; CPU about 0% when nothing is changing (spec §3).
- **No idle timers faster than 1 s** unless something is visible and animating. The equalizer runs only while the music state is visible (spec §7).
- **If Nothing Shell dies for any reason, the Windows taskbar must be usable again within a few seconds** (spec §2, §7). Plan 1's restore paths must keep working with package identity.
- **Keys:** a lone Win press opens the launcher (Plan 2). **Win+A / Win+N** (no Shift, Ctrl or Alt) open the panel and must not open Windows' Quick Settings, Notification Center or Start. Every other combo passes through (spec §3, §7).
- **Panel** (spec §6.3):
  - Opens from the dock tray, Win+A or Win+N. Anchored at the bottom right above the dock, **400 px wide**, glass, Geist.
  - **Wi-Fi tile:** tap toggles the radio; **▸** expands the network list (connected / join). A secured network with no saved profile opens Windows' Wi-Fi settings, never a password box of ours.
  - **Bluetooth tile:** tap toggles the radio; **▸** expands paired devices (connect / disconnect).
  - **Volume** slider plus an **output chip** that switches the default playback device.
  - **Brightness** slider for the built-in display.
  - **Notifications:**
    - grouped by app, showing the count and the latest text;
    - ✕ dismisses a group, and "Clear all";
    - clicking one opens its app.
  - Nothing else belongs in the panel.
- **Notch** (spec §6.4):
  - Black pill at the top centre of the primary display, **8 px from the top**, with a faint 1 px light edge.
  - States:
    - Resting **196×34**: dot-matrix time, then battery icon and %;
    - Music **300×34**: mini album art, then an animated **10×4** dot equalizer with red tops;
    - Notification **420×64**: app icon, app name, one line of text, then "now", shown for **~3.6 s**;
    - Timer **270×34** is Plan 4.
  - Priority: **notification > timer > music > resting**.
  - Hidden while a full-screen app is in front.
  - Launching from the launcher shows "Opening ‹name›…" (spec §6.2, §6.6).
- **Windows' own banners:** new notifications do not show Windows' bottom-right banner; they appear in the notch instead (spec §6.3). **Nothing Shell never changes the user's notification settings itself.** It shows a one-time, guided card that the user acts on.
- **Visuals:**
  - Palette: card `#1A1A1A`, tile `#161616`/`#202020`, panel `Brush.GlassDense` (solid `#141414`, user feedback 2026-11-18), line `rgba(255,255,255,0.10)`, text `#F2F2F2`, dim `#8A8A8A`, red `#E8231F` as the only accent, on-state `#F2F2F2` fill with dark text.
  - Motion 160–320 ms `cubic-bezier(.2,.8,.2,1)` (spec §5).
  - Numbers in dot matrix only; never dots for sentences (spec §5).
- **Privacy:** never write other apps' notification text, titles or senders to `log.txt` or any file. Log the app name, the count and ids only (spike warning).
- **Live checks:** implementers never start, kill or message the user's running installed shell, and never weaken `SingleInstance`. Live checks happen in the hands-on (Task 13).

## Review Focus

1. **Location access off.** Windows 11 24H2+ withholds Wi-Fi scan results from desktop apps without location consent. The network list must show a "Let desktop apps use location to list networks" row that opens `ms-settings:privacy-location`, not an empty list or a crash. *(Task 10 test `Classify_AccessDenied_ShowsLocationHint`.)*
2. **Notification floods and full screen.** A chat app sending many messages must not queue a minute of notch animations: the newest replaces the current one and restarts the 3.6 s. During full screen the notch stays hidden and nothing replays afterwards. *(Task 3 tests `NewNotification_ReplacesCurrentAndRestartsTimer`, `Fullscreen_HidesEverything_AndDropsTransients`.)*
3. **Media edge cases.** No session, a paused session, several sessions, or a session without a thumbnail: show music only while the current session is Playing; without art, show a plain grey dot. *(Task 3 test `Music_OnlyWhilePlaying`; Task 5 test `NoThumbnail_UsesPlaceholder`.)*
4. **Side effects of package identity.** `%AppData%\NothingShell` writes must reach the real folder, because the unpackaged watchdog reads `taskbar-restore.json` there. Uninstall must also remove the package. *(Task 2 manifest declares `unvirtualizedResources` plus `FileSystemWriteVirtualization=disabled`. Hands-on PN.1 kills the shell and expects the taskbar back.)*
5. **Device changes while the panel is open** (headphones plugged in or out, Bluetooth device off, the Wi-Fi adapter disabled): no stale entries, and no actions on missing devices. *(Task 8 test `Next_CurrentMissing_StartsAtFirst`; Task 11's device list is rebuilt on each expand.)*

---

## File Structure

```
src/NothingShell.Core/
  Launcher/LoneWinDetector.cs          (modify) Win+A / Win+N -> OpenPanel, swallowed, masked
  Notch/DotFont.cs                     5x7 glyphs (mockup table), Measure/Dots
  Notch/NotchStateMachine.cs           priority + transient expiry (pure, clock injected)
  Panel/NotificationGroups.cs          NotificationItem -> grouped rows
  Panel/WifiRules.cs                   network ordering + join action
  Panel/AudioOutputs.cs                next-device cycling
  Shell/PanelTracker.cs                which panels hold the dock up
src/NothingShell.Native/
  WlanNative.cs                        WlanOpenHandle/EnumInterfaces/GetAvailableNetworkList/Connect
  AudioPolicyNative.cs                 render endpoints + IPolicyConfig.SetDefaultEndpoint
  BluetoothAudioNative.cs              KS one-shot reconnect/disconnect for BT audio
src/NothingShell/
  Package/AppxManifest.xml, Package/Assets/logo.png
  app.uiaccess.manifest, app.manifest  (modify) <msix> identity element
  Notch/NotchWindow.xaml(.cs)          surface, layers, dot renderer, equalizer
  Notch/NotchController.cs             state machine <-> services <-> window
  Media/MediaSessionService.cs         GSMTC current session, playing, title, art
  Notifications/NotificationService.cs UserNotificationListener wrapper
  Panel/QuickPanelWindow.xaml(.cs)     tiles, lists, sliders, notifications
  Panel/QuickPanelController.cs        open/close/toggle, anchors, dock hold
  Panel/WifiService.cs, BluetoothService.cs, AudioOutputService.cs, BrightnessService.cs
build/install.ps1, build/uninstall.ps1 (modify) sparse package pack/sign/register/remove
build/verify/plan1-handson.ps1         (modify) Panel + Notch sections
tests/NothingShell.Core.Tests/{Notch,Panel,Shell}/*Tests.cs
```

---

### Task 1: Win+A / Win+N open the panel

**Files:**
- Modify: `src/NothingShell.Core/Launcher/LoneWinDetector.cs`, `src/NothingShell/Launcher/WinKeyListener.cs`
- Test: `tests/NothingShell.Core.Tests/Launcher/LoneWinDetectorTests.cs` (add)

**Interfaces:**
- Produces:
  - `KeyVerdict` gains `bool OpenPanel` as a 4th positional member with default `false`: `KeyVerdict(bool Swallow, bool InjectMaskFirst, bool OpenLauncher, bool OpenPanel = false)`.
  - `WinKeyListener.PanelRequested` (event `Action`, raised on the UI thread).

- [ ] **Step 1: Confirm the branch.** `plan3-panel-notch` was created from `plan2-launcher` together with this plan. Run `git branch --show-current`. Expected: `plan3-panel-notch`.
- [ ] **Step 2: Write the failing tests.** Use A = `0x41`, N = `0x4E`, LShift = `0xA0`, LCtrl = `0xA2`, LMenu = `0xA4`.
  - `WinA_SwallowsLetterAndOpensPanel`: down(LWin); down(A) → `(Swallow:true, InjectMaskFirst:false, OpenLauncher:false, OpenPanel:true)`; up(A) → Swallow only; up(LWin) → `(false, InjectMaskFirst:true, false, false)`.
  - `WinN_SameAsWinA`.
  - `WinShiftA_PassesThrough`: down(LWin), down(LShift), down(A). Not swallowed, no panel.
  - `WinCtrlN_PassesThrough`.
  - `AutoRepeatA_OpensOnce`: a second down(A) without an up is swallowed and `OpenPanel` is false.
  - `WinA_ThenWinE_EStillPassesThrough`: down(LWin), A down/up, down(E) → passes.
  - Existing lone-Win tests stay green.
- [ ] **Step 3: Run them and confirm they fail.** Run `dotnet test tests/NothingShell.Core.Tests --filter LoneWinDetector`. Expected: compile error (no `OpenPanel`).
- [ ] **Step 4: Implement.**
  - Track whether A or N is currently swallowed: its key-up must be swallowed too, even if Win was released first.
  - A swallowed letter makes Win "chorded", so the Win key-up gets the mask and no launcher opens.
- [ ] **Step 5: Run them and confirm they pass.** Then wire `WinKeyListener.PanelRequested` by posting to the UI thread, exactly like `LauncherRequested`. Until Task 7, the App only logs `panel requested`.
- [ ] **Step 6: Build and test.** Run `dotnet build NotWin.sln -c Release` and `dotnet test NotWin.sln`. Expected: green.
- [ ] **Step 7: Commit.** Message: `feat: Win+A / Win+N are routed to Nothing Shell's panel`.

### Task 2: Package identity (sparse package) for the shell

**Files:**
- Create:
  - `src/NothingShell/Package/AppxManifest.xml`, `src/NothingShell/Package/Assets/logo.png` (copy the spike logo);
  - `src/NothingShell/Shell/PackageIdentity.cs` (`static bool HasIdentity(out string? fullName)` via `GetCurrentPackageFullName`).
- Modify:
  - `src/NothingShell/app.uiaccess.manifest` and `src/NothingShell/app.manifest`: add the `<msix publisher="CN=NothingShell Dev" packageName="NothingShell" applicationId="App"/>` element;
  - `build/install.ps1`, `build/uninstall.ps1`;
  - `src/NothingShell/App.xaml.cs`: log `package identity=<bool> name=<full name>` at startup;
  - `src/NothingShell.Native/NativeMethods.txt`: add `GetCurrentPackageFullName`.

**Interfaces:**
- Produces: package `NothingShell` (publisher `CN=NothingShell Dev`, application `App`, executable `NothingShell.exe`), registered with external location `C:\Program Files\NothingShell`, and `PackageIdentity.HasIdentity`.

- [ ] **Step 1: Write `AppxManifest.xml`.**
  - Start from `spikes/NotifySpike/AppxManifest.xml` with `Identity Name="NothingShell"`, `DisplayName "Nothing Shell"` and `Executable="NothingShell.exe"`.
  - Capabilities:
    - `rescap:runFullTrust`;
    - `uap3:userNotificationListener`;
    - `rescap:unvirtualizedResources`;
    - `DeviceCapability Name="radios"`, so `Windows.Devices.Radios` access works with identity;
    - `DeviceCapability Name="bluetooth"`.
  - Properties: `desktop6:FileSystemWriteVirtualization>disabled` and `desktop6:RegistryWriteVirtualization>disabled`. Add the `desktop6` namespace to `IgnorableNamespaces`.
- [ ] **Step 2: Extend `build/install.ps1`.**
  - After copying to Program Files, stage `Package/` into `out/package`, then `makeappx pack /nv /o`, `signtool sign /fd SHA256 /sha1 <cert thumbprint>`, `Get-AppxPackage NothingShell | Remove-AppxPackage`, and `Add-AppxPackage -Path out/NothingShell.msix -ExternalLocation $install`.
  - Run it before the final launch.
  - On failure, print a clear warning and continue: the shell still works without identity, just without notifications.
  - **`uninstall.ps1`:** remove the package before deleting files.
- [ ] **Step 3: Log identity at startup.** Log the identity line in `App.OnStartup` right after the `startup uiAccess=` line. `NotificationService` (Task 6) uses `HasIdentity` to show "Notifications need the package. Run install.ps1" in the panel instead of failing.
- [ ] **Step 4: Verify without the live shell.**
  - Parse-check both scripts.
  - Run `dotnet build -c Release -p:UiAccess=true`. Expected: success.
  - Check that the `<msix>` element is in the embedded manifest: `mt.exe` is not needed; read the PE resource with `[System.Diagnostics.FileVersionInfo]`, or simply search the published exe bytes for `packageName="NothingShell"`.
- [ ] **Step 5: Commit.** Message: `feat: sparse package identity for the shell (notifications, radios)`.

### Task 3: Notch state machine and dot font

**Files:**
- Create: `src/NothingShell.Core/Notch/NotchStateMachine.cs`, `src/NothingShell.Core/Notch/DotFont.cs`
- Test: `tests/NothingShell.Core.Tests/Notch/NotchStateMachineTests.cs`, `DotFontTests.cs`

**Interfaces:**
- Produces:
  - `public enum NotchState { Hidden, Resting, Music, Timer, Notification, Opening }`
  - `public readonly record struct NotchNotification(string AppId, string AppName, string Text)`
  - `public sealed class NotchStateMachine`, with these members:
    - `public static readonly TimeSpan NotificationTime = TimeSpan.FromMilliseconds(3600)`
    - `public static readonly TimeSpan OpeningTime = TimeSpan.FromMilliseconds(1600)`
    - `void Notify(NotchNotification n, DateTimeOffset now)`
    - `void Opening(string name, DateTimeOffset now)`
    - `void SetMusicPlaying(bool playing)`
    - `void SetTimerRunning(bool running)`
    - `void SetFullscreen(bool fs)`
    - `NotchState StateAt(DateTimeOffset now)`
    - `NotchNotification? CurrentNotification`
    - `string? OpeningName`
    - `DateTimeOffset? NextChangeAt(DateTimeOffset now)` (when a transient ends)
  - `public static class DotFont`, with these members:
    - `bool[] GlyphRows(char c)`: 7 strings expanded to rows; null for unknown characters;
    - `int ColumnsFor(string text)`: sum of glyph widths plus 1 gap column between glyphs, without a trailing gap;
    - `IEnumerable<(int Col, int Row)> LitDots(string text)`.

- [ ] **Step 1: Write the failing tests.**
  - State machine:
    - `Resting_ByDefault`.
    - `Music_OnlyWhilePlaying`.
    - `Notification_ShowsFor3600ms_ThenFallsBack`: Music while playing, then Notification until +3600 ms, then Music.
    - `NewNotification_ReplacesCurrentAndRestartsTimer`: a second Notify at +2 s shows the second one until +5.6 s.
    - `Notification_BeatsTimerAndMusic`.
    - `Timer_BeatsMusic`.
    - `Opening_ShowsFor1600ms_BelowNotification`: Opening during a notification is dropped.
    - `Fullscreen_HidesEverything_AndDropsTransients`: Notify during full screen → Hidden; leaving full screen at +1 s → Resting, not Notification.
    - `NextChangeAt_IsTransientEnd`.
  - Dot font:
    - `Digits_AreFiveWide_SevenHigh`.
    - `Colon_IsOneWide`: rows `0,0,1,0,1,0,0`.
    - `Columns_For_1234WithColon`: `ColumnsFor("12:34") == 5+1+5+1+1+1+5+1+5 = 25`.
    - `Unknown_IsSkipped`.
    - `Glyph_Zero_MatchesMockup`: the rows from the mockup `G['0']`.
- [ ] **Step 2: Run them and confirm they fail.** Run `dotnet test tests/NothingShell.Core.Tests --filter "Notch|DotFont"`. Expected: compile errors.
- [ ] **Step 3: Implement.** The glyph table is copied from the mockup's `G` (digits, `:`, `%`, `°`). The mockup is the project's own approved design, so this is not third-party code.
- [ ] **Step 4: Run them and confirm they pass.** Same command as Step 2. Expected: PASS.
- [ ] **Step 5: Commit.** Message: `feat: notch state machine and 5x7 dot font`.

### Task 4: Notch window (resting + "Opening…")

**Files:**
- Create: `src/NothingShell/Notch/NotchWindow.xaml(.cs)`, `src/NothingShell/Notch/NotchController.cs`, `src/NothingShell/Notch/DotMatrix.cs` (a `FrameworkElement` that draws `DotFont.LitDots` as ellipses with `OnRender`)
- Modify: `src/NothingShell/App.xaml.cs` (create after the dock; `launcher.Launched += controller.Opening`; full-screen changes feed `SetFullscreen`)

**Interfaces:**
- Consumes: `NotchStateMachine`, `DotFont` (Task 3), `TrayStatusService` (`Time`, `BatteryPercent`, `Charging`, `HasBattery`), `LauncherController.Launched`, `FullscreenMonitor.Changed`.
- Produces: `NotchController.Notify(NotchNotification)`, `NotchController.SetMusic(bool playing, ImageSource? art)` (Task 5), `NotchController.Opening(string)`.

- [ ] **Step 1: Window.**
  - Borderless, transparent, topmost, `ShowInTaskbar=False`, never activated (`WS_EX_NOACTIVATE` | `WS_EX_TOOLWINDOW` via `DockWindowNative`, like the dock).
  - Placed at the top centre of the primary monitor, 8 DIP from the top. Re-placed on display changes.
  - Pill: `#000000`, a 1 px `#14FFFFFF` inside edge, and the mockup's soft shadow (`0 8 30 rgba(0,0,0,.28)`).
  - Size and corner radius animate over 300 ms with the standard ease:
    - Resting 196×34, r 20;
    - Music 300×34;
    - Notification 420×64, r 30;
    - Opening 300×34.
  - Content layers cross-fade over 160 ms, delayed 100 ms.
- [ ] **Step 2: Resting layer.**
  - Left, 14 px padding: dot-matrix `HH:mm`, dot diameter 3, gap 1, white.
  - Right: a battery outline glyph plus `NN%` in Geist Mono Medium 11 `#DDDDDD`. Red fill under 20% when not charging; ⚡ when charging. Hidden when `HasBattery` is false.
- [ ] **Step 3: Opening layer.** "Opening" in dim Geist 12, then the name in Geist Medium 12, ellipsised.
- [ ] **Step 4: Controller.**
  - One `DispatcherTimer`, armed only for `NextChangeAt`; there is no polling.
  - The clock updates through `TrayStatusService.PropertyChanged` (it already ticks once a minute).
  - Hidden state: `Hide()`. Every other state: `Show()` without activating.
  - Log `notch state=<State>` on changes only.
- [ ] **Step 5: Build and test.** Run `dotnet build NotWin.sln -c Release` and `dotnet test NotWin.sln`. Expected: green.
- [ ] **Step 6: Commit.** Message: `feat: notch window with resting time/battery and launcher "Opening…"`.

### Task 5: Media session and the notch music state

**Files:**
- Create: `src/NothingShell/Media/MediaSessionService.cs`
- Modify: `src/NothingShell/Notch/NotchWindow.xaml(.cs)`, `src/NothingShell/Notch/NotchController.cs`
- Test: `tests/NothingShell.Tests/MediaSessionServiceTests.cs`

**Interfaces:**
- Produces:
  - `public sealed record MediaSnapshot(bool Playing, string? Title, string? Artist, string? SourceAppId, ImageSource? Art)`
  - `public sealed class MediaSessionService : IDisposable { public event Action<MediaSnapshot>? Changed; public MediaSnapshot Current { get; } public void Start(); }`
  - The `MediaSessionService(Func<Task<ISessionSource?>> sessionManager)` constructor takes an `ISessionSource` adapter with `PlaybackPlaying`, `Title`, `Artist`, `AppId` and `Task<Stream?> ThumbnailAsync()`, so tests can run without WinRT.

- [ ] **Step 1: Write the failing tests.**
  - `NoSession_IsNotPlaying`.
  - `PausedSession_IsNotPlaying`.
  - `PlayingSession_ReportsTitleAndArtist`.
  - `NoThumbnail_UsesPlaceholder`: `Art` is null, and the window draws a `#3A3A3A` dot.
  - `SessionChange_RaisesChanged`.
- [ ] **Step 2: Run them and confirm they fail.** Run `dotnet test NotWin.sln --filter MediaSession`. Expected: compile errors.
- [ ] **Step 3: Implement.**
  - The real adapter uses `GlobalSystemMediaTransportControlsSessionManager.RequestAsync()`, plus `CurrentSessionChanged`, `PlaybackInfoChanged` and `MediaPropertiesChanged`.
  - The thumbnail is decoded to a 44 px frozen `BitmapImage`.
  - Log the source app id only, never the title.
- [ ] **Step 4: Music layer.**
  - Left: 22 px art with radius 7.
  - Right: a 10×4 dot equalizer, dot 3 px, pitch 5 px. Lit dots are `#F2F2F2`, the top lit dot is `#E8231F`, and unlit dots are `#222222`.
  - Animate at 8 fps (125 ms) with random heights, **only while the Music state is visible**.
  - Feed `SetMusicPlaying` from `Changed`.
- [ ] **Step 5: Run the tests and confirm they pass.** Then build. Expected: green.
- [ ] **Step 6: Commit.** Message: `feat: notch music state from the system media session`.

### Task 6: Notification service and the notch notification state

**Files:**
- Create: `src/NothingShell.Core/Panel/NotificationGroups.cs`, `src/NothingShell/Notifications/NotificationService.cs`
- Modify: `src/NothingShell/Notch/*`, `src/NothingShell/App.xaml.cs`
- Test: `tests/NothingShell.Core.Tests/Panel/NotificationGroupsTests.cs`

**Interfaces:**
- Produces:
  - `public sealed record NotificationItem(uint Id, string AppId, string AppName, string Text, DateTimeOffset Time)`
  - `public sealed record NotificationGroup(string AppId, string AppName, int Count, string LatestText, DateTimeOffset LatestTime, IReadOnlyList<uint> Ids)`
  - `public static class NotificationGroups { public static IReadOnlyList<NotificationGroup> Build(IEnumerable<NotificationItem> items); }`. Groups are ordered by `LatestTime` descending.
  - `public sealed class NotificationService : IDisposable`, with these members:
    - `bool Available`
    - `string? UnavailableReason` (one of `no package identity`, `access denied` or `listener error`)
    - `event Action<NotificationItem>? Added`
    - `event Action? ListChanged`
    - `Task<IReadOnlyList<NotificationItem>> GetAllAsync()`
    - `void Remove(IEnumerable<uint> ids)`
    - `void ClearAll()`
    - `bool OpenApp(string appId)`: `shell:AppsFolder\<appId>` via `ShellLinkNative.Open`

- [ ] **Step 1: Write the failing tests.**
  - `Build_GroupsByApp_CountsAndLatest`.
  - `Build_OrdersGroupsNewestFirst`.
  - `Build_LatestTextIsNewestItemsText`.
  - `Build_Empty_ReturnsEmpty`.
- [ ] **Step 2: Run them and confirm they fail.** Run `dotnet test tests/NothingShell.Core.Tests --filter NotificationGroups`. Expected: compile errors.
- [ ] **Step 3: Implement `NotificationGroups`.** Run the tests. Expected: pass.
- [ ] **Step 4: Implement `NotificationService`.**
  - Follow the spike: `UserNotificationListener.Current`, `RequestAccessAsync`, `GetNotificationsAsync(NotificationKinds.Toast)`, and `NotificationChanged`. `Added` fetches the item by id.
  - Text is the `ToastGeneric` binding's text elements joined with " · ".
  - If the event stops firing, there is a 1 s poll fallback, but only while the panel is open.
  - It never logs text; it logs `notification added app=<name> id=<id>` only.
  - Without identity, `Available=false` and a reason; there is no exception.
- [ ] **Step 5: Notch notification layer.**
  - A 36 px `#1D1D1D` circle with the app icon (`AppIconCache.Get(appId)`).
  - App name in Geist SemiBold 13.
  - One line of text in Geist 12 `#AAAAAA`, ellipsised.
  - "NOW" in Geist Mono 9 dim on the right.
  - `Added` feeds `NotchController.Notify` with a `NotchNotification` built from the item.
- [ ] **Step 6: Build and test.** Run `dotnet build NotWin.sln -c Release` and `dotnet test NotWin.sln`. Expected: green.
- [ ] **Step 7: Commit.** Message: `feat: notifications via UserNotificationListener; notch notification state`.

### Task 7: Quick-settings panel shell

**Files:**
- Create: `src/NothingShell.Core/Shell/PanelTracker.cs`, `src/NothingShell/Panel/QuickPanelWindow.xaml(.cs)`, `src/NothingShell/Panel/QuickPanelController.cs`
- Modify:
  - `src/NothingShell/Dock/DockReveal.cs`: `SetPanelOpen(bool)` becomes `SetPanelOpen(string source, bool open)`, backed by `PanelTracker`. Update its callers: dock popups pass `"popup"`.
  - `src/NothingShell/Dock/DockWindow.xaml(.cs)`: a `TrayClicked` event on a left-click of the tray area (not ⌃).
  - `src/NothingShell/App.xaml.cs`
- Test: `tests/NothingShell.Core.Tests/Shell/PanelTrackerTests.cs`

**Interfaces:**
- Produces:
  - `public sealed class PanelTracker { public void Set(string source, bool open); public bool AnyOpen { get; } }`
  - `QuickPanelController.Toggle()`, `Open()`, `Close()` and `IsOpen`.
  - `QuickPanelWindow` exposes `StackPanel TilesHost`, `StackPanel SlidersHost` and `StackPanel NotificationsHost` for Tasks 8–12.

- [ ] **Step 1: Write the failing tests.**
  - `NoneOpen_ByDefault`.
  - `OneSource_OpenThenClosed`.
  - `TwoSources_ClosingOneKeepsOpen`: this is the Plan 2 ruling's "popup closing must not clear the launcher's hold".
  - `SameSourceTwice_IsIdempotent`.
- [ ] **Step 2: Run them and confirm they fail.** Run `dotnet test tests/NothingShell.Core.Tests --filter PanelTracker`. Expected: compile error.
- [ ] **Step 3: Implement `PanelTracker`.** Switch `DockReveal` to it. Run the tests and confirm they pass.
- [ ] **Step 4: Window.**
  - 400 wide, padding 18, radius 28, `Brush.GlassDense` with the panel line.
  - Max height 600, with the thin scrollbar style from the launcher (move that style into `Theme/Tokens.xaml` so both use it).
  - Bottom-right: 24 DIP from the right edge, and 84 DIP above the monitor bottom, i.e. above the dock.
  - Topmost, activatable; closes on `Deactivated`, Esc, or the same key again.
  - Open motion: fade plus a 8 px upward slide over 200 ms.
  - The dock is held while the panel is open (`SetPanelOpen("panel", …)`).
  - Created on first open; hidden afterwards.
- [ ] **Step 5: Wire it up.** Connect `WinKeyListener.PanelRequested`, the dock's `TrayClicked`, and `FullscreenMonitor` (entering full screen closes the panel).
- [ ] **Step 6: Build and test.** Run `dotnet build NotWin.sln -c Release` and `dotnet test NotWin.sln`. Expected: green.
- [ ] **Step 7: Commit.** Message: `feat: quick-settings panel window (Win+A / Win+N / tray)`.

### Task 8: Volume slider and the output chip

**Files:**
- Create: `src/NothingShell.Core/Panel/AudioOutputs.cs`, `src/NothingShell.Native/AudioPolicyNative.cs`, `src/NothingShell/Panel/AudioOutputService.cs`
- Modify: `src/NothingShell.Native/AudioVolumeWatcher.cs` (add `SetMasterScalar(float)`), `QuickPanelWindow`
- Test: `tests/NothingShell.Core.Tests/Panel/AudioOutputsTests.cs`

**Interfaces:**
- Produces:
  - `public readonly record struct AudioOutput(string Id, string Name)`
  - `public static class AudioOutputs { public static AudioOutput? Next(IReadOnlyList<AudioOutput> outputs, string? currentId); }`
  - `AudioPolicyNative`:
    - `IReadOnlyList<(string Id, string Name)> ActiveRenderEndpoints()`
    - `string? DefaultRenderId()`
    - `bool SetDefault(string id)`: sets the console, multimedia and communications roles through `IPolicyConfig`, CLSID `870af99c-171d-4f9e-af0d-e63df40c2bc9`, IID `f8679f50-850a-41cf-9c72-430f290290c8`, vtable slot 13 `SetDefaultEndpoint(LPCWSTR, ERole)`.

- [ ] **Step 1: Write the failing tests.**
  - `Next_CyclesToFollowingDevice`.
  - `Next_WrapsAround`.
  - `Next_SingleDevice_ReturnsNull`: the chip is disabled.
  - `Next_CurrentMissing_StartsAtFirst`.
- [ ] **Step 2: Run them and confirm they fail.** Run `dotnet test tests/NothingShell.Core.Tests --filter AudioOutputs`. Expected: compile error.
- [ ] **Step 3: Implement.**
  - **Slider:**
    - A `.sl` row: a speaker glyph, then a 28 px rounded `#262626` track with a `#F2F2F2` fill, then the output chip.
    - Dragging sets the master volume live.
    - The value follows external volume changes through the existing watcher.
  - **Output chip:**
    - Geist Mono 10 uppercase on `#262626`, showing the current device's short name (text before the first " (").
    - A click switches to `Next`.
    - Endpoints are read when the panel opens.
- [ ] **Step 4: Run all tests.** Run `dotnet test NotWin.sln`. Expected: pass.
- [ ] **Step 5: Commit.** Message: `feat: panel volume slider and output device chip`.

### Task 9: Brightness slider

**Files:**
- Create: `src/NothingShell/Panel/BrightnessService.cs`
- Modify: `src/NothingShell/NothingShell.csproj` (`System.Management`, latest 10.x), `QuickPanelWindow`
- Test: `tests/NothingShell.Tests/BrightnessServiceTests.cs`

**Interfaces:**
- Produces: `public sealed class BrightnessService { public BrightnessService(IBrightnessBackend? backend = null); public bool Supported { get; } public int Get(); public void Set(int percent); }`. Here `IBrightnessBackend` has `int? Read()` and `void Write(int)`. The real backend uses WMI `root\wmi` `WmiMonitorBrightness.CurrentBrightness` and `WmiMonitorBrightnessMethods.WmiSetBrightness(1, percent)`.

- [ ] **Step 1: Write the failing tests.**
  - `Unsupported_WhenBackendReadsNull`: the row is hidden.
  - `Set_ClampsTo0_100`.
  - `Set_Coalesces_RapidDrags`: at most one write per 50 ms, and the final value is always written. Use an injected clock.
- [ ] **Step 2: Run them and confirm they fail.** Run `dotnet test NotWin.sln --filter Brightness`. Expected: compile error.
- [ ] **Step 3: Implement.** WMI calls run off the UI thread. `System.Management` is loaded only when the panel first opens. The row is a sun glyph plus the track.
- [ ] **Step 4: Run all tests.** Run `dotnet test NotWin.sln`. Expected: pass.
- [ ] **Step 5: Commit.** Message: `feat: panel brightness slider (WMI)`.

### Task 10: Wi-Fi tile

**Files:**
- Create: `src/NothingShell.Core/Panel/WifiRules.cs`, `src/NothingShell.Native/WlanNative.cs`, `src/NothingShell/Panel/WifiService.cs`
- Modify: `src/NothingShell.Native/NativeMethods.txt` (`WlanOpenHandle`, `WlanCloseHandle`, `WlanEnumInterfaces`, `WlanScan`, `WlanGetAvailableNetworkList`, `WlanConnect`, `WlanFreeMemory`, `WlanSetProfile`), `QuickPanelWindow`
- Test: `tests/NothingShell.Core.Tests/Panel/WifiRulesTests.cs`

**Interfaces:**
- Produces:
  - `public sealed record WifiNetwork(string Ssid, int SignalPercent, bool Secured, bool HasProfile, bool Connected)`
  - `public enum WifiAction { None, Connect, ConnectWithNewOpenProfile, OpenSettings }`
  - `public enum WifiScanStatus { Ok, NoAdapter, RadioOff, LocationDenied, Error }`
  - `public static class WifiRules`, with these members:
    - `IReadOnlyList<WifiNetwork> Order(IEnumerable<WifiNetwork> networks)`: connected first, then saved, then by signal descending; duplicate SSIDs merged with the strongest kept; empty SSIDs dropped;
    - `WifiAction ActionFor(WifiNetwork n)`;
    - `string? Hint(WifiScanStatus s)`
  - `WifiService`:
    - `Task<(WifiScanStatus, IReadOnlyList<WifiNetwork>)> ScanAsync()`
    - `Task<bool> SetRadioAsync(bool on)` (`Windows.Devices.Radios`, `RadioKind.WiFi`)
    - `bool Connect(WifiNetwork n)`

- [ ] **Step 1: Write the failing tests.**
  - `Order_ConnectedThenKnownThenSignal`.
  - `Order_MergesDuplicateSsids`.
  - `Order_DropsHiddenEmptySsid`.
  - `Action_Connected_None`.
  - `Action_Known_Connect`.
  - `Action_OpenUnknown_ConnectWithNewOpenProfile`.
  - `Action_SecuredUnknown_OpensSettings`.
  - `Classify_AccessDenied_ShowsLocationHint`: `Hint(LocationDenied)` contains "location".
  - `Hint_RadioOff`.
- [ ] **Step 2: Run them and confirm they fail.** Run `dotnet test tests/NothingShell.Core.Tests --filter WifiRules`. Expected: compile error.
- [ ] **Step 3: Implement.**
  - **Mapping:** `WlanGetAvailableNetworkList` `ERROR_ACCESS_DENIED` (5) maps to `LocationDenied`.
  - **Tile** (`.tile`, 60 px, radius 20):
    - Wi-Fi glyph and "Wi-Fi", plus a mono subline with the connected SSID or `OFF`.
    - It shows the on-state when the radio is on.
    - Tapping toggles the radio; **▸** expands the `.list` with up to 8 networks plus "More Wi-Fi settings" (`ms-settings:network-wifi`).
    - Hint rows open `ms-settings:privacy-location`.
    - The scan runs on expand only. SSIDs are never logged.
  - **Open-network profile:** the minimal WLAN profile XML (`<authentication>open</authentication><encryption>none</encryption>`), via `WlanSetProfile`, then `WlanConnect`.
- [ ] **Step 4: Run all tests.** Run `dotnet test NotWin.sln`. Expected: pass.
- [ ] **Step 5: Commit.** Message: `feat: panel Wi-Fi tile (radio, networks, join)`.

### Task 11: Bluetooth tile

**Files:**
- Create: `src/NothingShell.Core/Panel/BluetoothRules.cs`, `src/NothingShell.Native/BluetoothAudioNative.cs`, `src/NothingShell/Panel/BluetoothService.cs`
- Modify: `QuickPanelWindow`
- Test: `tests/NothingShell.Core.Tests/Panel/BluetoothRulesTests.cs`

**Interfaces:**
- Produces:
  - `public sealed record BtDevice(string Id, string Name, bool Connected, bool IsAudio)`
  - `BluetoothService`:
    - `Task<bool?> RadioOnAsync()` (null when there is no radio)
    - `Task<bool> SetRadioAsync(bool on)`
    - `Task<IReadOnlyList<BtDevice>> PairedAsync()` (via `DeviceInformation.FindAllAsync(BluetoothDevice.GetDeviceSelectorFromPairingState(true))` plus `BluetoothDevice.FromIdAsync(...).ConnectionStatus`; audio means the class-of-device major class is AudioVideo)
    - `Task<bool> ToggleConnectionAsync(BtDevice d)`
  - `BluetoothAudioNative.SetConnected(string deviceName, bool connect)`: finds the KS audio filter whose friendly name matches the device and sends `KSPROPERTY_ONESHOT_RECONNECT` (connect) or `KSPROPERTY_ONESHOT_DISCONNECT` (disconnect) through `IKsControl`.

- [ ] **Step 1: Write the failing test.** In `BluetoothRulesTests`, add `Order_ConnectedFirstThenName` for `public static IReadOnlyList<BtDevice> BluetoothRules.Order(IEnumerable<BtDevice>)`. `BtDevice` lives in Core next to it. Run `dotnet test tests/NothingShell.Core.Tests --filter BluetoothRules`. Expected: compile error.
- [ ] **Step 2: Implement `BluetoothRules.Order`.** Run the tests. Expected: pass.
- [ ] **Step 3: Implement the service and the tile.**
  - The tile mirrors Wi-Fi: tapping toggles the radio; ▸ lists paired devices in `BluetoothRules.Order` order.
  - Each row shows "Connected" or "Connect".
  - For audio devices, the action goes through `BluetoothAudioNative`.
  - For non-audio devices, or when the KS path fails, it opens `ms-settings:bluetooth` (spec: connect / disconnect is the builder's call, with this as the fallback).
  - Device names are never logged.
- [ ] **Step 4: Build and test.** Run `dotnet build NotWin.sln -c Release` and `dotnet test NotWin.sln`. Expected: green.
- [ ] **Step 5: Commit.** Message: `feat: panel Bluetooth tile (radio, paired devices, audio connect)`.

### Task 12: Notifications in the panel and the banner guidance

**Files:**
- Modify: `QuickPanelWindow`, `QuickPanelController`, `src/NothingShell.Core/ShellSettings.cs` (add `bool BannerTipDismissed`)
- Test: `tests/NothingShell.Core.Tests/SettingsStoreTests.cs` (round-trip `BannerTipDismissed`)

**Interfaces:**
- Consumes: `NotificationService`, `NotificationGroups` (Task 6).

- [ ] **Step 1: Write the failing test** `SaveThenLoad_RoundTripsBannerTipDismissed`. Run it and confirm it fails. Implement the property and confirm the test passes.
- [ ] **Step 2: Build the notifications section.**
  - Header row: "NOTIFICATIONS" (Geist Mono 10 dim) and "CLEAR ALL".
  - Groups as `.nc` cards:
    - background `#0FFFFFFF`, radius 18, padding 12/14;
    - a 32 px circle with the app icon;
    - the app name and a count in Geist SemiBold 13;
    - the latest text in Geist 12 `#BBBBBB`, 2 lines max;
    - a ✕ at the top right that removes the group's ids.
  - Clicking a card calls `OpenApp`, then closes the panel.
  - Empty state: "No notifications" dim.
  - When `Available` is false, one dim line with the reason.
- [ ] **Step 3: Add the one-time banner card.**
  - Shown at the top of the section until dismissed.
  - Text: "New notifications appear in the notch. To stop Windows' pop-ups too, turn on Do not disturb, or turn off banners for chatty apps." Two buttons: "Open settings" (`ms-settings:notifications`) and "Got it", which sets `BannerTipDismissed`.
  - Nothing Shell never changes the setting itself.
- [ ] **Step 4: Build and test.** Run `dotnet build NotWin.sln -c Release` and `dotnet test NotWin.sln`. Expected: green.
- [ ] **Step 5: Commit.** Message: `feat: panel notifications (grouped, dismiss, clear all, open app) and banner tip`.

### Task 13: Hands-on, budget and docs

**Files:**
- Modify: `build/verify/plan1-handson.ps1` (sections `Panel` and `Notch`, plus PN.1), `docs/superpowers/spikes/2026-11-17-phase1-measurements.md` (Phase 3), `README.md`

- [ ] **Step 1: Add the hands-on checks.** The script measures from the log where it can; the user answers y/n.
  - **PN.1:** `package identity=True` is logged. Kill the shell hard (elevated `Stop-Process`): the taskbar is back within a few seconds via the watchdog (identity did not break the state file). Then restart via Start-App.
  - **NT.1:** the notch shows at the top centre, with dot-matrix time and battery.
  - **NT.2:** launching Calculator from the launcher shows "Opening Calculator…" briefly.
  - **NT.3:** play music in Spotify or a browser: art plus equalizer. Pause: back to resting within about 1 s.
  - **NT.4:** the script sends a test toast (the spike's `send-test-toast.ps1` approach). The notch shows app plus text for about 3.6 s, and Windows' banner does or doesn't show depending on the user's banner settings (recorded, not judged).
  - **NT.5:** a full-screen video hides the notch; a toast during full screen does not pop the notch.
  - **QP.1:** Win+A opens the panel bottom right and Windows' Quick Settings does not open; the same for Win+N; Win+A again closes it.
  - **QP.2:** a tray click opens the panel; a click outside closes it; the dock stays up while it is open.
  - **QP.3:** the volume slider moves the system volume; the output chip switches device (if there are 2 or more).
  - **QP.4:** brightness changes the screen (laptop panel).
  - **QP.5:** the Wi-Fi tile toggles the radio (the user confirms they're OK to drop Wi-Fi for a few seconds) and the list shows networks or the location hint.
  - **QP.6:** the Bluetooth tile toggles; paired devices are listed; connecting the user's buds works or falls back to Settings.
  - **QP.7:** the test toast is listed in the panel group; ✕ removes it; "Clear all" works; a click opens the app.
  - Re-run the Launcher, Dock and Reserve sections as regression.
- [ ] **Step 2: Budget.**
  - Use the panel and notch: music playing, two toasts, the panel opened 3 times.
  - Stop the music and leave the PC idle for 10 minutes.
  - Measure with the Plan 1 method.
  - Pass: total working set under 100 MB and CPU under 0.5%. If it fails, profile and raise it with the user before closing the plan.
- [ ] **Step 3: Docs.**
  - README: Win+A/N, the panel, the notch, the banner tip, and the package identity, with uninstall now removing the package.
  - Measurements doc: Phase 3 numbers.
- [ ] **Step 4: Run all tests.** Run `dotnet test NotWin.sln`. Expected: all pass.
- [ ] **Step 5: Commit.** Message: `docs: phase 3 measurements + README`. Ask the user before pushing `plan3-panel-notch` and opening a PR.

## User decisions (2026-11-18, at plan review)

1. Execution: native (main session), one whole-branch review on the most capable model at the end.
2. Windows notification pop-ups: one-time guided tip only (Task 12); Nothing Shell never changes the setting.
3. Notch hover dashboard and timer stay in Plan 4.

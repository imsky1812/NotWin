# Plan 2: Launcher Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the Start menu with Nothing Shell's own launcher. The lone Win key (or the dock's start or search button) opens a centered, search-first panel. It finds apps, settings and files on this PC, and launches them with Enter. This plan also clears the Plan 1 follow-ups the final review assigned to Plan 2.

**Architecture:**
- Pure logic is unit-tested in `NothingShell.Core`: the lone-Win detector, ranking, the settings catalogue, usage history, UserAssist parsing, the most-used pick and the file-query builder.
- Win32/COM interop lives in `NothingShell.Native`: a dedicated low-level-hook thread, the keyboard hook plus mask key, AppsFolder enumeration, power actions and foreground activation.
- The WPF launcher (`src/NothingShell/Launcher/`) is a lazily created, activatable, topmost window over a scrim. `LauncherController` wires it to the hook, the dock buttons, the app index and file search.
- Both low-level hooks (keyboard and mouse) run on their own message-loop thread. This means a busy UI thread can never get them silently removed (Plan 1 final review, I1).

**Tech Stack:**
- .NET 10, WPF and C# 14, as in Plan 1. View models implement `INotifyPropertyChanged` directly, like `DockViewModel`; there is no MVVM toolkit in the repo.
- Microsoft.Windows.CsWin32.
- `System.Data.OleDb` (NuGet, Microsoft, MIT) to query the Windows Search index.
- xUnit.

**Spec:** `docs/superpowers/specs/2026-11-17-nothing-shell-design.md`. Read §2, §3, §5, §6.1, §6.2, §7 (Keys, surfaces, performance rules) and §9 (lone-Win risk). Visual source: `docs/mockups/final-v2.html` (the `.start`, `.search`, `.most`, `.best`, `.ri`, `.sfoot` and `.scrim` rules, lines 114–136 and 198–205). Plan 1 context: `README.md`, `docs/superpowers/spikes/2026-11-17-phase0-findings.md` ("Spike: lone Win key") and `docs/superpowers/spikes/2026-11-17-phase1-measurements.md`.

**Branch:** `plan2-launcher`, created from `plan1-foundation`. PR #1 is not merged yet; rebase onto `main` once it is. Ask the user before pushing.

## Global Constraints

- Windows 11, primary display only. Per-monitor DPI aware; all sizes are in DIPs (spec §2, §7).
- Memory: under **100 MB** total working set at idle after 10 minutes, measured after the launcher has been opened and closed; CPU about 0% at idle (spec §3).
- **If Nothing Shell dies for any reason, the Windows taskbar must be usable again within a few seconds** (spec §2, §7). Plan 1's restore paths must keep working: re-run the Task8 and Reserve hands-on sections at the end.
- **Lone Win key must open our launcher. Win+A and Win+N open our quick-settings panel. Other Win+ combos keep working normally** (spec §3). Plan 2 implements the lone Win key only. Win+A and Win+N pass through to Windows untouched until Plan 3.
- Launcher (spec §6.2):
  - Centered on screen, with a slight scrim. Focus goes straight into the search box.
  - Empty state: "Most used" shows 6 apps ranked by launches from Nothing Shell. Until there is history, it shows the 6 most recently used Start Menu apps. Below that: an "All apps ›" A–Z list and a power button (sleep / restart / shut down).
  - Typing searches this PC only, ranked **apps → settings → files**. The top result is a large "Best match" card, with up to 5 more rows showing their kind. ↑/↓ move, Enter opens, Esc closes. **No web results.**
  - Opening something closes the launcher.
- Fonts: **Geist** for text and **Geist Mono** for small uppercase labels (spec §5). Both are already bundled in `src/NothingShell/Theme/Fonts`.
- Palette only:
  - glass `rgba(16,16,16,0.90)`, panel line `rgba(255,255,255,0.10)`;
  - text `#F2F2F2`, dim `#8A8A8A`;
  - red `#E8231F` as the only accent;
  - mockup neutrals `#262626` (icon circles) and `#666` (placeholder).
  - Panel radius 28 (spec §5).
- Motion 160–320 ms, `cubic-bezier(.2,.8,.2,1)`, fades and slides only (spec §5).
- Performance: no idle timers faster than 1 s. Hidden surfaces are released or made very cheap (spec §7).
- App icons in launcher results keep their original colours, matching the dock (user decision 2026-11-18).
- Do not copy code from `jaxparrow07/nothing-kde-widgets` (GPL-3.0).
- Data folder `%AppData%\NothingShell\`: new file `launcher-usage.json`.
- Never start, kill or message the user's running installed shell from an implementer subagent. Live checks happen in the hands-on session with the user (Task 10). Never weaken `SingleInstance`.

## Review Focus

1. **Focus after the Win key.** When the launcher opens from the Win key over another app (including an elevated one like Task Manager), typed characters must land in the search box, not in the previous app. Covered by Task 6's `ShowAndFocus` with a foreground check plus fallback, logged as `launcher focus ok=<bool>`, and the hands-on check LA.2.
2. **Win-key combinations.** Holding Win and pressing another key, Win+Shift+S, Win+L, Win+D, Win+Tab, double taps, and Win while a key is held must never open the launcher, and Start must never open. Covered by Task 1's `LoneWinDetector` tests `ChordedWin_DoesNotOpen`, `WinAfterOtherKeyHeld_DoesNotOpen`, `DoubleTap_OpensTwice`, `InjectedOwnKeys_AreIgnored` and `AutoRepeatWinDown_StillLone`.
3. **The Windows Search index is unavailable or slow** (service stopped, provider missing, a query taking several seconds). Apps and settings must still appear instantly, the UI must never freeze, and file rows simply don't appear. Covered by Task 5 tests `Search_ProviderThrows_ReturnsEmpty` and `Search_StaleQueryResult_IsDiscarded`, and Task 6's rule that file results arrive asynchronously and are dropped if the query changed.
4. **Apps installed or uninstalled while the shell runs, and usage history that names apps which no longer exist.** Most used must skip missing apps, and the index refreshes on open. Covered by Task 3 test `Pick_IgnoresUsageForMissingApps` and Task 7's refresh-on-open rule (older than 60 s).
5. **Odd queries**: whitespace only, quotes, `%`, `_`, `[`, very long input and non-Latin text. No crash, no SQL injection into the index query, and sensible matches. Covered by Task 2 tests `Rank_WhitespaceQuery_ReturnsEmpty` and `Score_IsCaseAndCultureInsensitive`, and Task 5 tests `Build_EscapesQuotesAndWildcards` and `Build_TruncatesLongQuery`.

---

## File Structure

```
src/NothingShell.Core/Launcher/
  LoneWinDetector.cs        lone-Win state machine (pure)
  LaunchItem.cs             LaunchItem, LaunchKind
  SearchRanker.cs           scoring + ordering (apps → settings → files)
  SettingsPages.cs          fixed ms-settings catalogue with keywords
  LaunchUsageStore.cs       launcher-usage.json (counts + last launch)
  UserAssistParser.cs       ROT13 + binary parse of UserAssist values
  MostUsedPicker.cs         empty-state 6 apps
  FileQuery.cs              Windows Search SQL builder + escaping
src/NothingShell.Native/
  HookThread.cs             dedicated message-loop thread for LL hooks
  KeyboardHookNative.cs     WH_KEYBOARD_LL + mask-key injection
  MouseHookNative.cs        (modify) install on HookThread
  AppsFolderNative.cs       enumerate shell:AppsFolder
  PowerNative.cs            sleep / restart / shut down
  ForegroundNative.cs       reliable foreground activation
src/NothingShell/Launcher/
  WinKeyListener.cs         hook thread → UI thread "toggle launcher"
  AppIndex.cs               apps + settings index, refresh, usage, recent
  FileSearchService.cs      OleDb Windows Search + Recent items
  LauncherViewModel.cs      query, results, selection, views
  LauncherWindow.xaml(.cs)  scrim + panel UI
  LauncherController.cs     open/close/toggle, launch, events
tests/NothingShell.Core.Tests/Launcher/*Tests.cs
build/verify/plan1-handson.ps1   (modify) add "Launcher" section
docs/superpowers/spikes/2026-11-17-phase1-measurements.md (modify) add phase 2 numbers
README.md                   (modify) launcher usage
```

---

### Task 1: Lone Win key on a dedicated hook thread

**Files:**
- Create: `src/NothingShell.Core/Launcher/LoneWinDetector.cs`, `src/NothingShell.Native/HookThread.cs`, `src/NothingShell.Native/KeyboardHookNative.cs`, `src/NothingShell/Launcher/WinKeyListener.cs`
- Modify: `src/NothingShell.Native/NativeMethods.txt` (add `GetMessage`, `TranslateMessage`, `DispatchMessage`, `PostThreadMessage`, `GetCurrentThreadId`, `KBDLLHOOKSTRUCT`, `PeekMessage`), `src/NothingShell/App.xaml.cs`
- Test: `tests/NothingShell.Core.Tests/Launcher/LoneWinDetectorTests.cs`

**Interfaces:**
- Produces:
  - `public readonly record struct KeyVerdict(bool Swallow, bool InjectMaskFirst, bool OpenLauncher)`
  - `public sealed class LoneWinDetector { public KeyVerdict OnKey(uint vk, bool down, bool injectedByUs); }`
  - `public sealed class HookThread : IDisposable { public HookThread(string name); public void Invoke(Action action); public uint ThreadId { get; } }`. `Invoke` runs the action on the hook thread and blocks until it is done. It is used to install and uninstall hooks.
  - `public sealed class KeyboardHookNative.Hook : IDisposable { public Hook(HookThread thread, KeyCallback cb); }`, with `public delegate bool KeyCallback(uint vk, bool down, bool injectedByUs)`. Returning true swallows the key.
  - `public static void KeyboardHookNative.InjectMaskKey()`: sends VK `0xE8` down and up with `dwExtraInfo = KeyboardHookNative.OurMarker`.
  - `public sealed class WinKeyListener : IDisposable { public event Action? LauncherRequested; }`. The event is raised on the UI thread.

- [ ] **Step 1: Confirm the branch.** `plan2-launcher` was created from `plan1-foundation` together with this plan. Run `git branch --show-current`. Expected: `plan2-launcher`.
- [ ] **Step 2: Write the failing tests** in `LoneWinDetectorTests.cs`. Use VK constants LWin `0x5B`, RWin `0x5C`, A `0x41` and LShift `0xA0`.
  - `LoneTap_OpensLauncher`: down(LWin) then up(LWin). The keydown verdict is `(false,false,false)`; the keyup verdict is `(Swallow:false, InjectMaskFirst:true, OpenLauncher:true)`.
  - `RightWin_AlsoOpens`: the same with RWin.
  - `ChordedWin_DoesNotOpen`: down(LWin), down(A), up(A), up(LWin). Every verdict is `(false,false,false)`.
  - `ShiftThenWin_DoesNotOpen`: down(LShift), down(LWin), up(LWin), up(LShift). The LWin keyup verdict is `(false,false,false)`.
  - `WinAfterOtherKeyHeld_DoesNotOpen`: down(A), down(LWin), up(LWin). It does not open.
  - `DoubleTap_OpensTwice`: two lone taps open twice.
  - `AutoRepeatWinDown_StillLone`: down(LWin) three times, then up(LWin). It opens.
  - `InjectedOwnKeys_AreIgnored`: down(LWin), then down(0xE8, injectedByUs:true), up(0xE8, true), then up(LWin). It still opens. Our own 0xE8 events return `(false,false,false)`.
- [ ] **Step 3: Run the tests and confirm they fail.** Run `dotnet test tests/NothingShell.Core.Tests --filter LoneWinDetector`. Expected: build error, because `LoneWinDetector` is not defined.
- [ ] **Step 4: Implement `LoneWinDetector`.** It tracks the set of non-Win keys currently held and a `chorded` flag. Any non-Win keydown while Win is held sets `chorded`. Win keydown while another key is already held also sets `chorded`. Events with `injectedByUs` are ignored. Plan 2 never swallows; `Swallow` exists for Plan 3's Win+A/N.
- [ ] **Step 5: Run the tests and confirm they pass.** Run `dotnet test tests/NothingShell.Core.Tests --filter LoneWinDetector`. Expected: 8 passed.
- [ ] **Step 6: Implement the native pieces.**
  - `HookThread`: a background thread that runs a `GetMessage` loop and executes queued actions on a private thread message (`WM_APP+1`). `Dispose` posts `WM_QUIT` and joins with a 2 s timeout.
  - `KeyboardHookNative.Hook`: an `[UnmanagedCallersOnly]` static callback and a static slot, the same pattern as `MouseHookNative`. `injectedByUs = (flags & LLKHF_INJECTED) != 0 && dwExtraInfo == OurMarker`.
  - The callback must stay tiny: run the detector, call `InjectMaskKey()` when `InjectMaskFirst` is set, then `CallNextHookEx`.
  - `OurMarker` is `0x4E534831` ("NSH1").
- [ ] **Step 7: Implement `WinKeyListener`.** It owns a `HookThread("NothingShell.Hooks")`, a `LoneWinDetector` and the keyboard hook. When `OpenLauncher` is set, it does `Dispatcher.BeginInvoke(LauncherRequested)`. Log `win key hook installed` and `lone win -> launcher`, throttled to 1 line per 5 s.
- [ ] **Step 8: Wire it in `App.xaml.cs`.** Create the listener after the dock. Until Task 7, `LauncherRequested` only logs. Dispose it in `OnExit` and `RestoreShell`.
- [ ] **Step 9: Build and test.** Run `dotnet build NotWin.sln -c Release` and `dotnet test NotWin.sln`. Expected: build clean and all tests pass.
- [ ] **Step 10: Commit.** Message: `feat: lone Win key on a dedicated low-level hook thread`.

### Task 2: Launch items, ranking and the settings catalogue

**Files:**
- Create: `src/NothingShell.Core/Launcher/LaunchItem.cs`, `SearchRanker.cs`, `SettingsPages.cs`
- Test: `tests/NothingShell.Core.Tests/Launcher/SearchRankerTests.cs`, `SettingsPagesTests.cs`

**Interfaces:**
- Produces:
  - `public enum LaunchKind { App, Setting, File, Folder }`
  - `public sealed record LaunchItem(string Id, string Name, LaunchKind Kind, string Target, IReadOnlyList<string> Keywords)`. `Id` is the app's AppsFolder parsing name, the settings URI, or the file's full path. `Target` is what `ShellExecute` opens: `shell:AppsFolder\` + Id for apps, the URI for settings, or the path for files.
  - `public sealed record LaunchUsage(string Id, int Count, DateTimeOffset LastLaunched)`
  - `public static class SearchRanker`, with `public const int MaxResults = 6;`, `public static int Score(string query, LaunchItem item)`, and `public static IReadOnlyList<LaunchItem> Rank(string query, IEnumerable<LaunchItem> items, IReadOnlyDictionary<string, LaunchUsage> usage, int max = MaxResults)`
  - `public static class SettingsPages { public static IReadOnlyList<LaunchItem> All { get; } }`

- [ ] **Step 1: Write the failing tests.** Score values are 100 for an exact name match, 90 when the name starts with the query, 75 when a word starts with it, 65 for initials, 60 for a keyword prefix and 50 for a substring. 0 means no match.
  - `Score_Exact_Is100`: "notepad" against Notepad gives 100.
  - `Score_Prefix_Is90`: "note" against Notepad gives 90.
  - `Score_WordStart_Is75`: "code" against "Visual Studio Code" gives 75. Words split on space, `-`, `_` and `.`.
  - `Score_Initials_Is65`: "vsc" against "Visual Studio Code" gives 65. Initials need at least 2 query chars.
  - `Score_KeywordPrefix_Is60`: "bright" against Display, which has the keyword "brightness", gives 60.
  - `Score_Substring_Is50`: "pad" against Notepad gives 50.
  - `Score_NoMatch_Is0`.
  - `Score_IsCaseAndCultureInsensitive`: "NOTE" matches Notepad. "ı" (dotless i) does not throw. "café" matches "Café Notes".
  - `Rank_OrdersAppsThenSettingsThenFiles`: "sound" against app "SoundHound", setting "Sound" and file "sound.txt" gives App, Setting, File, in that order.
  - `Rank_WithinKind_ScoreThenUsageThenName`: two prefix-matching apps; the one with the higher usage count comes first. With equal usage, the alphabetical one comes first.
  - `Rank_CapsAtMax`: 20 matches give 6 results.
  - `Rank_WhitespaceQuery_ReturnsEmpty`: "   " gives empty.
  - `Rank_FoldersRankWithFiles`: Folder and File share one group, ordered by score.
  - In `SettingsPagesTests`:
    - `All_HasUniqueIdsAndMsSettingsUris`: every Id starts with `ms-settings:` and is unique.
    - `All_ContainsCorePages`: Display, Sound, Bluetooth & devices, Wi-Fi, Notifications and Windows Update are all present.
- [ ] **Step 2: Run the tests and confirm they fail.** Run `dotnet test tests/NothingShell.Core.Tests --filter "SearchRanker|SettingsPages"`. Expected: build errors.
- [ ] **Step 3: Implement the types.** Normalise with `ToLowerInvariant` after `Trim`, and strip diacritics via `NormalizationForm.FormD`, dropping `NonSpacingMark`. `SettingsPages.All` lists these pages, name → URI, with keywords:
  - Display `ms-settings:display` (brightness, resolution, scale, monitor)
  - Night light `ms-settings:nightlight`
  - Sound `ms-settings:sound` (volume, speakers, microphone, audio)
  - Notifications `ms-settings:notifications`
  - Focus `ms-settings:focus`
  - Power & battery `ms-settings:powersleep` (sleep, battery)
  - Storage `ms-settings:storagesense` (disk, space)
  - Clipboard `ms-settings:clipboard`
  - About `ms-settings:about` (pc name, specs)
  - Bluetooth & devices `ms-settings:bluetooth` (headphones, buds)
  - Printers & scanners `ms-settings:printers`
  - Mouse `ms-settings:mousetouchpad` (touchpad, cursor)
  - Wi-Fi `ms-settings:network-wifi` (wireless, internet)
  - Network & internet `ms-settings:network` (ethernet)
  - VPN `ms-settings:network-vpn`
  - Mobile hotspot `ms-settings:network-mobilehotspot`
  - Airplane mode `ms-settings:network-airplanemode`
  - Personalization `ms-settings:personalization`
  - Background `ms-settings:personalization-background` (wallpaper)
  - Colors `ms-settings:colors` (dark mode, theme)
  - Lock screen `ms-settings:lockscreen`
  - Installed apps `ms-settings:appsfeatures` (uninstall, programs)
  - Default apps `ms-settings:defaultapps` (browser)
  - Startup apps `ms-settings:startupapps`
  - Your info `ms-settings:yourinfo` (account)
  - Sign-in options `ms-settings:signinoptions` (password, pin, windows hello)
  - Date & time `ms-settings:dateandtime` (clock, time zone)
  - Language & region `ms-settings:regionlanguage` (keyboard)
  - Gaming `ms-settings:gaming-gamemode` (game mode)
  - Accessibility `ms-settings:easeofaccess`
  - Privacy & security `ms-settings:privacy` (permissions)
  - Windows Update `ms-settings:windowsupdate` (update)
- [ ] **Step 4: Run the tests and confirm they pass.** Same command as Step 2. Expected: all pass.
- [ ] **Step 5: Commit.** Message: `feat: launcher ranking and settings catalogue`.

### Task 3: Usage history, UserAssist and the Most used pick

**Files:**
- Create: `src/NothingShell.Core/Launcher/LaunchUsageStore.cs`, `UserAssistParser.cs`, `MostUsedPicker.cs`
- Test: `tests/NothingShell.Core.Tests/Launcher/LaunchUsageStoreTests.cs`, `UserAssistParserTests.cs`, `MostUsedPickerTests.cs`

**Interfaces:**
- Consumes: `LaunchItem` and `LaunchUsage` (Task 2), and the JSON helper pattern used by `SettingsStore` (atomic write via temp file plus replace).
- Produces:
  - `public sealed class LaunchUsageStore { public LaunchUsageStore(string dir); public IReadOnlyDictionary<string, LaunchUsage> Load(); public void Record(string id, DateTimeOffset now); }`. The file is `launcher-usage.json`. It is capped at 500 entries; least recently launched entries are dropped first.
  - `public readonly record struct UserAssistEntry(string Name, int RunCount, DateTimeOffset? LastRun)`
  - `public static class UserAssistParser { public static string Rot13(string s); public static UserAssistEntry? Parse(string valueName, byte[] data); }`. The value name is ROT13-decoded. RunCount is the int32 at offset 4. LastRun is the FILETIME at offset 60. Data shorter than 68 bytes returns null, and a FILETIME of 0 gives a null LastRun.
  - `public static class MostUsedPicker { public static IReadOnlyList<LaunchItem> Pick(IReadOnlyList<LaunchItem> apps, IReadOnlyDictionary<string, LaunchUsage> usage, IReadOnlyList<UserAssistEntry> recent, int count = 6); }`

- [ ] **Step 1: Write the failing tests.**
  - `Record_IncrementsCountAndSetsLast`.
  - `Record_PersistsAcrossInstances`, using a temp dir.
  - `Load_CorruptFile_ReturnsEmpty`.
  - `Record_CapsAt500DroppingOldest`.
  - `Rot13_RoundTrips`: `"{1NP14R77-02R7-4R5Q-O744-2RO1NR5198O7}\abgrcnq.rkr"` decodes to `"{1AC14E77-02E7-4E5D-B744-2EB1AE5198B7}\notepad.exe"`.
  - `Parse_ReadsCountAndFiletime`: build a 72-byte array with count 7 at offset 4 and FILETIME `133000000000000000` at offset 60.
  - `Parse_ShortData_ReturnsNull`.
  - `Pick_UsesLaunchCountsFirst`: 3 apps with usage, ordered by count desc, then last launch desc.
  - `Pick_FillsFromUserAssistByLastRun`: no usage. Entries map to app Ids, ordered by LastRun desc, with a case-insensitive Id match.
  - `Pick_IgnoresUsageForMissingApps`: usage for an Id that is not in `apps` is skipped.
  - `Pick_FillsRemainingAlphabetically`: the result has exactly 6 items when at least 6 apps exist.
  - `Pick_NoDuplicates`.
- [ ] **Step 2: Run the tests and confirm they fail.** Run `dotnet test tests/NothingShell.Core.Tests --filter "LaunchUsage|UserAssist|MostUsed"`. Expected: build errors.
- [ ] **Step 3: Implement the three types.** Match UserAssist names to apps by an exact, case-insensitive Id match. AppsFolder parsing names for desktop apps use the same known-folder-GUID form, for example `{1AC14E77-...}\notepad.exe`. Packaged apps use their AUMID.
- [ ] **Step 4: Run the tests and confirm they pass.** Same command as Step 2. Expected: all pass.
- [ ] **Step 5: Commit.** Message: `feat: launcher usage history and most-used pick`.

### Task 4: App index (AppsFolder, UserAssist, power, launching)

**Files:**
- Create: `src/NothingShell.Native/AppsFolderNative.cs`, `src/NothingShell.Native/PowerNative.cs`, `src/NothingShell/Launcher/AppIndex.cs`
- Modify: `src/NothingShell.Native/NativeMethods.txt` (add `IEnumShellItems`, `IShellItem`, `SIGDN`, `BHID_EnumItems`, `SetSuspendState`, `ExitWindowsEx`, `AdjustTokenPrivileges`, `LookupPrivilegeValue`, `OpenProcessToken`)
- Test: `tests/NothingShell.Core.Tests/Launcher/AppIndexRulesTests.cs`. It covers the pure filter in `AppIndexRules`, which goes in `src/NothingShell.Core/Launcher/AppIndexRules.cs`.

**Interfaces:**
- Consumes: `LaunchItem`, `SettingsPages` (Task 2), `LaunchUsageStore`, `UserAssistParser`, `MostUsedPicker` (Task 3), and `ShellLinkNative`'s ShellExecuteEx launch from Plan 1.
- Produces:
  - `public readonly record struct AppsFolderEntry(string ParsingName, string DisplayName)`
  - `public static IReadOnlyList<AppsFolderEntry> AppsFolderNative.Enumerate()`. Call it on an STA thread. It returns an empty list on failure and logs the HRESULT.
  - `public static class AppIndexRules { public static bool IsLaunchable(AppsFolderEntry e); }`. It drops names that start with "Uninstall", entries whose parsing name ends with `.url`, `.chm`, `.txt` or `.pdf`, and the case-insensitive names "Readme", "Help" and "Release notes".
  - `public static class PowerNative { public static bool Sleep(); public static bool Restart(); public static bool ShutDown(); }`. Restart and shut down enable `SeShutdownPrivilege`, then call `ExitWindowsEx(EWX_REBOOT|EWX_SHUTDOWN|EWX_POWEROFF, SHTDN_REASON_FLAG_PLANNED)`.
  - `public sealed class AppIndex`, with these members:
    - `public IReadOnlyList<LaunchItem> Apps { get; }`
    - `public IReadOnlyList<LaunchItem> All { get; }` (apps plus settings)
    - `public Task RefreshIfStaleAsync(TimeSpan maxAge)`, which enumerates on a dedicated STA thread
    - `public IReadOnlyList<LaunchItem> MostUsed()`
    - `public bool Launch(LaunchItem item)`, which records usage and calls ShellExecuteEx, setting `lpDirectory` to the target's folder for file paths
    - `public LaunchItem? FindApp(string idOrExePath)`, used by Task 9 for pin names

- [ ] **Step 1: Write the failing tests** in `AppIndexRulesTests`: `IsLaunchable_DropsUninstallers`, `IsLaunchable_DropsDocsAndUrls`, `IsLaunchable_KeepsNormalApps` (Notepad, `Microsoft.WindowsCalculator_8wekyb3d8bbwe!App`).
- [ ] **Step 2: Run the tests and confirm they fail.** Run `dotnet test tests/NothingShell.Core.Tests --filter AppIndexRules`. Expected: build error.
- [ ] **Step 3: Implement `AppIndexRules`, `AppsFolderNative` and `PowerNative`.**
  - `AppsFolderNative`: `SHCreateItemFromParsingName("shell:AppsFolder")`, then `BindToHandler(BHID_EnumItems)`, then the `IEnumShellItems` loop. For each item, read `SIGDN_PARSINGRELATIVETOPARENT` as ParsingName and `SIGDN_NORMALDISPLAY` as DisplayName. Release every COM pointer in `finally`.
  - `PowerNative.Sleep`: `SetSuspendState(false, false, false)`.
- [ ] **Step 4: Implement `AppIndex`.**
  - Items: apps come from `Enumerate` filtered by `IsLaunchable`, deduplicated by DisplayName (the first wins), with `Target = "shell:AppsFolder\\" + ParsingName`. Settings come from `SettingsPages.All`.
  - Recent entries: read the values of `HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\UserAssist\{CEBFF5CD-ACE2-4F4F-9178-9926F41749EA}\Count` via `Microsoft.Win32.Registry` and parse them with `UserAssistParser`.
  - The first refresh runs at startup on a background STA thread. The UI never waits for it; the launcher shows whatever index exists.
  - Logging: `app index: N apps (M ms)`, and `launch <kind> "<name>" ok=<bool>`. The launch line logs the name only, never a file path.
- [ ] **Step 5: Run the tests and confirm they pass.** Run `dotnet test NotWin.sln`. Expected: all pass. Also confirm the Release build is clean.
- [ ] **Step 6: Commit.** Message: `feat: launcher app index, power actions`.

### Task 5: File search (Windows Search index + Recent items)

**Files:**
- Create: `src/NothingShell.Core/Launcher/FileQuery.cs`, `src/NothingShell.Core/Launcher/FileSearchRules.cs`, `src/NothingShell/Launcher/FileSearchService.cs`
- Create: `tests/NothingShell.Tests/NothingShell.Tests.csproj`. It is an xUnit project that targets `net10.0-windows10.0.22621.0` with `UseWPF=true` and references `src/NothingShell` and `src/NothingShell.Core`. Add it to `NotWin.sln`. It holds tests for app-layer classes that take injected dependencies; it must never start the shell, a window or a hook.
- Modify: `src/NothingShell/NothingShell.csproj`, to add `PackageReference System.Data.OleDb`. Use the latest stable 10.x version and record the exact version in the commit.
- Test: `tests/NothingShell.Core.Tests/Launcher/FileQueryTests.cs`, `FileSearchRulesTests.cs`, `tests/NothingShell.Tests/FileSearchServiceTests.cs`

**Interfaces:**
- Consumes: `LaunchItem`, `LaunchKind` (Task 2).
- Produces:
  - `public static class FileQuery { public const int MaxQueryLength = 64; public static string? Build(string query, int top = 20); public static string EscapeLiteral(string s); }`. `Build` returns null for empty or whitespace queries. The query is `SELECT TOP {top} System.ItemPathDisplay, System.ItemNameDisplay, System.ItemType FROM SystemIndex WHERE SCOPE='file:' AND System.ItemNameDisplay LIKE '%{escaped}%' AND System.ItemType <> '.lnk' ORDER BY System.DateModified DESC`.
  - `public static class FileSearchRules { public static IReadOnlyList<LaunchItem> Merge(IReadOnlyList<LaunchItem> recent, IReadOnlyList<LaunchItem> indexed, int max); }`. It deduplicates by path, case-insensitively. Recent items come first, then indexed ones.
  - `public sealed class FileSearchService { public FileSearchService(Func<string, CancellationToken, IReadOnlyList<LaunchItem>>? indexQuery = null); public Task<IReadOnlyList<LaunchItem>> SearchAsync(string query, CancellationToken ct); }`. A null `indexQuery` means the real OleDb runner; tests inject one. It never throws; on any error it returns empty and logs once per minute. It runs the OleDb query on a background thread with a 2 s command timeout. Directories become `LaunchKind.Folder`.
  - Recent items: `.lnk` files in `%AppData%\Microsoft\Windows\Recent` whose names contain the query, resolved via `ShellLinkNative.Read`, keeping only existing targets.

- [ ] **Step 1: Write the failing tests.**
  - `Build_EmptyOrWhitespace_ReturnsNull`.
  - `Build_EscapesQuotesAndWildcards`: `o'brien` produces `o''brien`, `50%` produces `50[%]`, `a_b` produces `a[_]b` and `[x]` produces `[[]x]`.
  - `Build_TruncatesLongQuery`: 200 chars are cut to 64.
  - `Build_ContainsScopeAndExcludesLnk`.
  - `Merge_RecentFirstAndDeduped`.
  - `Merge_CapsAtMax`.
  - In `tests/NothingShell.Tests/FileSearchServiceTests.cs`, using an injected `indexQuery`:
    - `Search_ProviderThrows_ReturnsEmpty`;
    - `Search_StaleQueryResult_IsDiscarded`: a cancelled token yields empty, not results.
- [ ] **Step 2: Run the tests and confirm they fail.** Run `dotnet test NotWin.sln --filter "FileQuery|FileSearch"`. Expected: build errors.
- [ ] **Step 3: Implement it.** The connection string is `Provider=Search.CollatorDSO;Extended Properties='Application=Windows';`. Load `System.Data.OleDb` lazily, on the first file search only, to keep idle memory low.
- [ ] **Step 4: Run the tests and confirm they pass.** Same command as Step 2. Expected: all pass.
- [ ] **Step 5: Commit.** Message: `feat: launcher file search via the Windows Search index`.

### Task 6: Launcher window and view model

**Files:**
- Create: `src/NothingShell/Launcher/LauncherViewModel.cs`, `LauncherWindow.xaml`, `LauncherWindow.xaml.cs`, `src/NothingShell.Native/ForegroundNative.cs`
- Modify: `src/NothingShell.Native/NativeMethods.txt` (add `AttachThreadInput`, `BringWindowToTop`, `SetFocus`), `src/NothingShell/Theme/Tokens.xaml` (add `Brush.Scrim` `#47000000`, `Brush.Field` `#0FFFFFFF`, `Brush.Card` `#12FFFFFF`, `Brush.RowSel` `#17FFFFFF`, `Brush.Circle` `#262626`, `Brush.Placeholder` `#666666`)
- Test: `tests/NothingShell.Tests/LauncherViewModelTests.cs`

**Interfaces:**
- Consumes: `AppIndex` (Task 4), `FileSearchService` (Task 5), `SearchRanker` (Task 2) and `AppIconCache` from Plan 1.
- Produces:
  - `public enum LauncherView { Home, Results, AllApps }`
  - `public sealed class LauncherViewModel : INotifyPropertyChanged`. It implements the interface directly, like `DockViewModel`; there is no MVVM toolkit in this repo. Constructor: `LauncherViewModel(Func<IReadOnlyList<LaunchItem>> searchable, Func<IReadOnlyList<LaunchItem>> mostUsed, Func<IReadOnlyList<LaunchItem>> allApps, Func<string, CancellationToken, Task<IReadOnlyList<LaunchItem>>> fileSearch, TimeSpan fileDebounce)`. The app passes 150 ms for `fileDebounce`; tests pass `TimeSpan.Zero`. Members:
    - `string Query`
    - `LauncherView View`
    - `IReadOnlyList<LaunchItem> MostUsed`
    - `IReadOnlyList<LaunchItem> Results`. Index 0 is the best match.
    - `IReadOnlyList<LaunchItem> AllApps` (A–Z)
    - `int Selected`
    - `void MoveSelection(int delta)`. It clamps and does not wrap.
    - `LaunchItem? SelectedItem`
    - `void Reset()`
    - `event Action<LaunchItem>? LaunchRequested`
    - `void ActivateSelected()`
  - `public sealed class LauncherWindow : Window { public void ShowAndFocus(); public void HideNow(); public event Action? Dismissed; }`
  - `public static bool ForegroundNative.Activate(nint hwnd)`. It tries `SetForegroundWindow`. If the foreground window is still not ours, it calls `AttachThreadInput` to the foreground thread, then `BringWindowToTop` + `SetForegroundWindow` + `SetFocus`, then detaches. It returns whether we became foreground.

- [ ] **Step 1: Write the failing view-model tests.**
  - `EmptyQuery_ShowsHome`.
  - `Typing_ShowsResultsRankedAndSelectsFirst`.
  - `MoveSelection_ClampsWithinResults`.
  - `FileResults_ArriveLater_AppendAfterAppsAndSettings_KeepSelectionOnSameItem`.
  - `FileResults_ForOldQuery_AreDropped`.
  - `Enter_RaisesLaunchRequestedForSelected`.
  - `AllApps_IsAlphabeticalAndNavigable`.
  - `Reset_ClearsQueryAndSelection`.
  - Inject `Func<string, CancellationToken, Task<IReadOnlyList<LaunchItem>>>` for file search and a fixed item list for apps.
- [ ] **Step 2: Run the tests and confirm they fail.** Run `dotnet test NotWin.sln --filter LauncherViewModel`. Expected: build errors.
- [ ] **Step 3: Implement `LauncherViewModel`.**
  - Apps and settings are ranked synchronously on every keystroke with `SearchRanker.Rank` over `AppIndex.All`.
  - Files are fetched after a 150 ms debounce, with cancellation on each keystroke. They are merged into the files group, and the list stays capped at 6.
  - After a merge, the selection stays on the same item if it is still present; otherwise it goes to 0.
- [ ] **Step 4: Build `LauncherWindow` from the mockup.**
  - **Window:** one borderless, transparent, topmost, `ShowInTaskbar=False` window covering the primary monitor bounds.
    - Background: the scrim brush, faded in over 180 ms.
    - A click on the scrim closes the window.
  - **Panel:** 620 wide, 150 from the top of the monitor, centered horizontally.
    - Padding 16, corner radius 28.
    - Background: the glass brush, with a 1 px panel-line border.
    - Opens with a fade plus an 8 px upward slide over 200 ms, `cubic-bezier(.2,.8,.2,1)`.
  - **Search field:**
    - Background `Brush.Field`, radius 18, padding 14/16, margin-bottom 16.
    - An 18 px magnifier stroke icon in `#AAAAAA`.
    - The TextBox uses Geist 18 px with no border.
    - Placeholder: "Search apps, settings, files" in `#666`.
    - An "ESC" label on the right.
  - **Section labels** ("MOST USED", "BEST MATCH", "ALL APPS"):
    - Geist Mono 10 px, dim, uppercase, letter-spacing 1 (≈0.1 em), margin-bottom 10.
  - **Most used:**
    - A 6-column UniformGrid with 4 px gaps.
    - Each tile has 10 px vertical padding, radius 14, and a hover fill of `#0FFFFFFF`.
    - Each tile shows a 48 px `#262626` circle holding the app icon at 28 px, then the name at 11 px in `#CCCCCC`, 7 px below.
  - **Best match card:**
    - Background `Brush.Card`, radius 18, padding 14, gap 16, margin-bottom 10.
    - When selected, it gets an inner 1.5 px `#F2F2F2` outline.
    - A 58 px circle holds the icon at 36 px.
    - The name is 19 px SemiBold.
    - Under the name, a Geist Mono 9 px label reads "<KIND> · ENTER TO OPEN".
  - **Result rows:**
    - Padding 9/10, radius 12, 14 px text, gap 12.
    - A 32 px circle holds the icon at 20 px.
    - The kind label sits on the right.
    - The selected row's background is `Brush.RowSel`.
  - **Footer:**
    - A 1 px panel-line border on top, with padding-top 12 and margin-top 12.
    - Left: "↑↓ MOVE · ENTER OPEN · NO WEB RESULTS".
    - Right: "ALL APPS ›", plus a 34 px `#262626` round power button.
    - The power button opens a small DockPopup-style menu with Sleep, Restart and Shut down.
  - **Icons:**
    - Apps, files and folders: real icons via `AppIconCache` (original colours).
    - Settings: a monochrome gear stroke icon.
    - Load icons only for visible items.
  - **Keys:** Up/Down moves the selection, Enter launches, Esc goes back from All apps or closes from Home or Results, and Tab is ignored.
  - **Closing:** the window closes when it is deactivated, for example by Alt+Tab or a click elsewhere.
  - **`ShowAndFocus`:**
    1. Position on the current primary monitor.
    2. `Show()`, then `ForegroundNative.Activate`, then `SearchBox.Focus()` + `Keyboard.Focus`.
    3. Log `launcher focus ok=<bool>`.
  - **`HideNow`:** calls `Hide()` with no exit animation, then `vm.Reset()`, and drops the per-open icon `ImageSource` references.
- [ ] **Step 5: Run the tests and confirm they pass.** Run `dotnet test NotWin.sln`. Expected: all pass, and the Release build is clean.
- [ ] **Step 6: Commit.** Message: `feat: launcher window and view model`.

### Task 7: Launcher controller and integration

**Files:**
- Create: `src/NothingShell/Launcher/LauncherController.cs`
- Modify: `src/NothingShell/App.xaml.cs`, `src/NothingShell/Dock/DockWindow.xaml(.cs)` (add left-click events on start and search), `src/NothingShell/Dock/DockController.cs`

**Interfaces:**
- Consumes: `WinKeyListener.LauncherRequested` (Task 1), `AppIndex` (Task 4), `FileSearchService` (Task 5), `LauncherWindow` and `LauncherViewModel` (Task 6).
- Produces:
  - `public sealed class LauncherController : IDisposable`, with these members:
    - `public void Toggle()`
    - `public void Open()`
    - `public void Close()`
    - `public bool IsOpen { get; }`
    - `public event Action<string>? Launched`, which carries the display name. Plan 3's notch subscribes to it for "Opening ‹name›…".
  - `DockWindow`: `public event Action? StartClicked; public event Action? SearchClicked;`

- [ ] **Step 1: Write the controller.**
  - **Window creation:** create the window lazily on the first `Open`. If the open fails, log the exception and leave `IsOpen=false`.
  - **`Open`:** fire-and-forget `AppIndex.RefreshIfStaleAsync(60 s)`, then `ShowAndFocus`. While the launcher is open, set `DockReveal.SetPanelOpen(true)` so the dock stays visible; clear it on close.
  - **`LaunchRequested`:** `Close()` first, then `AppIndex.Launch(item)`, then raise `Launched(item.Name)`.
  - **Power items:** use `PowerNative`. `Close()` runs before calling it.
  - **`Toggle`:** used by the Win key and by both dock buttons.
- [ ] **Step 2: Wire it up.**
  - In `App.xaml.cs`, create `AppIndex`, `FileSearchService`, `LauncherController`, then `WinKeyListener.LauncherRequested += controller.Toggle`.
  - In `DockController`, `StartClicked` and `SearchClicked` call `Toggle`. The start button keeps its right-click menu.
  - In `DockWindow.xaml`, add `MouseLeftButtonUp` handlers to `StartButton` and `SearchButton`, and remove the "Plan 2 binds both" comment.
- [ ] **Step 3: Release memory.** Dispose the controller in `OnExit`. After 5 minutes closed, call `GC.Collect(1)` once via a one-shot `DispatcherTimer` started on `Close`; Plan 1 rule: no idle timers faster than 1 s.
- [ ] **Step 4: Build and test.** Run `dotnet build NotWin.sln -c Release` and `dotnet test NotWin.sln`. Expected: clean and green.
- [ ] **Step 5: Commit.** Message: `feat: open the launcher from the Win key and the dock`.

### Task 8: Plan 1 follow-ups, input and robustness

**Files:**
- Modify: `src/NothingShell.Native/MouseHookNative.cs`, `src/NothingShell/Shell/MouseEdgeHook.cs`, `src/NothingShell/Shell/FullscreenMonitor.cs`, `src/NothingShell/Dock/DockController.cs`, `src/NothingShell/Dock/DockPopup.xaml.cs`, `src/NothingShell/Dock/AppIconCache.cs`
- Create: `src/NothingShell.Core/FullscreenProbeThrottle.cs`
- Test: `tests/NothingShell.Core.Tests/FullscreenProbeThrottleTests.cs`, `tests/NothingShell.Core.Tests/FullscreenRuleTests.cs` (add)

**Interfaces:**
- Consumes: `HookThread` (Task 1).
- Produces:
  - `MouseHookNative.Hook(HookThread thread, MoveCallback cb)`. The mouse hook now lives on the shared hook thread. `MouseEdgeHook` marshals moves to the UI thread with its existing 60 Hz throttle, and `Reinstall()` reinstalls on the hook thread.
  - `public sealed class FullscreenProbeThrottle { public FullscreenProbeThrottle(TimeSpan min); public bool ShouldProbe(DateTimeOffset now); }`, with min 250 ms. A trailing probe is scheduled when a probe is skipped.
  - `FullscreenRule.Evaluate(..., bool? previous)`: a null foreground probe keeps `previous` instead of reporting "not full screen".

- [ ] **Step 1: Write the failing tests.**
  - `ShouldProbe_AllowsFirst_ThenThrottlesWithin250ms`.
  - `ShouldProbe_AllowsAfterInterval`.
  - `NullForeground_KeepsPreviousState`, in `FullscreenRuleTests`.
- [ ] **Step 2: Run the tests and confirm they fail.** Run `dotnet test tests/NothingShell.Core.Tests --filter "FullscreenProbeThrottle|NullForeground"`. Expected: build errors or failures.
- [ ] **Step 3: Implement.**
  - Move the mouse hook onto `HookThread`, sharing the same instance as `WinKeyListener` (pass it in from `App`).
  - Use the probe throttle and the null-foreground rule in `FullscreenMonitor`.
  - Close any open dock popup and the launcher when full screen starts. The launcher has its own close via `LauncherController.Close`.
  - Clicking the ⌃ overflow button closes an open dock popup first.
  - `AppIconCache`: add a negative cache. A key that failed to load is not retried for 5 minutes; keep at most 256 negative entries.
- [ ] **Step 4: Run the tests and confirm they pass.** Run `dotnet test NotWin.sln`. Expected: all pass, and the Release build is clean.
- [ ] **Step 5: Commit.** Message: `fix: hooks on their own thread, throttled full-screen probe, icon negative cache`.

### Task 9: Plan 1 follow-ups, data and lifecycle

**Files:**
- Modify: the shared `JsonFile.WriteAtomic` helper in `src/NothingShell.Core`, used by `TaskbarRestoreStore` (in `TaskbarRestoreState.cs`) and `SettingsStore`: `Flush(true)` on the temp file before the replace. Also modify `src/NothingShell.Core/SettingsStore.cs`, `src/NothingShell/Shell/WatchdogLauncher.cs`, `src/NothingShell/Shell/TaskbarController.cs`, `src/NothingShell.Native/ShellLinkNative.cs`, `src/NothingShell/Dock/PinnedAppsImporter.cs`, `src/NothingShell/Dock/DockController.cs`, `src/NothingShell/App.xaml.cs`, `src/NothingShell/SingleInstance.cs`, `build/verify/plan1-handson.ps1` (Quit-App)
- Test: `tests/NothingShell.Core.Tests/SettingsStoreTests.cs` (add), `TaskbarSessionTests.cs` (add)

**Interfaces:**
- Consumes: `AppIndex.FindApp` (Task 4).
- Produces:
  - The CLI `NothingShell.exe --quit` asks the running instance to quit cleanly through the registered window message `NothingShell.Quit`, broadcast to top-level windows. The running app's taskbar watcher window receives it, after a `ChangeWindowMessageFilterEx` allow. The command exits 0 when the instance was gone within 10 s and 1 otherwise.
  - `SettingsStore.Load()` normalises `PinnedApps: null` to `[]`.

- [ ] **Step 1: Write the failing tests.**
  - `Load_NullPinnedApps_BecomesEmpty`.
  - `RestoreStore_Save_WritesThroughFlush`: the file exists and parses after `Save`. Assert the content round-trips with no truncation, and replace the weak `Contains("true")` assertion in the existing test with a parsed-value assertion.
- [ ] **Step 2: Run the tests and confirm they fail.** Run `dotnet test tests/NothingShell.Core.Tests --filter "NullPinnedApps|RestoreStore"`. Expected: fail.
- [ ] **Step 3: Implement.**
  - `JsonFile.WriteAtomic`: `FileStream.Flush(true)` on the temp file before the replace. Normalise null pins.
  - **Watchdog:** subscribe to the watchdog's `Exited` event. If it exits while the shell runs, log `watchdog exited (code N)` and relaunch it once. If the relaunch fails, log an error. The taskbar stays hidden by design, and the user can quit from the menu.
  - **`ShellLinkNative` launch:** set `lpDirectory` to the target's folder. On failure, log `GetLastError`.
  - **Pins:** resolve display names through `AppIndex.FindApp`. This fixes UWP pins named "Application Frame Host" and PWA pins launching plain Chrome. A pin whose shortcut has an AUMID launches `shell:AppsFolder\<AUMID>`.
  - **`--quit`:** the handler calls `Shutdown()` through the existing exit path. In `plan1-handson.ps1`, `Quit-App` uses `--quit` first and falls back to WM_CLOSE-to-all.
- [ ] **Step 4: Run the tests and confirm they pass.** Run `dotnet test NotWin.sln`. Expected: all pass, and the Release build is clean.
- [ ] **Step 5: Commit.** Message: `fix: restore-file flush, null pins, watchdog relaunch, pin names, --quit`.

### Task 10: Hands-on launcher check, budget and docs

**Files:**
- Modify: `build/verify/plan1-handson.ps1` (add section `Launcher`), `docs/superpowers/spikes/2026-11-17-phase1-measurements.md` (add "Phase 2" section), `README.md` (Using it: launcher)

**Interfaces:**
- Consumes: everything above. Log lines: `win key hook installed`, `lone win -> launcher`, `launcher focus ok=`, `app index:`, `launch `.

- [ ] **Step 1: Add the `Launcher` section** to the hands-on script. The script measures what it can from the log; the user answers y/n.
  - **LA.1:** tap Win. The launcher opens centered and Start does not open. Measured: `lone win -> launcher` logged.
  - **LA.2:** with Notepad focused, tap Win and type "calc" without clicking. The text appears in the search box. Measured: `launcher focus ok=True`. Repeat with elevated Task Manager focused.
  - **LA.3:** "calc", then Enter, launches Calculator and the launcher closes. Measured: `launch App "Calculator" ok=True`.
  - **LA.4:** "display" shows the Display setting; Enter opens Settings.
  - **LA.5:** typing a file name the user knows shows it under the file rows within about 1 s.
  - **LA.6:** Esc closes; clicking outside closes; Win again closes.
  - **LA.7:** Win+E, Win+D, Win+Shift+S, Win+Tab, Win+L behave normally, and Win+A and Win+N still open Windows' own panels (Plan 3 changes this).
  - **LA.8:** start and search buttons on the dock open the launcher.
  - **LA.9:** "All apps ›" lists apps A–Z, and Esc returns.
  - **LA.10:** the power menu shows Sleep, Restart and Shut down. Only check that it opens; do not click them.
  - **LA.11:** after 3 launches, reopen. Most used reflects those launches.
  - **LA.12:** a full-screen video closes an open launcher and dock popup.
  - Then re-run the existing sections Task8, Dock and Reserve unchanged, as a regression check of Plan 1.
- [ ] **Step 2: Budget.** Open the launcher 5 times with searches, close it, leave the PC idle 10 minutes, then measure:
  - working set, private bytes and CPU over 60 s for NothingShell and the watchdog, using the Plan 1 method;
  - the size of `%AppData%\NothingShell\launcher-usage.json`.
  Pass: total working set under 100 MB and CPU under 0.5%. If it fails, profile (`dotnet-counters`), reduce, and raise it with the user before closing the plan.
- [ ] **Step 3: Docs.**
  - README: Win opens the launcher, the dock buttons, Esc and Enter, All apps, power, and the fact that Win+A/N are still Windows' own until Plan 3.
  - Measurements doc: Phase 2 numbers.
- [ ] **Step 4: Run all tests.** Run `dotnet test NotWin.sln`. Expected: all pass.
- [ ] **Step 5: Commit.** Message: `docs: phase 2 launcher measurements + README`. Ask the user before pushing `plan2-launcher` and opening a PR.

---

## User decisions (2026-11-18, at plan review)

1. **Fenn at sign-in:** Fenn no longer starts with Windows, so no Fenn-after-start check is needed.
2. **Power menu:** Restart and Shut down act immediately, with no confirmation, the same as Windows' Start menu.
3. **Ranking:** keep the strict apps → settings → files order from the spec.
4. **Execution:** native. Tasks are implemented in the main session, followed by one whole-branch review on the most capable model.

# Plan 9: Tic-tac-toe, Folder Pictures, Notes, Alarms, Settings Sync — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the five additions the user approved: three new desktop widgets (tic-tac-toe, notes, alarm), eight
more folder pictures, and settings sync through a folder the user picks.

**Architecture:**
- Rules (game, alarm timing, sync decisions) are pure Core code with tests.
- The app layer holds the widgets, the popup editors (normal windows that can take the keyboard, like
  `PhotoManagePopup`), the alarm service and the sync service.
- New widget kinds are appended to `WidgetKind`, because layouts are saved as numbers.

**Tech Stack:** .NET 10, WPF, System.Text.Json, `System.Media.SoundPlayer`, `Microsoft.Win32.OpenFolderDialog`, xUnit.

**Spec:** `docs/superpowers/specs/2026-11-21-new-widgets-and-sync-design.md`

## Global Constraints
- **Enum values, appended in this order:** `TicTacToe` = 10, `Notes` = 11, `Alarm` = 12.
- **Widget sizes:**
  - TicTacToe: [2×2, 3×3];
  - Notes: [2×2, 2×1, 4×2];
  - Alarm: [2×1, 1×1].
- **Each new widget appears at most once per desktop.** In the ＋ Add picker, TicTacToe comes after Snake, and Notes
  then Alarm come after Agenda.
- **Picker names:** "Tic-tac-toe", "Notes", "Alarm".
- **New dot pictures**, each 11 columns wide: rocket, heart, moon, sun, code, book, bolt, ghost.
- **Alarm sound:** `C:\Windows\Media\Alarm01.wav`, looped for at most 60 s. Snooze is 5 min.
- **Sync folder:** `<chosen folder>\Nothing Shell sync\`, holding `settings.json`, `notes.txt` and `alarms.json`.
  - **Per PC, never taken from a copy:** `StartWithWindows`, `SyncFolder`, `PinsImported`.
- **Never logged:** note text, alarm labels, or the sync folder path. Log outcomes and counts only.
- No new NuGet packages.

## Review Focus
1. **Sync when both PCs changed a file:** the newer file wins and nothing is half-written. Covered by the `SyncRule`
   tests in Task 6.
2. **Sync folder unreachable** (OneDrive offline, folder deleted): the shell keeps its local copies and the status
   says so. Covered by the `SyncService` test in Task 7.
3. **A repeating alarm around midnight or at the end of the week** rings on the right day. Covered by the
   `AlarmRules` tests in Task 4.
4. **Time typed badly in the alarm editor** ("25:00", "7", "abc"): it is refused and the field turns red. Covered by
   the `ParseTime` tests in Task 4.
5. **Tic-tac-toe input after the game ends:** a click restarts the game, and no move goes onto a full board.
   Covered by the game tests in Task 2.

---

### Task 1: Eight more folder pictures
**Files:**
- Modify `src/NothingShell.Core/Widgets/DotArt.cs`.
- Test `tests/NothingShell.Core.Tests/Widgets/PhotoAndFolderTests.cs`.
- [ ] **Test `DotArt_SixteenPictures_All11Wide`:**
  - `DotArt.Pictures` has 16 names, the 8 new ones last, in the order rocket, heart, moon, sun, code, book, bolt, ghost.
  - Every row of every picture is 11 characters, using only `#` and `.`.
  - `Next("star") == "rocket"` and `Next("ghost") == "cat"`.
- [ ] Red, draw the eight pictures, green, then commit `feat(widgets): eight more folder pictures`.

### Task 2: Tic-tac-toe
**Files:**
- Create:
  - `src/NothingShell.Core/Widgets/TicTacToe.cs`;
  - `src/NothingShell/Widgets/TicTacToeWidget.cs`.
- Modify:
  - `WidgetLayout.cs` (kind, sizes, picker);
  - `ShellSettings.cs`, adding `GameScore TicTacToeScore`;
  - `AddPicker.cs` (name, icon "gamepad");
  - `App.xaml.cs` (factory).
- Test `tests/NothingShell.Core.Tests/Widgets/TicTacToeTests.cs`.

**Interfaces — Produces:**
- `record GameScore(int You, int Draws, int Shell)`
- `sealed class TicTacToe`:
  - `char[] Cells` (9 cells, each '.', 'X' or 'O')
  - `bool Over`, `char? Winner`, `int[]? Line`
  - `bool Play(int cell)`: X moves; false if the cell is taken or the game is over
  - `void ShellMove(Random rng)`
  - `void Reset(bool shellFirst, Random rng)`

**Tests:**
- [ ] `Winner_RowColumnDiagonal`
- [ ] `Play_TakenOrOver_IsRefused`
- [ ] `ShellMove_TakesTheWin`
- [ ] `ShellMove_BlocksYourWin`
- [ ] `ShellMove_EmptyBoard_TakesCentre`
- [ ] `ShellMove_FullBoard_DoesNothing`
- [ ] `Draw_WhenFullWithNoLine`
- [ ] `Agenda_Sizes...`-style rule test for TicTacToe sizes and the picker position.

**Widget behaviour:**
- Dotted 3×3 grid; X drawn as red dots, O as white dots.
- A click plays your move, then the shell answers after 350 ms.
- When the game ends, the winning line blinks for 1.2 s. The next click resets; who goes first alternates.
- The score is shown under the board as "YOU 3 · DRAW 1 · NOTHING 2" and saved through `UpdateSettings`.
- [ ] Red, green, build the app, commit `feat(widgets): tic-tac-toe`.

### Task 3: Notes widget
**Files:**
- Create:
  - `src/NothingShell/Widgets/NotesWidget.cs`;
  - `src/NothingShell/Widgets/NotesEditorPopup.cs`;
  - `src/NothingShell/Notch/Dashboard/SharedNotes.cs`.
- Modify:
  - `NotesTile.cs`, which takes a `SharedNotes`;
  - `WidgetLayout.cs`, `AddPicker.cs` (icon "book"), `App.xaml.cs`.
- Test `tests/NothingShell.Tests/SharedNotesTests.cs`.

**Interfaces — Produces:** `sealed class SharedNotes(NotesStore store)`:
- `string Text`
- `void Set(string text, object? from)`: saves, then raises `Changed(from)`
- `event Action<object?>? Changed`
- `void Reload()`: re-reads the store and raises `Changed(null)`

**Tests:**
- [ ] `Set_SavesAndTellsTheOthers`: a listener sees the new text with `from` set to the setter.
- [ ] `Reload_PicksUpTheFile`.

**Widget:**
- "NOTES" label, then the text. At 2×1 the first 3 lines; at 2×2 and 4×2 as many lines as fit, trimmed.
- When empty, it shows "TAP TO WRITE".
- A click opens `NotesEditorPopup` next to the widget. The popup:
  - is a 320×260 multi-line TextBox in the card style;
  - saves 500 ms after typing stops;
  - closes on Esc or when it loses focus, saving immediately.
- The NotesTile updates from `Changed` whenever it is not the sender.
- [ ] Red, green, commit `feat(widgets): notes widget shared with the notch`.

### Task 4: Alarm rules and store (Core)
**Files:**
- Create:
  - `src/NothingShell.Core/Alarms/AlarmRules.cs`;
  - `src/NothingShell.Core/Alarms/AlarmStore.cs`.
- Test `tests/NothingShell.Core.Tests/Alarms/AlarmRulesTests.cs`.

**Interfaces — Produces:**
- `record Alarm(string Id, int Hour, int Minute, int Days, bool On)`. `Days` is a bitmask with bit 0 = Sunday and
  bit 6 = Saturday; 0 means once.
- `static class AlarmRules`:
  - `DateTime? NextRing(Alarm a, DateTime after)`: the first ring strictly after `after`, in local wall-clock time;
    null when the alarm is off.
  - `(Alarm Alarm, DateTime At)? Next(IEnumerable<Alarm> all, DateTime now)`
  - `IReadOnlyList<Alarm> Due(IEnumerable<Alarm> all, DateTime lastCheck, DateTime now)`: alarms with a ring in
    `(lastCheck, now]`.
  - `string DaysLabel(int days)`: "ONCE", "EVERY DAY", "WEEKDAYS", "WEEKENDS", else "MON WED FRI".
  - `string Until(DateTime at, DateTime now)`: "IN 25 MIN", "IN 8 H", "IN 2 D".
  - `(int H, int M)? ParseTime(string text)`: accepts "7:30", "07:30" and "730"; refuses "25:00", "7" and "abc".
- `sealed class AlarmStore(string dir)`: `IReadOnlyList<Alarm> Load()` and `Save(IReadOnlyList<Alarm>)`, stored in
  `alarms.json` through `JsonFile.WriteAtomic`. A bad file loads as an empty list.

**Tests:**
- [ ] `NextRing_OnceToday_ThenTomorrow`
- [ ] `NextRing_Weekdays_FridayNightGoesToMonday`
- [ ] `NextRing_Off_IsNull`
- [ ] `Due_FiresOnceInTheWindow`
- [ ] `DaysLabel_Names`
- [ ] `Until_Units`
- [ ] `ParseTime_GoodAndBad` (Theory)
- [ ] `Store_RoundTrip_AndBadFileIsEmpty`
- [ ] Red, green, commit `feat(alarms): alarm rules and store`.

### Task 5: Alarm service, ring window, widget, editor
**Files:**
- Create:
  - `src/NothingShell/Alarms/AlarmService.cs`;
  - `src/NothingShell/Alarms/AlarmRingWindow.cs`;
  - `src/NothingShell/Widgets/AlarmWidget.cs`;
  - `src/NothingShell/Widgets/AlarmEditorPopup.cs`.
- Modify `WidgetLayout.cs`, `AddPicker.cs` (icon "cup") and `App.xaml.cs`.
- Test `tests/NothingShell.Tests/AlarmServiceTests.cs`.

**Interfaces — Produces:** `sealed class AlarmService(AlarmStore store, Func<DateTime> now)`:
- `IReadOnlyList<Alarm> Alarms`
- `void Set(IReadOnlyList<Alarm>)`: saves and raises `Changed`
- `void Reload()`
- `event Action? Changed`
- `event Action<Alarm>? Ringing`
- `Alarm? RingingNow`
- `void Check()`: called by a 1 s timer, using `lastCheck`
- `void Snooze()`: rings again in 5 min
- `void Stop()`
- After a "once" alarm rings, it is saved with `On = false`.

**Tests** (with a fake clock):
- [ ] `Check_RingsOnceAtItsMinute`
- [ ] `OnceAlarm_TurnsOffAfterRinging`
- [ ] `Snooze_RingsAgainInFiveMinutes`
- [ ] `Stop_ClearsRinging`

**App:**
- On `Ringing`:
  - loop the sound, stopping after 60 s or on Stop / Snooze;
  - `_notch.NotifyLocal("Alarm", "07:30", "timer")` and `notifications.AddLocal("Alarm", "07:30")`;
  - show `AlarmRingWindow`: topmost, at the top centre under the notch, with the time and **Snooze 5 min** / **Stop**.
    It waits while the screen is in full screen and shows when full screen ends, if the alarm is still ringing.
- **AlarmWidget:**
  - 2×1: "ALARM" label, the big time and "IN 8 H · WEEKDAYS"; "NO ALARMS" when there are none.
  - 1×1: the big time only.
  - The card flashes red while ringing.
  - A click opens `AlarmEditorPopup`. Each row has a time TextBox (red on a bad `ParseTime`), S M T W T F S toggle
    chips, an on/off toggle and ✕. Below the rows is **＋ Add**, which adds the next full hour, once, on.
- [ ] Red, green, commit `feat(alarms): alarm widget, editor and ringing`.

### Task 6: Sync rules (Core)
**Files:** Create `src/NothingShell.Core/Sync/SyncRule.cs`; Test `tests/NothingShell.Core.Tests/Sync/SyncRuleTests.cs`.

**Interfaces — Produces:**
- `record SyncSide(bool Exists, string? Hash, DateTime WrittenUtc)`
- `enum SyncAction { None, Push, Pull }`
- `static class SyncRule`:
  - `SyncAction Decide(SyncSide local, SyncSide remote, string? lastSyncedHash)`:
    - only one side changed since the last sync: copy it to the other side;
    - both changed: the newer one wins;
    - no last sync: the newer one wins, or the side that exists;
    - same hash: None.
  - `ShellSettings Merge(ShellSettings mine, ShellSettings theirs)`: theirs, but keeps mine's `StartWithWindows`,
    `SyncFolder` and `PinsImported`.
  - `string Hash(byte[] bytes)`: SHA-256 as hex.
- Modify `ShellSettings`: add `string? SyncFolder`.

**Tests:**
- [ ] `OnlyLocalChanged_Push`
- [ ] `OnlyRemoteChanged_Pull`
- [ ] `BothChanged_NewerWins`
- [ ] `FirstSync_OneSideMissing`
- [ ] `SameHash_None`
- [ ] `Merge_KeepsPerPcFields`
- [ ] Red, green, commit `feat(sync): sync rules`.

### Task 7: Sync service, Settings section, start-up and live apply
**Files:**
- Create `src/NothingShell/Sync/SyncService.cs`.
- Modify:
  - `SettingsWindow.cs`: a "Sync" section;
  - `App.xaml.cs`;
  - `DockController.cs`: `ApplyExternal(ShellSettings s)`, which swaps the settings, saves, re-applies pins
    (`Rebuild`), keep-visible and previews.
- Test `tests/NothingShell.Tests/SyncServiceTests.cs`.

**Interfaces — Produces:** `sealed class SyncService(string dataDir, Func<string?> folder)`:
- `SyncOutcome RunOnce()`, where `record SyncOutcome(bool Reached, IReadOnlyList<string> Pulled, IReadOnlyList<string> Pushed)`.
- `last-sync.json` in the data dir keeps the last synced hash for each file.
- Pulled settings are merged with `SyncRule.Merge` before they are written locally.
- Files are written to a temp file and then renamed.

**Tests** (temp folders, no WPF):
- [ ] `FirstRun_PushesLocalFiles`
- [ ] `RemoteNewer_IsPulled_AndPerPcFieldsKept`
- [ ] `FolderMissing_NotReached_LocalUntouched`
- [ ] `NoChange_NothingCopied`

**App:**
- At start, before `StartDock` loads settings, read `SyncFolder` from the local settings file and run `RunOnce()`.
- After that, run it on a 30 s timer, and on a debounced (2 s) `FileSystemWatcher` over the sync folder.
- When something was pulled:
  - **settings:** `dock.ApplyExternal(store.Load())`, then rebuild the widgets and restart the weather;
  - **notes:** `SharedNotes.Reload()`;
  - **alarms:** `AlarmService.Reload()`.
- **The Settings "Sync" section:**
  - the status: "OFF", "SYNCED HH:MM", or "CAN'T REACH THE FOLDER";
  - **Choose folder…** (an `OpenFolderDialog`), then a sync runs now;
  - **Turn off**.
- [ ] Red, green, commit `feat(sync): settings, notes and alarms sync through a folder you pick`.

### Task 8: Docs and hands-on
- [ ] README: lines for the three widgets, the pictures, alarms (and their limits) and sync.
- [ ] Hands-on: add a `Plan9` section with checks for tic-tac-toe, notes in both places, an alarm one minute ahead
  (sound, Snooze, Stop), and sync with a test folder.
- [ ] `dotnet test NotWin.sln` passes. Commit `docs: Plan 9 README and hands-on`.

# Plan 12: Rename Nothing Shell to NotWin — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The app is NotWin everywhere (text, programs, folders, code, Windows registration), and the user's data and
the old install carry over with a single reinstall.

**Architecture:** A pure Core rule decides the data migration and an app helper performs it at start-up. The
mechanical rename (git mv + text replace) runs as one task with the build and tests as its gate. The installer gains
a legacy-cleanup step for the old name.

**Tech Stack:** .NET 10, WPF, PowerShell 7, xUnit.

**Spec:** `docs/superpowers/specs/2026-11-21-rename-to-notwin-design.md`

## Global Constraints
- **New names:**
  - `NotWin` (exe, assembly, root namespace, data folder, Program Files folder, Run value, package identity);
  - `NotWin.Core`, `NotWin.Native`, `NotWin.Watchdog`, `NotWin.DragHelper`;
  - tests `NotWin.Tests`, `NotWin.Core.Tests`;
  - cert `CN=NotWin Dev`;
  - mutex `Local\NotWin.Instance`, message `NotWin.Quit`, property `NotWin.DragGo`.
- **Visible text:** "Nothing Shell" becomes "NotWin". "Nothing" alone (the design language, "NOTHING 2" in the
  tic-tac-toe score) stays.
- **Unchanged:**
  - the DPAPI entropy `"NothingShell.Google"`;
  - the repository folder;
  - files under `docs/superpowers/` (history);
  - the `.superpowers/` ledgers.
- **Migration skips** `taskbar-restore.json`, `log.txt`, `log.1.txt` and `draghelper.log`. It writes the marker
  `migrated-from-nothingshell.txt` and keeps the old folder.
- **Never logged:** file names. Counts only.

## Review Focus
1. **New folder already has data:** a second start, or a reinstall after use. The migration must not run again or
   overwrite anything. Test: `Plan_NewHasFiles_IsNone`.
2. **Old folder missing** (a fresh machine): there is nothing to do, and start-up is not slowed down. Test:
   `Plan_NoOld_IsNone`.
3. **A locked or unreadable file in the old folder:** the copy continues and the count is reported. Test:
   `Run_ReadOnlyTarget_ContinuesAndCounts`.
4. **Leftover old Run value** (the installer step was skipped): it is removed at start-up so the old exe cannot start
   too. A code step in Task 3; the hands-on check is in Task 5.
5. **Google sign-in after migration:** `google.dat` still decrypts. Test: `Vault_ReadsAFileMigratedFromTheOldFolder`.

---

### Task 1: Data migration rule and runner
**Files:**
- Create `src/NothingShell.Core/DataMigration.cs` (it moves with the rename in Task 2).
- Test `tests/NothingShell.Core.Tests/DataMigrationTests.cs`.

**Interfaces — Produces:**
- `enum MigrationStep { None, Copy }`
- `static class DataMigration`:
  - `const string Marker = "migrated-from-nothingshell.txt"`
  - `static readonly IReadOnlySet<string> Skip` = { `taskbar-restore.json`, `log.txt`, `log.1.txt`,
    `draghelper.log` }, compared case-insensitively.
  - `MigrationStep Plan(bool oldExists, bool newHasFiles)`: Copy only when the old folder exists and the new one
    has no files.
  - `(int Copied, int Failed) Run(string oldDir, string newDir)`: copies recursively, skips the Skip names at the
    top level, never overwrites, writes the Marker last, returns the counts.

**Tests:**
- [ ] `Plan_CopyOnlyWhenOldThereAndNewEmpty` (four cases)
- [ ] `Plan_NewHasFiles_IsNone`
- [ ] `Plan_NoOld_IsNone`
- [ ] `Run_CopiesTreeSkipsStateAndLogs_WritesMarker`: `settings.json`, `frames/a/1.jpg` and `google.dat` are copied;
  `taskbar-restore.json` and `log.txt` are not; the Marker exists.
- [ ] `Run_NeverOverwrites`
- [ ] `Run_ReadOnlyTarget_ContinuesAndCounts`: a read-only existing target makes Failed go up by 1, and the other
  files are still copied.
- [ ] Red, green, commit `feat: data migration rule for the NotWin rename`.

### Task 2: Mechanical rename
**Steps:**
- [ ] `git mv` the five project folders `src/NothingShell*` → `src/NotWin*` and the two test folders
  `tests/NothingShell*.Tests` → `tests/NotWin*.Tests`, and each `.csproj` to the new name.
- [ ] Text-replace `NothingShell` → `NotWin` in `*.cs`, `*.xaml`, `*.csproj`, `*.sln`, `*.manifest`, `*.xml`,
  `*.ps1`, `*.json` and `*.md` under `src/`, `tests/`, `build/` and the root `README.md`.
  - Keep the literal `"NothingShell.Google"` in GoogleVault (spec).
  - Task 3 adds the explicit legacy names in its own files, so no other `NothingShell` occurrence is kept.
- [ ] Text-replace "Nothing Shell" → "NotWin" in the same files.
- [ ] Build `dotnet build NotWin.sln` with 0 errors, then run `dotnet test NotWin.sln`, which must pass.
- [ ] **Check:** `git grep -n "NothingShell" -- src tests build README.md` finds only the GoogleVault entropy (and
  Task 3's legacy names once that task is done).
- [ ] Commit `refactor: rename Nothing Shell to NotWin (projects, namespaces, names)`.

### Task 3: Migration at start-up, legacy Run value
**Files:**
- Modify `src/NotWin/App.xaml.cs` (OnStartup).
- Modify `src/NotWin/Startup/StartupRegistration.cs`.
- Test `tests/NotWin.Tests/GoogleVaultTests.cs`.

**Steps:**
- [ ] **Test `Vault_ReadsAFileMigratedFromTheOldFolder`:** save credentials with GoogleVault in folder A, copy
  `google.dat` to folder B, and load from B. The credentials are equal.
- [ ] **App.OnStartup:** right after `SingleInstance.TryAcquire()` succeeds, call
  `DataMigration.Plan(Directory.Exists(old), HasFiles(new))`. On Copy, run `Run` and log `data migrated from the old
  folder (copied N, failed M)`.
  - `old` is `%AppData%\NothingShell`; it is the constant `LegacyNames.DataFolder` in a new
    `src/NotWin/Startup/LegacyNames.cs`.
  - All legacy literals live in `LegacyNames`: `DataFolder = "NothingShell"`, `RunValue = "NothingShell"`,
    `ProcessNames = ["NothingShell", "NothingShell.Watchdog"]`.
  - Note: Log writes to `%AppData%\NotWin`. Migration runs before any Log call in OnStartup; QuitCommand logs
    nothing.
- [ ] **StartupRegistration:** add `public static void RemoveLegacyRunValue()`, which deletes the `NothingShell`
  value if it exists. App start calls it, guarded.
- [ ] Red (the test fails before Task 1/2 code is wired), green, run the suite, commit
  `feat: carry data over from Nothing Shell; drop its leftover start entry`.

### Task 4: Installer legacy cleanup
**Files:** Modify `build/install.ps1` and `build/uninstall.ps1`.

**Steps:**
- [ ] **install.ps1:** before the process-stop step, add `Remove-LegacyNothingShell`. It runs only if
  `C:\Program Files\NothingShell` exists or the `NothingShell` package or Run value exists. In order, it:
  1. stops `NothingShell.Watchdog`, then `NothingShell`;
  2. if `C:\Program Files\NothingShell\NothingShell.Watchdog.exe` exists, runs it with
     `--restore-now --state "%AppData%\NothingShell"` and waits;
  3. removes the Run value `NothingShell`;
  4. runs `Get-AppxPackage -Name NothingShell | Remove-AppxPackage`;
  5. deletes `C:\Program Files\NothingShell`.

  Each step writes one line to the console, and none of them stops the install.
- [ ] **Check:** `pwsh -NoProfile -Command "[System.Management.Automation.Language.Parser]::ParseFile(...)"` reports
  0 parse errors for both scripts.
- [ ] Commit `build: installer removes the old Nothing Shell install before installing NotWin`.

### Task 5: Docs, hands-on, memory
- [ ] **README:** the title, install folder and data folder use the new names.
- [ ] **Hands-on script:** paths are already renamed by Task 2. Add the check `RN.1 'NotWin took over'`:
  - the old folder `C:\Program Files\NothingShell` is gone;
  - no `NothingShell` Run value;
  - `%AppData%\NotWin` holds `migrated-from-nothingshell.txt`;
  - Google Calendar is still connected.
- [ ] **Memory:** the project memory note says the app is now called NotWin (repo NotWin).
- [ ] `dotnet test NotWin.sln` passes. Commit `docs: NotWin rename in README and hands-on`.

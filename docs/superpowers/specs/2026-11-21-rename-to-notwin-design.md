# Plan 12: Rename Nothing Shell to NotWin (short design)

**Why (user, 2026-11-21):** the GitHub repository is already called NotWin; the app should carry the same name
everywhere — what you see, the programs, folders, code and its registration with Windows ("Everything").

## What changes
| Today | After |
|---|---|
| "Nothing Shell" in every visible string (66 places) | "NotWin" |
| Projects / namespaces / assemblies `NothingShell*` (806 lines) | `NotWin*` (`NotWin`, `NotWin.Core`, `NotWin.Native`, `NotWin.Watchdog`, `NotWin.DragHelper`, tests `NotWin.Tests`, `NotWin.Core.Tests`) |
| `NothingShell.exe`, `NothingShell.Watchdog.exe`, `NothingShell.DragHelper.exe` | `NotWin.exe`, `NotWin.Watchdog.exe`, `NotWin.DragHelper.exe` |
| `C:\Program Files\NothingShell` | `C:\Program Files\NotWin` |
| `%AppData%\NothingShell` | `%AppData%\NotWin` |
| Run value `NothingShell`, cert `CN=NothingShell Dev`, package identity `NothingShell` | `NotWin`, `CN=NotWin Dev`, `NotWin` |
| Mutex `Local\NothingShell.Instance`, message `NothingShell.Quit`, window property `NothingShell.DragGo` | `Local\NotWin.Instance`, `NotWin.Quit`, `NotWin.DragGo` |

**Unchanged on purpose:**
- The repository folder `D:\NothingLauncher`.
- Old specs, plans and ledgers. They are history.
- The DPAPI entropy label `"NothingShell.Google"` used by the Google vault. Changing it would make the saved sign-in
  unreadable, and the label is never shown.

## How data and the old install carry over (builder's calls; user approved the plan)

**Data migration.** It runs at the start of `NotWin.exe`, right after the single-instance check and before anything
writes to the data folder.
- It runs only if `%AppData%\NotWin` is missing or empty and `%AppData%\NothingShell` exists.
- It copies the old folder's contents recursively into the new folder.
- `taskbar-restore.json` (the old session's taskbar state) and log files (`log*.txt`, `draghelper.log`) are skipped.
- When the copy is done it writes a `migrated-from-nothingshell.txt` marker.
- The old folder stays as a backup.
- A file that fails to copy is logged by name count only. The copy continues and never blocks start-up.

**Removing the old install.** `build/install.ps1` gains a legacy-cleanup step that runs before the new install. It
does what `uninstall.ps1` did for the old name:
- stop `NothingShell` and `NothingShell.Watchdog`;
- run the old watchdog's `--restore-now` with the old state folder, so the taskbar comes back as the user had it;
- remove the `NothingShell` Run value;
- remove the `NothingShell` sparse package;
- delete `C:\Program Files\NothingShell`.

It is skipped when there is no old install. The old dev certificate stays in the stores (harmless); a new `CN=NotWin
Dev` certificate is created and trusted the same way as before.

**Start with Windows.** At start-up the app also removes a leftover `NothingShell` Run value, in case the installer
step was skipped.

**Notifications.** Windows ties notification access to the package identity, so the user may be asked once to allow
notifications for NotWin.

## Approaches considered
1. **Big-bang rename with a migration step (picked).** One reinstall; afterwards nothing carries the old name.
2. **Display names only.** The user declined it: they want everything renamed.
3. **Staged (display first, internals later).** Two migrations instead of one, for no gain.

## Left to the builder
- The mechanical rename order (git mv, then text replace).
- Updating the hands-on checklist and README.
- The memory note that names the project.

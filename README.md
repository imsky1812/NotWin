# Nothing Shell (NotWin)

A personal Windows 11 shell layer styled after Nothing OS. Phase 1 replaces the Windows taskbar with a
floating dock: start and search buttons, pinned and running apps, and a tray with Wi-Fi, volume, battery
and the clock. It hides until the mouse touches the bottom edge, or stays visible with its space reserved.
Later phases add the launcher, quick settings and notifications, the notch, and desktop widgets
(see `docs/superpowers/specs/2026-11-17-nothing-shell-design.md`).

Personal use only.

## Requirements

- Windows 11 (developed on 26300), 1920×1080 at 125% tested
- .NET SDK 10 (`net10.0-windows10.0.22621.0`)
- PowerShell 7 (`pwsh`)

## Install

The dock needs `uiAccess` so it can sit above other windows, including elevated ones. Windows only allows
that for a signed exe under Program Files, so installing needs admin once.

```powershell
# from an elevated PowerShell 7 (Run as administrator)
D:\NothingLauncher\build\install.ps1
```

`install.ps1` does the following:
1. Creates a self-signed code-signing cert `CN=NothingShell Dev` (non-exportable) and trusts it on this PC.
2. Publishes the app and the watchdog, signs them, and stops any running copy.
3. Copies them to `C:\Program Files\NothingShell`, then starts the shell once as you (not as admin).

The shell then adds itself to `HKCU\...\Run`, so it starts at sign-in. Only the installed copy writes
that entry.

`install.ps1` also registers a small **sparse package** named `NothingShell`, which points at
`C:\Program Files\NothingShell`. This gives the shell a package identity, which Windows requires before an app can
read notifications. It never appears in Start. If the registration fails, the shell still works, just without
notifications. `uninstall.ps1` removes it. Use `-NoTimestamp` to skip the signing timestamp when offline.

## Using it

- Push the mouse to the bottom edge to show the dock. It hides again shortly after you leave it.
  It stays hidden while a full-screen app (video, game) is in front.
- **Launcher (start menu):** tap **Win** alone, or click the dock's start or search button. Type to search
  apps, settings and files on this PC (no web results), ranked apps → settings → files. Use ↑/↓ to move,
  **Enter** to open and **Esc** to close. A click outside the panel or another Win tap also closes it.
  With nothing typed it shows **Most used** (your launches from Nothing Shell; at first, the apps Windows
  saw you use most recently), **All apps ›** (A–Z) and the power button (Sleep / Restart / Shut down, which
  act immediately).
- **Quick settings + notifications panel:** **Win+A**, **Win+N** or a click on the dock's tray opens it at the bottom
  right. It has:
  - **Wi-Fi tile:** tap to toggle the radio; **›** lists networks (saved and open networks join directly, secured
    new ones open Windows' Wi-Fi settings).
  - **Bluetooth tile:** tap to toggle the radio; **›** lists paired devices (buds and headphones connect or
    disconnect; other devices open Windows' Bluetooth settings).
  - **Volume slider** and an **output chip** that switches the playback device.
  - **Brightness slider** (built-in display).
  - **Notifications** grouped by app: click to open the app, ✕ dismisses, **Clear all**.

  Esc or a click outside closes it.
- **Notch:** a black pill at the top centre shows:
  - the time (dot matrix) and battery;
  - the playing song (art and an equalizer);
  - new notifications for about 3.6 s;
  - "Opening ‹app›…" after a launch;
  - a running timer (small dot grid and countdown);
  - a short charging card when you plug in or unplug.

  It hides while a full-screen app is in front.
- **Notch dashboard:** rest the mouse on the notch to open it; it closes when the mouse leaves. Two pages (dots
  bottom right, or the mouse wheel):
  - page 1: **Timer** (dots go out as time passes; − / play / +; "Time is up" in the notch and the panel),
    **Media** (art, progress, previous / play / next), **Clipboard** (recent text and images you copied, screenshots
    included; click to copy one again; ⤢ for the whole list) and **Notes** (one note, saved as you type);
  - page 2: **Shelf** (drop files to keep them handy, drag them out to use), **Keep awake**, **Weather**, and the
    tools **Grab text** (copies the text in a screen region), **Color** (copies a pixel's hex), **Snip** (copies a
    region and saves a PNG to `Pictures\Screenshots`), **Phone** (Phone Link), **Draw** (draw on the screen with a pen; Done copies
    it and saves a PNG to `Pictures\Screenshots`) and
    **Settings**.

  ✎ **Edit** (bottom left) resizes tiles with their size chip, removes them with ✕ and brings them back with
  **＋ Add**; **Done** saves the layout. Esc closes the dashboard.
- **Desktop widgets:** on the desktop, under your windows (still there after Win+D): **Clock** (dot matrix, drawn
  straight on the wallpaper, dark or light to stay legible), **Snake**, **Timer** (the same timer as the notch),
  **Calendar** (dates; Google events come in a later plan), **Weather**, **Media** and **Battery** (the laptop and
  connected Bluetooth devices; dots or the wheel switch devices).
  - **Snake:** click the board to play; arrows or WASD steer, Space pauses / restarts. While you play, those keys go
    only to the snake; clicking anywhere else, switching windows or Esc pauses it and gives the keys back. The best
    score is saved.
  - **Edit mode:** right-click any widget > **Edit widgets** (or Settings > Widgets > Edit widgets). Drag widgets
    (they snap to the dot grid; a spot that overlaps shows red and is refused), click a size chip to change size,
    then **Done** or Esc. Full screen also ends it. The layout is saved.
  - **Add and remove:** in edit mode every widget has a **✕** (remove) and the top pill has **＋ Add**, which lists
    the widgets you can add. Photo frames and app folders can be added as many times as you like; the others once.
  - **Photo frame:** click an empty frame to pick photos (copies are kept in `%AppData%\NothingShell\frames`, up to
    24 per frame); they change every minute. Click a frame with photos to remove or add some. In edit mode the
    ■ / ● chip switches square and circle.
  - **App folder:** shows a dot picture (click it in edit mode to change it). Click the folder to open its apps;
    **＋** adds apps from a searchable list, right-click an icon > *Remove from folder*.
- **Settings:** right-click the open notch, the Settings tile, or "Settings…" in the start button's right-click
  menu: weather city, widget size (**S / M / L**, applied live), **Edit widgets**, a **wallpaper** picker (four
  Nothing-style wallpapers, plus **✦ New**, which draws a fresh random one at your screen size and applies it; the last 12
  you made stay in the list; one is only applied when you click it), start with Windows, keep the dock visible, and
  Quit.
- **Windows' own notification pop-ups:** Nothing Shell never changes your notification settings. A one-time tip in
  the panel offers to open Windows' settings so you can turn on *Do not disturb*, or turn off banners for chatty apps.
- Quit from a script: `& 'C:\Program Files\NothingShell\NothingShell.exe' --quit` (the same clean exit as the menu).
- Right-click the start button (left-most) for **Keep taskbar visible**, **Start with Windows** and
  **Quit Nothing Shell**. Quitting always gives the Windows taskbar back.
- Right-click an app for pin/unpin and its windows.
- **Genie:** minimizing a window bends it into a funnel that slides into its dock icon; restoring flows it back out
  (like macOS, in Nothing's style). For the best look turn off Windows' own animation (System > Advanced >
  Performance > "Animate windows when minimizing and maximizing"); Nothing Shell never changes that setting.
- **Window previews:** rest the mouse on a running app in the dock to see a live preview of each of its windows; click
  one to switch to it, ✕ to close it. Both effects can be switched off in Settings > Shell.

If Fenn (another taskbar app) is running, Nothing Shell asks before taking over.

## Uninstall

```powershell
# elevated PowerShell 7
D:\NothingLauncher\build\uninstall.ps1
```

This stops the shell and the watchdog, restores the Windows taskbar, removes the Run entry, and deletes
`C:\Program Files\NothingShell`. The dev cert and your data folder are left in place.

## Data folder: `%AppData%\NothingShell`

| File | What it is |
|---|---|
| `settings.json` | Pinned apps, keep-visible, start-with-Windows, the dashboard layout, the timer length, the weather city |
| `clipboard.json` | The notch's clipboard history: the last 50 copies, up to 20 of them images (copies password managers mark private are skipped) |
| `clips\` | The history's images as PNG files (deleted when they leave the history) |
| `notes.txt` | The notch's note |
| `shelf.json` | Paths of the files on the shelf (references, not copies) |
| `weather.json` | The last weather report, shown while offline |
| `launcher-usage.json` | How often and when you launched each item from the launcher (for Most used; max 500 entries) |
| `log.txt` | Startup, taskbar hide/restore, appbar and full-screen events |
| `taskbar-restore.json` | Present only while the shell runs: the taskbar's original auto-hide setting |

If the shell is killed, the watchdog (`NothingShell.Watchdog.exe`) restores the Windows taskbar from
`taskbar-restore.json`. If the taskbar is ever left hidden, run this to restore it by hand:

```powershell
& 'C:\Program Files\NothingShell\NothingShell.Watchdog.exe' --restore-now --state "$env:APPDATA\NothingShell"
```

Ending the shell with Task Manager's "End process tree" also ends the watchdog, so the taskbar can stay hidden until
the next start; use the `--restore-now` command above.

## Development

```powershell
dotnet build NotWin.sln
dotnet test tests\NothingShell.Core.Tests
dotnet run --project src\NothingShell        # Debug, no uiAccess
```

- Only one copy runs at a time (`Local\NothingShell.Instance`). Quit the installed copy from the start
  button menu before running a dev build.
- A Debug build runs without uiAccess, so z-order over elevated windows and messages from Explorer
  behave differently there. Test those on the installed copy.
- A Release build refuses to hide the taskbar unless the watchdog is running next to it.
- Hands-on checks: `build\verify\plan1-handson.ps1` (elevated; use `-Only <sections>` and `-Reinstall`).

| Project | Role |
|---|---|
| `src/NothingShell` | WPF app: dock, tray, popups, startup |
| `src/NothingShell.Core` | Pure logic (settings, reveal state machine, rules); unit-tested |
| `src/NothingShell.Native` | Win32 interop (CsWin32): taskbar, appbar, hooks, icons |
| `src/NothingShell.Watchdog` | Restores the taskbar if the shell dies |
| `spikes/` | Phase 0 feasibility spikes (results in `docs/superpowers/spikes/`) |

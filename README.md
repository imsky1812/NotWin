# NotWin

A personal Windows 11 shell layer styled after Nothing OS. Phase 1 replaces the Windows taskbar with a
floating dock: start and search buttons, pinned and running apps, and a tray with Wi-Fi, volume, battery
and the clock. It hides until the mouse touches the bottom edge, or (Keep dock visible) stays up on the desktop and over normal windows and slides down only for maximized and full-screen apps.
Later phases add the launcher, quick settings and notifications, the notch, and desktop widgets
(see `docs/superpowers/specs/2026-11-17-nothing-shell-design.md`).

Formerly called Nothing Shell (renamed 2026-11-21).

## Install on your PC (one click)

You need Windows 11 (64-bit). Nothing else: .NET is built in.

**[⬇ Download NotWin-Setup.exe](https://github.com/imsky1812/NotWin/releases/latest/download/NotWin-Setup.exe)**
(about 95 MB, version 1.0.0). All versions are on the [Releases](https://github.com/imsky1812/NotWin/releases) page.

1. Download **`NotWin-Setup.exe`** with the link above, or build it yourself (see [Build the setup](#build-the-setup)).
2. Double-click it. Windows SmartScreen may say *"Windows protected your PC"*, because the file is not signed by a
   known publisher: click **More info**, then **Run anyway**.
3. Click **Yes** on the admin prompt, then **OK** on *"Install NotWin?"*.
4. Your taskbar is replaced by the NotWin dock after a few seconds, and *"NotWin is installed and running"*
   appears. NotWin now starts with Windows.

Right-click the dock's start button and choose **Settings…**. To show your calendar, see
[Add your Google Calendar](#add-your-google-calendar).

**What setup does.** The dock needs Windows' `uiAccess` to stay above every window, and Windows only allows that
for a signed program in Program Files. So setup:

- unpacks NotWin into an admin-only folder;
- makes a certificate just for your PC and signs NotWin with it. The certificate's private key only ever lives in
  setup's memory, so once setup closes nothing else can ever be signed with it;
- trusts that certificate on your PC;
- installs into `C:\Program Files\NotWin`;
- registers a small package so NotWin can read notifications;
- adds NotWin to **Settings > Apps**;
- starts NotWin as you, not as admin.

Setup also writes a short log to `%TEMP%\NotWin-Setup.log`.

**Update:** run a newer `NotWin-Setup.exe` the same way. Your settings are kept.

**Uninstall:** go to **Settings > Apps > Installed apps > NotWin > Uninstall**. Your Windows taskbar comes back, and
the certificate, package and start-with-Windows entry are removed. Your settings stay in `%AppData%\NotWin`; delete
that folder too if you want nothing left.

## Add your Google Calendar

NotWin reads your calendar (read-only) through a Google sign-in that **you** own, so no one else's app ever sees
your events. You set it up once, in about five minutes.

1. Open [Google Cloud Console](https://console.cloud.google.com/) and sign in with the Google account whose calendar
   you want. Create a project (top bar > project picker > **New project**, any name, e.g. `NotWin`).
2. Go to **APIs & Services > Library**, search **Google Calendar API**, and click **Enable**.
3. Go to **Google Auth Platform > Branding** (older consoles: **OAuth consent screen**). Fill in an app name (e.g.
   `NotWin`) and your email.
4. Go to **Audience**, choose **External**, then click **Publish app**. While the app is still in *Testing*, Google
   signs you out every 7 days and only listed test users can sign in.
5. Go to **Google Auth Platform > Clients** (older consoles: **Credentials**) and click **Create client**. Choose
   **Desktop app** and click **Create**. Copy the **Client ID** and **Client secret**.
6. In NotWin, open **Settings > Google Calendar**, paste the client ID and secret, and click **Connect**.
7. Your browser opens Google's sign-in. Pick your account. On *"Google hasn't verified this app"*, click
   **Advanced > Go to NotWin (unsafe)**: it is your own app. Allow read-only calendar access. When the page says you
   can close the tab, Settings shows **CONNECTED**.
8. Add the **Calendar** widget (edit widgets > **＋ Add > Calendar**) for dots under days with events, or **Up next**
   for your coming meetings and trips. The calendars switched on in Google Calendar are the ones shown.

Keep the client secret to yourself: paste it only into NotWin's Settings. It is stored encrypted for your Windows
account, never in a shared or synced file. **Disconnect** in the same place signs NotWin out.

**If something goes wrong:**

| What you see | What to do |
|---|---|
| *Error 403: access_denied* | The app is still in *Testing* and your account is not a test user. Publish it (step 4) or add yourself under **Audience > Test users**. |
| Signed out after a week | Same cause: publish the app (step 4), then **Connect** again. |
| **OFFLINE** in Settings | No internet. The last events stay visible, and NotWin refreshes on its own every 15 minutes. |

## Install for development (from source)

You need the .NET SDK 10 (`net10.0-windows10.0.22621.0`) and PowerShell 7 (`pwsh`). Developed on Windows 11
26300, tested at 1920×1080, 125%.

The dock needs `uiAccess` so it can sit above other windows, including elevated ones. Windows only allows
that for a signed exe under Program Files, so installing needs admin once.

Two steps. Building and signing run **without** admin rights; only the copy into Program Files is elevated, so no
build tool ever runs as admin.

```powershell
# 1. from a normal PowerShell 7 (NOT as administrator)
D:\NothingLauncher\build\build.ps1
```

```powershell
# 2. then from an elevated PowerShell 7 (Run as administrator)
D:\NothingLauncher\build\install.ps1
```

`build.ps1` creates a self-signed code-signing cert `CN=NotWin Dev` (non-exportable) the first time, then
publishes the app, the watchdog and the drag helper into `out\app`, signs them and packs the sparse package.
`install.ps1` trusts that certificate on this PC (straight from the certificate store), copies `out\app` into an
admin-only staging folder, refuses to go on unless every exe and dll there is validly signed by that certificate,
stops any running copy, copies the files to `C:\Program Files\NotWin`, then starts the shell once as you (not as
admin).

The shell then adds itself to `HKCU\...\Run`, so it starts at sign-in. Only the installed copy writes
that entry.

`install.ps1` also registers a small **sparse package** named `NotWin`, which points at
`C:\Program Files\NotWin`. This gives the shell a package identity, which Windows requires before an app can
read notifications. It never appears in Start. If the registration fails, the shell still works, just without
notifications. `uninstall.ps1` removes it. Use `build.ps1 -NoTimestamp` to skip the signing timestamp when offline.

## Using it

- Push the mouse to the bottom edge to show the dock. It hides again shortly after you leave it.
  It stays hidden while a full-screen app (video, game) is in front.
- **Launcher (start menu):** tap **Win** alone, or click the dock's start or search button. Type to search
  apps, settings and files on this PC (no web results), ranked apps → settings → files. Use ↑/↓ to move,
  **Enter** to open and **Esc** to close. A click outside the panel or another Win tap also closes it.
  With nothing typed it shows **Most used** (your launches from NotWin; at first, the apps Windows
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
  **Calendar** (dates, plus your Google Calendar once connected), **Weather**, **Media** and **Battery** (the laptop
  and connected Bluetooth devices; dots or the wheel switch devices).
  - **Google Calendar:** Settings > Google Calendar walks you through making your own Google Cloud OAuth client
    (Desktop app, Calendar API, consent screen published so you stay signed in), then **Connect** signs you in once in
    your browser (read-only). The month gets a dot under days with events from every calendar switched on in Google
    Calendar; the 2×1 widget shows the next event. Events refresh every 15 minutes and are kept (encrypted for your
    Windows account) for when you are offline. **Disconnect** signs out and deletes them.
  - **Up next:** a widget (＋ Add > Up next) with only your own plans from Google Calendar: meetings, things you
    added, and trips or bookings Gmail put in your calendar; no holidays or birthdays. 2×2 lists the next four, 2×1
    shows the next one large, 4×2 the next six. "IN 25 MIN" / "NOW" in red when it is soon; click an event to open it
    in Google Calendar.
  - **Tic-tac-toe:** you are X (red dots) against Nothing (O). Click a square; at the end a click starts a new game,
    and who opens alternates. The score is kept.
  - **Notes:** the notch's note on the desktop; click it to edit in a small window next to it (Esc closes). Both show
    the same text.
  - **Alarm:** the next alarm and when it rings; click to add or change alarms (time, days, on/off). A ring plays
    Windows' alarm sound for up to a minute, shows in the notch and the panel, and puts Snooze 5 min / Stop at the top
    of the screen; clicking the ringing widget stops it. Alarms ring only while the PC is awake and NotWin runs.
  - **Folder pictures:** sixteen dot pictures now (rocket, heart, moon, sun, code, book, bolt, ghost added).
- **Sync:** Settings > Sync > Choose folder… (e.g. a OneDrive folder) keeps your settings, pins, widget layout, notes
  and alarms in `<folder>\NotWin sync`; another PC with NotWin and the same folder picks them up (at
  start and while running). When both PCs changed something, the newer copy wins. The Google sign-in, the calendar
  cache, photo-frame photos, start-with-Windows and the wallpaper stay on each PC.
  - **Snake:** click the board to play; arrows or WASD steer, Space pauses / restarts. While you play, those keys go
    only to the snake; clicking anywhere else, switching windows or Esc pauses it and gives the keys back. The best
    score is saved.
  - **Edit mode:** right-click any widget > **Edit widgets** (or Settings > Widgets > Edit widgets). Drag widgets
    (they snap to the dot grid; a spot that overlaps shows red and is refused), click a size chip to change size,
    then **Done** or Esc. Full screen also ends it. The layout is saved.
  - **Add and remove:** in edit mode every widget has a **✕** (remove) and the top pill has **＋ Add**, which lists
    the widgets you can add. Photo frames and app folders can be added as many times as you like; the others once.
  - **Photo frame:** click an empty frame to pick photos (copies are kept in `%AppData%\NotWin\frames`, up to
    24 per frame); they change every minute. Click a frame with photos to remove or add some. In edit mode the
    ■ / ● chip switches square and circle.
  - **App folder:** shows a dot picture (click it in edit mode to change it). Click the folder to open its apps;
    **＋** adds apps from a searchable list, right-click an icon > *Remove from folder*.
- **Settings:** right-click the open notch, the Settings tile, or "Settings…" in the start button's right-click
  menu: weather city, widget size (**S / M / L**, applied live), **Edit widgets**, a **wallpaper** picker (four
  Nothing-style wallpapers, plus **✦ New**, which draws a fresh random one at your screen size and applies it; the last 12
  you made stay in the list; one is only applied when you click it), start with Windows, keep the dock visible, and
  Quit.
- **Windows' own notification pop-ups:** NotWin never changes your notification settings. A one-time tip in
  the panel offers to open Windows' settings so you can turn on *Do not disturb*, or turn off banners for chatty apps.
- Quit from a script: `& 'C:\Program Files\NotWin\NotWin.exe' --quit` (the same clean exit as the menu).
- Right-click the start button (left-most) for **Keep taskbar visible**, **Start with Windows** and
  **Quit NotWin**. Quitting always gives the Windows taskbar back.
- Right-click an app for pin/unpin and its windows.
- **Window previews:** rest the mouse on a running app in the dock to see a live preview of each of its windows; click
  one to switch to it, the red ✕ to close it. It can be switched off in Settings > Shell.

If Fenn (another taskbar app) is running, NotWin asks before taking over.

## Uninstall (development install)

Installed with **setup**? Use **Settings > Apps** instead (see above). For a `build.ps1` / `install.ps1` install:

```powershell
# elevated PowerShell 7
D:\NothingLauncher\build\uninstall.ps1
```

This stops the shell and the watchdog, restores the Windows taskbar, removes the Run entry, and deletes
`C:\Program Files\NotWin`. The dev cert and your data folder are left in place.

## Build the setup

```powershell
# from a normal PowerShell 7 (NOT as administrator)
D:\NothingLauncher\build\make-setup.ps1
```

This makes `out\NotWin-Setup.exe`, a single file with:

- NotWin, its watchdog and drag helper, self-contained for 64-bit Windows, so no .NET is needed on the PC;
- the unsigned notification package;
- the installer itself (`src\NotWin.Setup`), which signs everything on each PC.

Nothing is signed on the build PC. The same file without its payload is the uninstaller that setup leaves in Program
Files.

## Data folder: `%AppData%\NotWin`

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
| `migrated-from-nothingshell.txt` | Written once when NotWin copied your data over from `%AppData%\NothingShell` (counts only) |

**Coming from Nothing Shell:** the first NotWin start copies `%AppData%\NothingShell` into `%AppData%\NotWin` when
the new folder has no data yet (never overwriting; the old folder stays as a backup), so Google Calendar, widgets,
notes and pins carry over. `install.ps1` removes the old `C:\Program Files\NothingShell`, its package and its
start-with-Windows entry.

If the shell is killed, the watchdog (`NotWin.Watchdog.exe`) restores the Windows taskbar from
`taskbar-restore.json`. If the taskbar is ever left hidden, run this to restore it by hand:

```powershell
& 'C:\Program Files\NotWin\NotWin.Watchdog.exe' --restore-now --state "$env:APPDATA\NotWin"
```

Ending the shell with Task Manager's "End process tree" also ends the watchdog, so the taskbar can stay hidden until
the next start; use the `--restore-now` command above.

## Development

```powershell
dotnet build NotWin.sln
dotnet test tests\NotWin.Core.Tests
dotnet run --project src\NotWin        # Debug, no uiAccess
```

- Only one copy runs at a time (`Local\NotWin.Instance`). Quit the installed copy from the start
  button menu before running a dev build.
- A Debug build runs without uiAccess, so z-order over elevated windows and messages from Explorer
  behave differently there. Test those on the installed copy.
- A Release build refuses to hide the taskbar unless the watchdog is running next to it.
- Hands-on checks: `build\verify\plan1-handson.ps1` (elevated; use `-Only <sections>` and `-Reinstall`).

| Project | Role |
|---|---|
| `src/NotWin` | WPF app: dock, tray, popups, startup |
| `src/NotWin.Core` | Pure logic (settings, reveal state machine, rules); unit-tested |
| `src/NotWin.Native` | Win32 interop (CsWin32): taskbar, appbar, hooks, icons |
| `src/NotWin.Watchdog` | Restores the taskbar if the shell dies |
| `spikes/` | Phase 0 feasibility spikes (results in `docs/superpowers/spikes/`) |

# Plan 9: tic-tac-toe, more folder pictures, notes, alarms, settings sync (short design)

**Why (user, 2026-11-21):** the user picked these from a list of ideas, and added a tic-tac-toe widget themselves.
They are all to have the shell's Nothing look: dot drawings, black cards, red accents.

## Builder's picks (for the user to approve)

### 1. Tic-tac-toe widget (new kind `TicTacToe`, sizes 2×2 or 3×3)
- You are X and play against the shell (O).
  - X is drawn as red dots, O as white dots, on a dotted 3×3 grid.
- The shell's moves:
  - it takes a win when it can, and blocks your win;
  - otherwise it plays a good square (centre first, then corners) with a little randomness.
  - So it is beatable, but not easily.
- Click a square to play. When the game ends, the winning line blinks, and a click starts a new game.
- You and the shell take turns going first.
- The score (you, draws, shell) is saved.

### 2. More app-folder pictures
- Eight new dot pictures: rocket, heart, moon, sun, code (`</>`), book, bolt, ghost.
- They are picked like today: a click in edit mode steps to the next one. New folders still get a random picture.

### 3. Notes widget (new kind `Notes`, sizes 2×2, 2×1 or 4×2)
- It shows the same notes as the notch's Notes tile (`notes.txt`). An edit in one place shows in the other.
- Desktop widgets cannot take the keyboard; the snake gets its keys through the shell's key hook. So editing doesn't
  happen inside the card. A click opens a small editor popup next to the widget, which:
  - can take the keyboard;
  - saves as you type;
  - closes with Esc or when you click elsewhere.

### 4. Alarm widget (new kind `Alarm`, sizes 2×1 or 1×1)
- These are Nothing Shell's own alarms; Windows' Clock app does not let other apps read its alarms.
- The widget shows the next alarm, e.g. "07:30 · IN 8 H", with its days.
- A click opens an editor popup. For each alarm: its time, a repeat setting (once, every day, weekdays, or chosen
  days), an on/off switch, and delete. There is also **＋ Add**.
- When an alarm rings:
  - the notch shows "Alarm 07:30" with **Snooze 5 min** and **Stop**;
  - Windows' alarm sound (`C:\Windows\Media\Alarm01.wav`) loops for up to a minute;
  - the widget flashes red.
  - It is also listed in the quick-settings notifications.
- In full screen the sound still plays, and the notch waits until full screen ends, as the timer does.
- **Limit:** alarms ring only while the PC is awake and Nothing Shell is running. They do not wake a sleeping PC.

### 5. Settings sync (Settings > Sync)
- You pick a folder, for example in OneDrive. Nothing Shell keeps a copy there of:
  - settings (dock pins, widget layout and sizes, notch layout, widget scale, weather city, toggles);
  - notes;
  - alarms.
- Another PC running Nothing Shell with the same folder picks up the copy: at start, and when the copy changes while
  it runs.
- If both PCs changed something, the newer copy wins.
- **Stays per PC:**
  - the Google sign-in (Windows' encryption ties it to this PC);
  - the calendar cache;
  - photo-frame photos (too big to copy);
  - start-with-Windows;
  - the wallpaper.
- A pinned app missing on the other PC stays pinned, and its launch fails as it would today. Nothing is uninstalled or
  installed.
- With no folder chosen, nothing changes.

## Non-goals
- No online opponent.
- No waking from sleep.
- No sync service or account of our own; the folder you pick does the syncing.

## Left to the builder
- The dot drawings themselves.
- The exact popup layouts.
- How often to check the sync folder for changes. The plan is to watch it for changes, plus a check at start.

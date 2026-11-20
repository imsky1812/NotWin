# Dot drain minimize + dock window previews — design

Date: 2026-11-20. Extends the dock (`2026-11-17-nothing-shell-design.md` §6.1). Branch: `plan5-desktop-widgets`.

## 1. Intent

The user wants window minimizing to feel like macOS's genie effect "but with Nothing taste", and the dock to show
**live previews of an app's windows** on hover instead of only the app name (screenshot: hovering Claude showed just
"Claude"). Of four directions offered, the user picked **Dot drain**; of three build approaches, **A** (a real
snapshot of the window, with a fallback).

## 2. Goals, non-goals, anti-goals

Goals
- Minimizing any normal app window plays **Dot drain**: the window turns into a halftone dot grid made from its own
  picture; the dots stream along a curve into that app's dock icon, which blinks one red dot as they land.
- Restoring a minimized window plays it **in reverse**: dots flow up out of the icon and re-form the window, then the
  real window is shown.
- Hovering a dock icon of a running app for ~0.4 s shows a **preview card**: one live picture per window (titles under
  them); click a picture to bring that window forward, ✕ to close it.

Non-goals
- Replacing Windows' own minimize animation: no app can. The user turns Windows' animation off themselves (System >
  Advanced > Performance > "Animate windows when minimizing and maximizing"); the shell never changes that setting.
- Maximize / close / open animations; animations for windows not shown in the dock.
- Previews for apps with no windows (pinned, not running): they keep the name label.

Anti-goals
- Any delay or stutter in the minimize itself, or an animation that plays over a full-screen app.
- A frozen overlay left on screen, or input blocked during the animation (the overlay never takes clicks).
- Window titles or app content in the log; snapshots written to disk.
- Measurable idle cost: nothing runs between minimizes; the overlay exists only while an animation plays.

## 3. Constraints

- The shell runs at High integrity with uiAccess; dock windows are topmost layered WPF windows.
- The existing window-event hook (`WindowEventsNative`, one static hook used by the dock's `WindowTracker`) is the
  source of window events; minimize start/end are `EVENT_SYSTEM_MINIMIZESTART` (0x0016) / `MINIMIZEEND` (0x0017).
- Budget: under 100 MB working set, ~0 % idle CPU (spec §3). A single animation may use the GPU/CPU briefly.

## 4. Approaches

| | Approach | Trade-off |
|---|---|---|
| **A (chosen)** | At minimize start, snapshot the window (`PrintWindow` with full content) and build the dots from its pixels; if the snapshot is black or fails, use B for that window | Real "your window turns to dots"; some apps (games, DRM video) give black frames → fallback |
| B | Dots fill the window's outline in greys, no content | Always works, cheaper, less magical (the fallback in A) |
| C | Windows.Graphics.Capture | Most reliable pixels, but a capture session per minimize, more memory, more code |

Previews: one sensible way — **DWM thumbnails** (`DwmRegisterThumbnail`), the live window picture the real taskbar
uses, drawn into our own popup window; no pixels are copied.

## 5. Design

### 5.1 Dot drain
- **Trigger:** `MINIMIZESTART` for a window the dock tracks (it has a dock item). Ignored while a full-screen app is in
  front, while the launcher/panel covers the dock, and when the window is already invisible or cloaked.
- **Snapshot (designer's call, after the builder check):** the minimize event arrives after the window is already
  minimized, when `PrintWindow` gives a blank image. But Windows keeps a live picture of every minimized window (the
  one the taskbar shows): the overlay registers a **DWM thumbnail** of the window and shows it at the window's normal
  position (`GetWindowPlacement`.`rcNormalPosition`), so for one frame the window seems still there; the shell then
  copies that screen region (`BitBlt` from the screen) and samples it to the dot grid — the pixels are never stored —
  and the thumbnail is removed as the dots take over. If the picture is near-black (over 97 % of samples) or the
  thumbnail fails, approach B (grey dots in the window's outline) is used for that window.
- **Dots:** pitch 14 px at 100 % scale (DPI-scaled); each dot's radius from the snapshot's luminance under it
  (halftone: brighter = larger, like the wallpapers). Dots are white with a thin dark rim so they read on light and dark
  wallpapers alike. *(Designer's call.)* About 1,500–4,000 dots for a typical window.
- **Motion:** 480 ms total. Each dot travels from its grid spot to the dock icon's centre along a quadratic curve
  pulled toward the bottom (genie-like); dots start in order of distance to the icon (nearest first, 0–180 ms
  stagger), ease-in, shrink to 30 % on arrival. At the end the icon shows a single red dot blink (160 ms).
- **Restore (`MINIMIZEEND`):** the window is already visible again at this moment, so the shell snapshots it and plays
  the reverse over it (dots from the icon out to the grid, 420 ms), then removes the overlay. The shell never hides or
  restyles other apps' windows. *(Designer's call: the real window shows under the dots for those 420 ms;
  acceptable.)*
- **Overlay:** one transparent, topmost, click-through, no-activate window covering the dock's monitor, shared by every
  animation running at the same moment, created when the first starts and destroyed when the last ends (never idle).
  Drawing via `CompositionTarget.Rendering` with a `DrawingVisual`.
- **Several at once (designer's call):** at most 4 animations run together; further minimizes in the same burst
  (Win+M, "minimize all") are not animated.
- **Other monitors (designer's call):** only windows on the dock's monitor animate; windows on other monitors minimize
  as usual.
- **Target:** the dock icon's screen rectangle from the dock window (if the dock is hidden, the bottom-centre of the
  monitor where the dock appears).

### 5.2 Window previews
- **Hover:** pointer on a dock item with ≥ 1 window for 400 ms → the card opens above the item; leaving item and card
  for 300 ms closes it. Hovering another item switches the card.
- **Card:** black `#1A1A1A`, radius 22, the 1.5 px edge; per window a 220 × 140 (DIP) live thumbnail
  (`DwmRegisterThumbnail`, aspect kept) with the title under it (one line, ellipsis) and a ✕ on hover. Up to 6
  previews in a row; more → the card scrolls sideways.
- **Click** a preview → that window comes forward (restoring it if minimized, which then plays the reverse dot
  drain). ✕ → the window is asked to close (WM_CLOSE).
- The name tooltip on running apps is replaced by the card; pinned-not-running items keep the tooltip.
- **Clicks are unchanged (designer's call):** click and right-click on a dock item do what they do today; any click on
  the item closes the card first, and the card and a dock popup are never open together.

### 5.3 Settings (designer's call)
- Settings > Shell gets two switches, both on by default: **Dot drain when minimizing** and **Window previews in the
  dock**, saved in `ShellSettings`. Under the first, a small hint: "Turn off Windows' own minimize animation for the
  best look" (it only tells; the shell never changes that setting).

## 6. Left to the builder
- How the event hook is extended for 0x0016/0x0017 without breaking the tracker.
- Exact easing curves and stagger, as long as the totals above hold.
- The card's open/close animation (follow `DockPopup`).
- Hands-on checks (DD.1 … in the hands-on script) and README lines, including the note about Windows' own animation.

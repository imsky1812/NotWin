# Widget instances, photo frame and app folder — design

Date: 2026-11-19. Extends `2026-11-17-nothing-shell-design.md` §6.5 (desktop widgets) and Plan 5
(`docs/superpowers/plans/2026-11-19-plan5-desktop-widgets.md`, branch `plan5-desktop-widgets`).

## 1. Intent

The user wants the desktop widgets to behave like the Nothing phone's: **several photo frames and several app folders**,
and an edit mode where widgets are **added and removed**, not only moved and resized. Two new widgets:

- **Photo frame:** a slideshow of the user's own photos, square or circle.
- **App folder:** a dot-drawn picture (a cat, a folder, other objects) that opens a taskbar-like popup of app shortcuts.

## 2. Goals, non-goals, anti-goals

Goals
- Any number of photo frames and app folders; each keeps its own photos / apps / shape / picture.
- Edit mode: **✕** on every widget removes it; **＋ Add** opens a picker of widget types and places the new one.
- The existing widgets (clock, snake, timer, calendar, weather, media, battery) can be removed and added back too.
- The user's current layout survives the change (converted once, positions kept).

Non-goals
- Folder names (user, 2026-11-19: the desktop shows only the picture, the popup only icons and ＋).
- Several instances of the existing widgets (they stay one each; see §5).
- Online photo sources, albums, editing, captions.
- Drag-and-drop of apps into folders, or of folders onto each other.

Anti-goals (a failure even if it works)
- Photos or file names leaving the PC, or file names / app names written to the log.
- A slideshow that costs CPU while nothing changes, or memory that grows with the number of photos.
- Losing the user's layout, snake best score or weather city in the conversion.
- Any new way for a widget failure to crash the shell (every widget build and popup is guarded, as today).

## 3. Constraints

- Everything in the parent spec §3 (personal use, under 100 MB working set, ~0 % idle CPU, privacy rules).
- Widgets are child windows of the desktop icon view (`SHELLDLL_DefView`); only children of it are drawn on this
  Windows build (live finding, 2026-11-19). Popups are top-level windows like the dock's `DockPopup`.
- Layout rules stay: 136 px cells, 12 px gaps, 74 px snap, overlaps refused on move, flagged on resize.
- Settings stay one JSON file written through the dock controller's `UpdateSettings`.

## 4. Approaches

| | Approach | Trade-off |
|---|---|---|
| **A (chosen)** | Every widget is an **instance with its own id**; the saved layout is a list of `{id, kind, size, x, y, options}` | Touches the Plan 5 layout, controller and saving; no caps, no special cases, phone-like |
| B | Fixed hidden slots (Photo 1…6, Folder 1…6) as extra one-off kinds | Quick, but capped at 6 and every later per-widget feature works around slots |
| C | A second, separate list just for frames and folders | Old widgets untouched, but edit mode, dragging, overlaps and saving all handle two systems |

**The user picked A** (2026-11-19): "like the phone" means no caps, and the layout code is new and well tested, so
this is the cheapest moment to change its shape.

## 5. Design

### 5.1 Instances and the saved layout
- `WidgetPlacement` gains a string **`Id`** (short random, e.g. 8 hex characters) and an optional **`Options`** object
  per kind. Kinds stay the enum, plus `Photo` and `Folder`.
- **Repeatable kinds:** `Photo` and `Folder` (any number). All other kinds: at most one each. *(Builder's call recorded
  as the designer's: two clocks or two snake boards add little; the picker shows a one-off kind only while it is not
  on the desktop.)*
- **Conversion:** a saved layout without ids gets an id per entry on load; positions and sizes are kept. The current
  `Sanitize` rule "missing kinds are added back at their default spot" is **removed**: a removed widget stays removed.
  A first run (no saved layout) still gets the default seven.
- **Removing** a widget deletes its entry. Its options go with it, and for a photo frame its copied photos are deleted.
  Global state is unaffected (the snake best score, the weather city, the timer).
- **Background work of removed widgets (designer's call):** the widget object is disposed (its timers stop; the
  snake releases its key gate). Services the notch also uses (weather, media, the timer) keep running. The
  Bluetooth battery watcher is used only by the battery widget, so it runs only while a battery widget is on the
  desktop.
- The controller keys windows by **id**, not by kind, and builds a widget object per instance from a factory by kind.

### 5.2 Edit mode additions
- Every widget shows a small round **✕** at its top-left corner (mockup style: dark disc, light ✕). Click → removed at
  once (no confirmation; the phone does the same). *(Designer's call: a removed frame's photos are gone; acceptable
  because they are copies.)*
- The top pill: "Drag widgets · size chips change size" **[＋ Add] [Done]**.
- **＋ Add** opens a picker popup: one row per kind available (name + a small dot icon). Clicking places the widget at
  its default size in the **first free snapped spot** (scanning from the top-left), or, if none fits, at the top-left
  flagged as overlapping. The picker closes; edit mode stays on.

### 5.3 Photo frame
- Sizes **2×2 (default) · 1×1 · 3×3** (square only). A second chip in edit mode toggles **■ square / ● circle**
  (default square). **The photo sits inside the normal black card** (user, 2026-11-19): the card's 16 px padding
  frames it, square = the photo with rounded corners (radius 14) inside the card, circle = the photo clipped to a
  circle centred on the black square card.
- **Empty:** a dashed outline, a dot-drawn camera and "ADD PHOTOS". Click → the Windows file picker (images:
  jpg, jpeg, png, bmp, gif (first frame), webp, heic if Windows can decode it; multi-select).
- **Photos:** each chosen file is decoded, scaled so its long side is at most 1600 px, re-encoded as JPEG (quality 90)
  and **copied** to `%AppData%\NothingShell\frames\<id>\<random>.jpg`. The original file name is not kept. Files that
  fail to decode are skipped silently (a count, never names, goes to the log).
- **Limit (designer's call):** at most **24 photos per frame**; extra files in one pick are skipped and the popup says
  "24 max". A 240 px thumbnail is saved next to each copy at import, so the manage popup never decodes full photos.
- **Slideshow:** shows one photo, cropped to fill (uniform-to-fill, centred). Every **60 s** it crossfades (600 ms) to
  the next; **order = order added** (designer's call: the Windows picker gives no click order, so each batch is sorted
  by date taken, else file date; there is no reordering); single photo → no timer. *(Designer's call: the timer pauses only while a full-screen
  app is in front, like the rest of the shell; when the desktop is merely covered it keeps going, since one decode a
  minute is cheap.)* Only the current and next photo are decoded (decode width =
  the widget's on-screen width), so memory does not grow with the photo count.
- **Click (not editing, has photos):** a **manage popup** next to the widget: a grid of thumbnails (each with ✕ to
  remove) and a "＋" tile that opens the file picker again. Removing the last photo returns the frame to empty.

### 5.4 App folder
- Sizes **1×1 (default) · 2×2**. The card shows only a **dot-drawn picture**, centred, in the dot-matrix style of the
  weather glyphs (light dots on the black card).
- **Pictures:** a set of 8 — cat, folder, plant, game controller, music note, camera, coffee cup, star. A new folder
  gets a random one; in edit mode clicking the picture moves to the next one.
- **Click (not editing):** a **popup like the dock**: a rounded black pill above (or below, if no room) the widget with
  the apps' real icons in a row (wrapping to more rows after 8), and a last **＋** tile. Click an icon → the app opens
  (through the same unelevated launch path as the dock and launcher) and the popup closes. **Right-click an icon →
  "Remove from folder".** The popup closes on a click outside or Esc.
- **＋** opens an **app chooser**: a search box over the launcher's installed-app list (`AppIndex.Apps`), icons and
  names; click to add (duplicates ignored). An empty folder's popup shows only ＋.
- A folder stores app **ids** (the launcher's `LaunchItem.Id`); an app that is no longer installed is skipped when
  shown.

### 5.5 Settings and storage
- Options live in the layout entry (`Options`): photo frame → `{ shape, photos: [file names in its folder] }`;
  folder → `{ picture, apps: [app ids] }`.
- Unknown kinds or options in a saved file are dropped on load (as today), never a crash.
- **Rollback safety (designer's call):** the new list is saved under a **new settings key** (`WidgetList`); the old
  `Widgets` key is read once for the conversion and then left untouched. An older build that reads the file keeps
  working from the old key and cannot erase frames or folders.

## 6. Left to the builder
- Exact dot drawings for the 8 pictures and the camera (in the existing `DotFont` / glyph style).
- Popup sizes, spacing, animation (follow `DockPopup` and the dashboard's look).
- The id format, the options' JSON shape, and how the conversion is tested.
- Where the file picker and the app chooser windows live in code.
- Hands-on checks for the new behaviour, added to the `Widgets` section of the hands-on script.

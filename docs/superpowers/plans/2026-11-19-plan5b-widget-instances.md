# Plan 5b: Widget instances, photo frame and app folder — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Desktop widgets become instances with ids: any number of photo frames and app folders, and edit mode can add (＋ Add picker) and remove (✕) widgets.

**Architecture:** The saved layout becomes a list of `WidgetPlacement`s with an `Id` and an optional `WidgetConfig` (Core, pure, tested), saved under a new settings key `WidgetList`. `DesktopWidgets` keys windows by id and gets each widget object from a factory in `App`; a removed widget is disposed. Two new widgets (`PhotoFrameWidget`, `FolderWidget`) with their popups; photo copies live in `%AppData%\NothingShell\frames\<id>\`.

**Tech Stack:** C# / WPF on .NET 10 (`net10.0-windows10.0.22621.0`), xUnit; existing `DotFont`, `TileChrome`, `AppIconCache`, `AppIndex`, `ShellLinkNative`.

**Spec:** `docs/superpowers/specs/2026-11-19-widget-instances-photo-folder-design.md` (extends `docs/superpowers/specs/2026-11-17-nothing-shell-design.md` §6.5). Branch: `plan5-desktop-widgets` (continues Plan 5).

## Global Constraints

- Photo copies: long side ≤ **1600 px**, JPEG quality **90**, thumbnails **240 px**, at most **24 photos per frame**, stored as `%AppData%\NothingShell\frames\<id>\<random>.jpg`; original file names are never kept.
- Slideshow: next photo every **60 s**, crossfade **600 ms**, order = order added (each batch sorted by date taken, else file date); paused only while a full-screen app is in front.
- Photo frame sizes **2×2 (default) · 1×1 · 3×3**; shape `square` (default) / `circle`; the photo sits **inside the normal black card** (16 px padding), square = photo corners radius 14, circle = photo clipped to a centred circle.
- Folder sizes **1×1 (default) · 2×2**; no name anywhere; 8 pictures: `cat`, `folder`, `plant`, `gamepad`, `note`, `camera`, `cup`, `star`; a new folder gets a random one.
- Folder popup: dock-like black pill with app icons (row wraps after **8**), last tile ＋; click launches through `ShellLinkNative.Open` and closes; right-click → "Remove from folder"; closes on outside click or Esc.
- Repeatable kinds: `Photo`, `Folder`. All other kinds at most one each.
- Never log file names, app names, photo paths. Every widget build and popup is guarded (a failure logs, never crashes the shell).
- Layout rules unchanged: 136 px cells, 12 px gaps, 74 px snap, `Gap` 6, overlaps refused on move and flagged on resize.

## Review Focus

1. **Existing users' settings.** An old file (`Widgets` key, entries without ids) converts with positions kept and nothing re-added; the old key is never rewritten, so an older build still works. *(Task 1 `Sanitize_OldEntries_GetIds_KeepPositions`, `Settings_SavesWidgetList_LeavesOldKey`.)*
2. **Odd image files.** A corrupt file, a huge photo, a non-image renamed `.jpg` → skipped or scaled, never a crash; skipped files reported as a count only. *(Task 4 `Import_SkipsUnreadable_CountsOnly`, `Import_ScalesLongSideTo1600`.)*
3. **Removing one frame** deletes only that frame's folder, never a sibling's or the `frames` root. *(Task 4 `Delete_RemovesOnlyThatFrame`.)*
4. **Apps that disappear.** A folder holding an uninstalled app id shows the rest and keeps working. *(Task 2 `Resolve_SkipsMissingApps`.)*
5. **A full desktop.** ＋ Add with no free spot places the widget at the top-left, flagged red, instead of failing; removing every widget leaves an empty desktop that stays empty after a restart. *(Task 1 `Add_NoRoom_TopLeft_Flagged`, `Sanitize_EmptyStaysEmpty`.)*

---

## File Structure

| File | Responsibility |
|---|---|
| `src/NothingShell.Core/Widgets/WidgetLayout.cs` (modify) | `WidgetKind` + `Photo`, `Folder`; `WidgetPlacement.Id/Config`; `WidgetConfig`; `WidgetRules`; `DesktopLayout` add/remove/config/sanitize |
| `src/NothingShell.Core/ShellSettings.cs` (modify) | `WidgetList` key |
| `src/NothingShell.Core/Widgets/PhotoList.cs` (create) | photo order, cap, next index |
| `src/NothingShell.Core/Widgets/FolderApps.cs` (create) | add (dedup), remove, resolve against installed apps, pictures |
| `src/NothingShell.Core/Widgets/DotArt.cs` (create) | the 8 folder pictures + camera as dot rows |
| `src/NothingShell/Widgets/DesktopWidgets.cs` (modify) | windows keyed by id; factory; ✕, ＋ Add, shape chip, picture click; config saving |
| `src/NothingShell/Widgets/EditBackdrop.cs` (modify) | ＋ Add in the pill; ✕ in `EditChrome` |
| `src/NothingShell/Widgets/AddPicker.cs` (create) | the ＋ Add popup |
| `src/NothingShell/Widgets/PhotoStore.cs` (create) | import / thumbnails / delete under `frames\<id>` |
| `src/NothingShell/Widgets/PhotoFrameWidget.cs` (create) | empty state, slideshow, manage popup |
| `src/NothingShell/Widgets/FolderWidget.cs` (create) | dot picture, popup with icons, app chooser |
| `src/NothingShell/App.xaml.cs` (modify) | widget factory, shared snake gate, battery service ownership |

---

### Task 1: Instance layout (Core)

**Files:**
- Modify: `src/NothingShell.Core/Widgets/WidgetLayout.cs`, `src/NothingShell.Core/ShellSettings.cs`
- Test: `tests/NothingShell.Core.Tests/Widgets/WidgetLayoutTests.cs` (existing tests keep passing; update the ones that assert the old "missing kinds are added back" rule)

**Interfaces:**
- Produces:
  - `enum WidgetKind { Clock, Snake, Timer, Calendar, Weather, Media, Battery, Photo, Folder }`
  - `sealed record WidgetConfig(string? Shape = null, IReadOnlyList<string>? Photos = null, string? Picture = null, IReadOnlyList<string>? Apps = null)`
  - `sealed record WidgetPlacement(WidgetKind Kind, WidgetSize Size, int X, int Y, string Id = "", WidgetConfig? Config = null)`
  - `WidgetRules.Sizes(Photo) = [2×2, 1×1, 3×3]`, `Sizes(Folder) = [1×1, 2×2]`, `static bool IsRepeatable(WidgetKind)`, `static IReadOnlyList<WidgetKind> PickerOrder` = Photo, Folder, Clock, Snake, Timer, Calendar, Weather, Media, Battery
  - `DesktopLayout`: `int IndexOf(string id)`, `IReadOnlyList<WidgetKind> Addable()`, `(DesktopLayout Layout, string Id) Add(WidgetKind kind, int areaW, int areaH, Func<string> newId, WidgetConfig? config = null)`, `DesktopLayout Remove(string id)`, `DesktopLayout WithConfig(string id, WidgetConfig config)`, `static string NewId(Random rng)` (8 lowercase hex), and `Sanitize(DesktopLayout? loaded, int areaW, int areaH, Func<string> newId)`
  - `ShellSettings.WidgetList` (`DesktopLayout?`); the old `Widgets` stays as a read-only source for conversion.
- Rules: `Sanitize(null)` → the default seven with new ids; an empty list stays empty; entries without an id get one; duplicate ids get a new one; a second copy of a one-off kind is dropped (the first wins); unknown kinds dropped; **missing kinds are not added back**; sizes/positions snapped and clamped as today.

- [ ] **Step 1: Write the failing tests** (in `WidgetLayoutTests.cs`, ids from a counter `() => $"id{n++}"`):
  - `Sanitize_OldEntries_GetIds_KeepPositions`: a layout of `new(Clock, 3×1, 0, 0)` and `new(Timer, 2×2, 296, 148)` → two entries, same X/Y, `Id` = "id0", "id1".
  - `Sanitize_DoesNotAddRemovedKindsBack`: a saved layout with only the clock → exactly one entry.
  - `Sanitize_EmptyStaysEmpty` (empty list → empty) and `Sanitize_Null_IsDefaultSevenWithIds` (7 entries, 7 distinct ids).
  - `Sanitize_DropsSecondOneOff_KeepsManyPhotos`: two clocks + three photos → one clock, three photos.
  - `Add_PlacesAtFirstFreeSpot`: default layout, `Add(Photo, 1600, 900, …)` → new entry 2×2, not overlapping (`Overlaps(i)` false), id returned.
  - `Add_NoRoom_TopLeft_Flagged`: area 300×300 filled by a 2×2 at (0,0) → `Add(Folder…)` at (0,0) and `Overlaps` true.
  - `Addable_HidesPresentOneOffs_AlwaysOffersPhotoAndFolder`: default layout → `[Photo, Folder]`; after `Remove(snakeId)` → `[Photo, Folder, Snake]`.
  - `Remove_ById` and `WithConfig_ReplacesOnlyThatEntry`.
  - `Json_RoundTrip_KeepsIdsAndConfig` (System.Text.Json with the settings' options) and `Settings_SavesWidgetList_LeavesOldKey` (a `ShellSettings` with `Widgets` set and `WidgetList` set serialises both; deserialising a file with only `Widgets` gives `WidgetList == null`).
- [ ] **Step 2: Run, expect FAIL** — `dotnet test tests/NothingShell.Core.Tests -nologo -v q --filter WidgetLayoutTests` → compile errors (`Id`, `Add`, `Remove`, `Photo` missing).
- [ ] **Step 3: Implement** the interfaces above. `Add` scans like the existing `FirstFree`; `Sanitize` keeps today's snapping/clamping.
- [ ] **Step 4: Run, expect PASS**, then the whole suite: `dotnet test NotWin.sln -nologo -v q`. (The app will not compile against the new `Sanitize` signature until Task 3 — fix the single call in `DesktopWidgets.RebuildCore` to pass `() => DesktopLayout.NewId(Random.Shared)` and read `_get().WidgetList ?? _get().Widgets`, saving to `WidgetList` in `Apply`/`EndDrag`.)
- [ ] **Step 5: Commit** `feat: widget instances in the layout (ids, add, remove, config)`.

### Task 2: Photo order, folder apps, dot pictures (Core)

**Files:**
- Create: `src/NothingShell.Core/Widgets/PhotoList.cs`, `FolderApps.cs`, `DotArt.cs`
- Test: `tests/NothingShell.Core.Tests/Widgets/PhotoAndFolderTests.cs`

**Interfaces:**
- Produces:
  - `static class PhotoList { const int Max = 24; static (IReadOnlyList<string> List, int Skipped) Append(IReadOnlyList<string> existing, IReadOnlyList<(string File, DateTime Taken)> batch); static int Next(int index, int count); }` — `Append` sorts the batch by `Taken`, appends, stops at `Max`.
  - `static class FolderApps { static IReadOnlyList<string> Add(IReadOnlyList<string> apps, string id); static IReadOnlyList<string> Remove(IReadOnlyList<string> apps, string id); static IReadOnlyList<T> Resolve<T>(IReadOnlyList<string> ids, Func<string, T?> find) where T : class; }`
  - `static class DotArt { static IReadOnlyList<string> Pictures { get; } /* the 8 names in the Global Constraints order */; static string[] Rows(string name); static string Next(string? current); static string Random(Random rng); }` — each picture is 11 rows × 11 columns of `#` / `.`; unknown name → `folder`; `"camera"` also serves the empty photo frame.
- [ ] **Step 1: Write the failing tests:** `Append_SortsBatchByDateTaken`, `Append_StopsAt24_ReportsSkipped` (20 existing + 10 new → 24, skipped 6), `Next_Wraps` (`Next(2, 3) == 0`, `Next(0, 1) == 0`), `Add_IgnoresDuplicates`, `Resolve_SkipsMissingApps` (ids a, b, c with b missing → [A, C]), `DotArt_EveryPictureIs11x11` (all 8, each row length 11, 11 rows, at least 12 `#`), `DotArt_NextCyclesAndUnknownIsFolder`.
- [ ] **Step 2: Run, expect FAIL** (types missing).
- [ ] **Step 3: Implement**; draw the 8 pictures and make them readable at 11×11 (render-check them in Task 5).
- [ ] **Step 4: Run, expect PASS** + whole suite.
- [ ] **Step 5: Commit** `feat: photo order, folder apps and dot pictures`.

### Task 3: Controller keyed by id, ✕ and ＋ Add

**Files:**
- Modify: `src/NothingShell/Widgets/DesktopWidgets.cs`, `EditBackdrop.cs`, `WidgetCard.cs` (interface), `src/NothingShell/App.xaml.cs`
- Create: `src/NothingShell/Widgets/AddPicker.cs`
- Test: `tests/NothingShell.Tests/EditChromeTests.cs` (extend)

**Interfaces:**
- Consumes: Task 1.
- Produces:
  - `DesktopWidgets(Func<ShellSettings> get, Action<Func<ShellSettings, ShellSettings>> update, Func<WidgetPlacement, Action<WidgetConfig>, IDesktopWidget?> factory)` — windows in `Dictionary<string, (WidgetHost Host, Grid Root, IDesktopWidget Widget)>`; a widget whose id left the layout is disposed and its window destroyed; `saveConfig` stores via `WithConfig` + `UpdateSettings` (no rebuild).
  - `IDesktopWidget` gains `void SetPaused(bool paused) { }` (full screen) and `void Removed() { }` (called before `Dispose` when the user removes it; the photo frame deletes its files there).
  - `EditChrome(FrameworkElement card, WidgetPlacement p, bool overlaps, double scale)` gains `Border Close` (top-left ✕: 26 px dark disc `#2A2A2A`, light ✕) and, for `Photo`, `Border? ShapeChip` ("■ ▸" / "● ▸") beside the size chip.
  - `EditBackdrop.Build(..., Action done, Action add)`: pill text then **＋ Add** (outlined pill) then **Done**.
  - `AddPicker.Show(IReadOnlyList<WidgetKind> kinds, RectI below, Action<WidgetKind> pick)`: a topmost black popup under the pill, one row per kind (small `DotArt`/glyph icon + name), closes on pick, outside click or Esc.
  - Folder in edit mode: a press-and-release on the grab layer with < 4 px movement → `DotArt.Next` picture (saved). Photo: shape chip toggles `square`/`circle` and rebuilds that widget.
  - App: the factory creates a new widget object per id for every kind; one `SnakeKeyGate` held by App and passed to each `SnakeWidget`; `WinKeyListener` events go to the current snake (`_snake`); `BatteryWidget` creates and owns its `DeviceBatteryService` (disposed with it); `_fullscreen.Changed` → `_widgets.SetFullscreen(fs)` → every widget's `SetPaused`.
- [ ] **Step 1: Write the failing tests** (STA, like `GrabLayer_IsHitTestableOnEveryPixel`): `EveryWidget_HasACloseButton_TopLeft` (`Close` aligned left/top), `PhotoOnly_HasAShapeChip` (Photo → `ShapeChip` not null and reads "■ ▸" for square; Clock → null).
- [ ] **Step 2: Run, expect FAIL** (`Close`, `ShapeChip` missing).
- [ ] **Step 3: Implement** the interfaces; ✕ calls `Removed()`, then `Apply(_layout.Remove(id))`; ＋ Add → `AddPicker` with `_layout.Addable()` → `Apply(Add(...).Layout)` (a new folder gets `DotArt.Random`); the drag code moves from kind to id.
- [ ] **Step 4: Run, expect PASS** + whole suite; render edit mode offscreen (✕ on each widget, ＋ Add in the pill, picker open).
- [ ] **Step 5: Commit** `feat: add and remove widgets in edit mode`.

### Task 4: Photo frame

**Files:**
- Create: `src/NothingShell/Widgets/PhotoStore.cs`, `PhotoFrameWidget.cs`
- Test: `tests/NothingShell.Tests/PhotoStoreTests.cs`

**Interfaces:**
- Consumes: `PhotoList` (Task 2), `WidgetConfig.Photos/Shape`, `IDesktopWidget.Removed/SetPaused` (Task 3).
- Produces:
  - `sealed class PhotoStore(string framesRoot)`: `Task<(IReadOnlyList<string> Added, int Skipped)> ImportAsync(string frameId, IReadOnlyList<string> files)` (off the UI thread; returns new file names in date-taken order), `string PhotoPath(string frameId, string name)`, `string ThumbPath(string frameId, string name)` (`<name>.thumb.jpg`), `void Delete(string frameId)` (only `framesRoot\frameId`; refuses an empty or path-like id).
  - `PhotoFrameWidget(string id, WidgetConfig? config, PhotoStore store, Action<WidgetConfig> save)` : `IDesktopWidget` — empty state (dashed outline, `DotArt "camera"`, "ADD PHOTOS"; click → `OpenFileDialog`, multiselect, filter `*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.webp;*.heic`); slideshow (decode width = on-screen width; only current + next decoded; 60 s / 600 ms crossfade; `SetPaused` stops the timer); click with photos → manage popup (thumbnail grid from `ThumbPath`, ✕ per photo, ＋ tile, "24 max" when full); `Removed()` → `store.Delete(id)`.
- [ ] **Step 1: Write the failing tests** (temp directory per test; images generated with `RenderTargetBitmap` / `PngBitmapEncoder` on STA): `Import_ScalesLongSideTo1600` (a 4000×1000 PNG → stored JPEG 1600×400, thumb long side 240), `Import_SkipsUnreadable_CountsOnly` (a text file named `x.jpg` + one good PNG → 1 added, skipped 1), `Import_DoesNotKeepOriginalNames` (stored name ≠ source name), `Delete_RemovesOnlyThatFrame` (frames a and b → delete a → b still there, root still there), `Delete_RefusesPathLikeIds` (`".."`, `""`, `"a\\b"` → nothing deleted).
- [ ] **Step 2: Run, expect FAIL** (`PhotoStore` missing).
- [ ] **Step 3: Implement** `PhotoStore` (WPF `BitmapDecoder` with `BitmapCreateOptions.IgnoreColorProfile`, `TransformedBitmap` scale, `JpegBitmapEncoder { QualityLevel = 90 }`; date taken from `BitmapMetadata.DateTaken`, else file write time) and `PhotoFrameWidget`; wire `Photo` in the App factory with `new PhotoStore(Path.Combine(DataDir, "frames"))`.
- [ ] **Step 4: Run, expect PASS** + whole suite; render offscreen: empty 2×2, square with a photo, circle with a photo, the manage popup.
- [ ] **Step 5: Commit** `feat: photo frame widget (slideshow, square or circle, manage photos)`.

### Task 5: App folder

**Files:**
- Create: `src/NothingShell/Widgets/FolderWidget.cs`
- Test: `tests/NothingShell.Tests/FolderWidgetTests.cs`

**Interfaces:**
- Consumes: `FolderApps`, `DotArt` (Task 2); `AppIndex.Apps`, `AppIndex.FindApp(string)`; `AppIconCache.Get(string, int)`; `ShellLinkNative.Open(string)`.
- Produces:
  - `FolderWidget(string id, WidgetConfig? config, Func<IReadOnlyList<LaunchItem>> apps, Func<string, LaunchItem?> find, AppIconCache icons, Action<WidgetConfig> save)` : `IDesktopWidget` — the card shows `DotArt.Rows(picture)` centred (dot size: 1×1 → 7, 2×2 → 15); click → popup above the widget (below if no room above), icons 40 px in 52 px tiles, wrap after 8, last tile ＋; click an icon → `ShellLinkNative.Open(item.Target)`, log `folder launch ok=…` without the name, close; right-click → a `DockPopup` row "Remove from folder"; ＋ → app chooser (search box + list of `apps()` with icons; typing filters by name, case-insensitive; click adds via `FolderApps.Add` and saves).
  - `internal static FrameworkElement BuildPopupContent(IReadOnlyList<LaunchItem> items, AppIconCache icons, Action<LaunchItem> open, Action<LaunchItem> remove, Action add)` exposed `public` for the test.
- [ ] **Step 1: Write the failing tests** (STA): `Popup_HasOneTilePerApp_PlusAdd` (3 items → 4 tiles, last is ＋), `Popup_WrapsAfterEight` (10 items → 2 rows), `Popup_EmptyFolder_OnlyAdd`.
- [ ] **Step 2: Run, expect FAIL**.
- [ ] **Step 3: Implement** and wire `Folder` in the App factory (`_index.Apps`, `_index.FindApp`, one shared `AppIconCache`).
- [ ] **Step 4: Run, expect PASS** + whole suite; render all 8 pictures at 1×1 and 2×2 and the popup with 3 apps; redraw any picture that does not read clearly.
- [ ] **Step 5: Commit** `feat: app folder widget (dot pictures, app popup, add and remove apps)`.

### Task 6: Hands-on and docs

**Files:**
- Modify: `build/verify/plan1-handson.ps1` (`Widgets` section), `README.md`, `docs/superpowers/spikes/2026-11-17-phase1-measurements.md` (note only)

- [ ] **Step 1: Hands-on checks** after DW.10 (user answers y/n):
  - **DW.13:** ＋ Add places a photo frame and a folder; ✕ removes a widget; the picker no longer lists a one-off that is on the desktop.
  - **DW.14:** an empty frame opens the file picker; photos appear; square / circle chip; the slideshow changes after a minute; clicking opens the manage popup, ✕ removes a photo, ＋ adds more.
  - **DW.15:** a folder's picture changes on click in edit mode; outside edit mode clicking opens the popup; ＋ adds apps; an icon opens its app; right-click removes it.
  - **DW.16:** after a restart, frames (photos, shape), folders (picture, apps) and removed widgets are as left.
- [ ] **Step 2: Docs.** README: adding and removing widgets, photo frames, app folders.
- [ ] **Step 3: Run all tests and commit** `docs: widget instances, photo frame and folder`. Ask the user before reinstalling and before pushing.

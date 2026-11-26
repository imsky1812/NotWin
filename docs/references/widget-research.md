# Widget research — Nothing OS widget sheet + Dribbble (2026-11-26)

Read-only browsing; nothing signed in to, liked or followed. Images stay local (git-ignored: they are other
people's artwork) — `nothing-widgets.webp` (the Nothing widget sheet) and `dribbble/*.jpg` (search pages and two
shots). Only ideas and patterns below; NotWin's widgets are drawn from scratch.

## The Nothing widget sheet (nothing-widgets.webp)
- **Three surfaces only:** white `#F2F2F2`-ish, near-black, and solid red for "on / alert". Red tiles are rare
  (REC, Mic access, Location access, "OVER LIMIT"): red means *something is active that you should notice*.
- **Shapes:** 1×1 circle, 1×1 rounded square, 2×1 pill (icon + label), 2×2 circle / square. Pills read as
  "toggle"; circles read as "glanceable state".
- **Toggle tile anatomy:** one line icon centred (circle) or left (pill) + a short label in a small grotesk;
  on = white tile with black icon (or red for privacy), off = black tile with white icon.
- **Numbers:** dot-matrix numerals for big values (5,543 steps, 22/10 stacked clock, 43,465), small caps labels
  underneath ("TOTAL STEPS", "STREAK", "OVER LIMIT"). Units as small suffix ("6H20", "16H 32M").
- **Charts as dots:** weekly sleep = one row of dot pairs per day with red dots for the outliers; days + values
  as tiny labels. Weather forecast = dot icons per day.
- **Analogue clocks:** white face with fine ticks and a red seconds hand; black face with chunky white hands and a
  single red dot. No numerals.
- **Compass / direction:** black circle, N in red, dot-matrix arrow, W/E/S letters in tiny type.
- **Flight / trip card:** monospaced caps lines (FROM / TO / IN 19 MIN) + two small icon rows.
- **Stacked clock:** HH over MM in dot matrix on a tall pill (1×2).

## Dribbble patterns (search: "nothing os widgets", "nothing dot matrix", "nothing os notification")
- **Dotted ring as progress** (NOS widgets dark): a ring of white dots with the elapsed part in red dots; same
  idea in a rounded-square version. Good for timers, battery, focus sessions — cheaper than an arc and on-brand.
- **Dot weather icons** (Nothing widgets, icons & numerals): ~9×9 dot icons (sun, cloud, rain, moon) above a dot
  temperature with a dot "°" and a caps label; each shown in black and white variants.
- **Dot bar charts** (stock widgets): columns of dots, the newest column brighter / red; caps title with a red ＋.
- **Notification / island cards:** black pill with a small icon, two lines of text and a red action dot; a dot
  seconds counter ("02:19").
- **Battery bundles:** device glyph + percentage rows, several devices per tile.
- Common restraint: one accent (red) per tile at most; generous padding; type never larger than the dot numerals.

## What this means for NotWin
- Keep the existing card (`#1A1A1A`, radius 26) as the "black" surface; add a **light** variant (`#E8E8E8` with
  `#101010` ink) and a **red** variant (`#D71921`) for on / privacy states. Circles = the same card with full radius.
- Reuse `DotMatrix` for numerals and `DotArtView` for icons; add a **dotted ring** primitive for progress.
- Only redraw on change: system events where they exist, else ≥ 1 s (clocks with seconds) / ≥ 2 s (system meters)
  timers that run only while the widget is visible and not paused.

## Light mode research (2026-11-26, after the first light-mode install)
Problem seen on screen: #E8E8E8 cards on the light halftone wallpaper (#EDEDED + black dots) have no edge; the dots
run through the bare clock; the snake board is a heavy grey block.

What good light UIs do (Dribbble "light mode widgets", "desktop widgets light"; Behance; Nothing OS 5 "Lucent" home
screens via image search; macOS desktop widgets):
- **Separate by value and depth, not lines:** pure white cards on a slightly darker, calm grey or soft-gradient
  background, plus a very soft wide shadow (large blur, ~6–10% black). The water-intake and neumorphic panels on
  Dribbble work this way.
- **Nothing OS 5 "Lucent":** frosted, slightly translucent white widgets over a soft, blurred, low-contrast wallpaper.
  Busy patterns are kept for dark mode or for small accents, never behind text.
- **Calm wallpaper behind content:** light themes use low-detail wallpapers (gradient, very faint grain); high-contrast
  halftone fights everything placed on it.
- **Ink stays black, red stays the only accent**; unlit dots get much lighter in light mode (~8% black), or the grid
  turns heavy.

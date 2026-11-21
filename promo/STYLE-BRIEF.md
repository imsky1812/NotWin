# NotWin promo: style brief and rundown

Made with the Bang Motion skill (opener/promo category). Draft for approval before any animation code.

## Concept

Three candidates:

1. **One continuous shot (chosen).** The whole video is one Windows desktop running NotWin. The camera never cuts: it
   glides from the dock at the bottom, to the launcher in the middle, the panel at the bottom right, the notch at the
   top and the widgets on the left, and each feature is a place the camera arrives at. NotWin *is* a single space laid
   over Windows, so travelling through that space is the most convincing thing to show.
2. **Problem → gone.** A cluttered stock taskbar and desktop are swept away by one dot-matrix wave and NotWin appears.
   Good hook, too thin for 45 s; I'm using it as the opening only.
3. **Dot as stage.** A single red dot moves across a halftone field, and the dots around it gather into each feature.
   Abstract and pretty, but it hides the real UI, which is the selling point.

Structure fingerprint:
`one continuous shot · 8 beats · taskbar → dots / dock / launcher / quick panel / notch dashboard / widget grid / setup file / wordmark · stock taskbar dissolves into dots that form the dock · the whole desktop collapses into a dot-matrix "NotWin"`

## Style

- **Feel:** precise, quiet, tactile.
- **Palette (from `src/NotWin/Theme/Tokens.xaml`):** OLED black `#000000` and ink `#101010` (Brush.Ink) as base; cards
  `#1A1A1A` / tiles `#161616`; text `#E8E8E8` (Brush.Text) with dim `#999999`; light wallpaper `#E6E6E6` (halftone
  light); red `#D71921` (Brush.Red) only as the guide dot, "today" on the calendar, "IN 25 MIN" and the X in
  tic-tac-toe.
- **Fonts:** Space Grotesk 500 (sentence case lines), Space Mono (ALL-CAPS labels and numbers), both bundled, OFL.
  UI pieces use the app's own fonts, so the video reads as the product.
- **Emphasis:** no highlight shapes. Words come up dim → full, one at a time, with a Space Mono label beside the object.
  No full stops on short lines.
- **Background:** halftone dot field from the NotWin wallpapers (≥ 2 layers: black base + soft radial lift + dots).
  Dark for the shell; switches once to the light halftone wallpaper when the camera reaches the widgets: the dots
  swell until they merge into off-white (the wipe *is* the wallpaper), and back to black for the end card.
- **Background motion:** the dot field breathes: dot sizes follow a slowly drifting noise field (menu §7c #8),
  computed from the timeline clock, so every render is identical.
- **Signature motion:** dot-matrix build. Things assemble from dots (dock, notch time, widgets, wordmark) and leave
  by dissolving into dots. Motion is percussive: fast `power4.out` in, quicker `power2.in` out, no bouncy springs, no
  gradients on objects.
- **Guide:** one small red dot (the wallpaper's red dot) leads the camera to each next feature.
- **Object rendering:** flat UI rebuilt as DOM components from the app's XAML and the mockup, with dot-matrix glyphs
  (one family).
- **Transitions:** mostly camera moves inside the one desktop; the dot-swell wallpaper change; one push into the
  notch as it opens; the final collapse to the wordmark. No light leaks, no flat colour bars.
- **Special moment (used once):** the final collapse: every widget, the dock and the notch break into dots that fly
  into the dot-matrix "NotWin".
- **Sample content only:** neutral app names, "Build finished", "Lo-fi set", made-up calendar dots. Nothing from your
  real notifications, calendar or files.

## Rundown (16:9, 48 s = 24 bars at 120 BPM, no voice)

| s | Beat | Line on screen | What we see |
|---|---|---|---|
| 0–4.5 | Taskbar → dock | "Your taskbar, redrawn" | A grey stock-style taskbar on a black desktop. Its pixels break into halftone dots that pull in and snap into the floating black NotWin dock: start, search, pinned apps, tray. |
| 4.5–10 | Dock behaviour | `HIDES AT THE EDGE` → `STEPS ASIDE FOR FULL SCREEN` | Camera close on the dock. A cursor touches the bottom edge, the dock rises; a window grows to full screen and the dock slides away under it, then returns. |
| 10–17 | Launcher | "Tap Win. Find anything" | A Win keycap presses. The launcher opens centre; "note" is typed, results rank apps → settings → files; then **Most used** and **All apps A–Z** roll past. |
| 17–23 | Quick settings + notifications | `WIN + A` | The red dot drops to the tray; the panel rises bottom right: Wi-Fi and Bluetooth tiles toggle, volume and brightness slide, two sample notifications stack, one is dismissed. |
| 23–31 | Notch + dashboard | "Rest on the notch" | Camera tilts to the top. The dot-matrix time in the black pill; the cursor rests, the notch opens into the dashboard: timer dots go out, media progress is dragged, clipboard, note; page 2: shelf, weather. |
| 32–40 | Widgets + Draw | "A desktop that's yours" | The dots swell into the light wallpaper. Widgets assemble on the dot grid one by one: calendar with event dots (today in red), Up next "IN 25 MIN", photo frame, tic-tac-toe (red X), notes, alarm, snake moving. Then the dashboard's Draw pen circles the clock and draws a heart in red. |
| 39–43 | Install | "Download. Double-click. Done" | Back to black. `NotWin-Setup.exe` lands, a double-click, a dot progress row fills, and the notch says "NotWin is installed". |
| 43–46 | End card | **NotWin** · "A Nothing OS-style shell for Windows 11" · `github.com/imsky1812/NotWin` | Everything on screen breaks into dots that fly into a dot-matrix "NotWin"; tagline and link come up beneath. Loops to frame 1. |

Square cut (1080×1080): same timeline, reframed camera; text moved inside the square safe area.

## Deliverables

- `promo/index.html`: the animation (double-click to play, loops; `?debug=1` adds a scrub bar).
- `promo/out/NotWin-promo-1080p.mp4` (1920×1080, 60 fps) and `NotWin-promo-square.mp4`; `promo/out/` is gitignored.

## Music

Synthesized in the page (`renderMusic()` in `index.html`, Web Audio `OfflineAudioContext`), so it's original and
renders the same every time. 120 BPM, F minor (Fm · D♭ · A♭ · E♭). Every animation action writes a cue on the
16th-note grid (`cue(t, kind)`), and the track plays a sound for each cue: key press, typing ticks, toggles, slider
sweeps, notification chimes, timer dots going out, widget plucks, pen scribbles, progress arpeggio. The bed: intro
pad and clock ticks → groove from the dock snap (4 s) → arps in the launcher → snare roll and riser into the drop at
32 s (light wallpaper) → filtered breakdown for setup → final hit as the wordmark forms.

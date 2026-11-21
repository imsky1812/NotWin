# NotWin promo

A 48-second promo for NotWin, built with the Bang Motion skill: one HTML page, no build step.

- **Watch:** double-click `index.html` (needs the `assets/` folder next to it). It plays with sound and loops;
  click once if the browser blocks autoplay with sound. **Space** pauses, **R** restarts, `?debug=1` adds a scrub bar,
  `?sq=1` gives the square 1080×1080 cut and `?v=1` the vertical 1080×1920 cut for phones.
- **Plan:** `STYLE-BRIEF.md` (concept, style, rundown, music).
- **Render MP4s** (from `promo/tools`, after `npm install`):

  ```powershell
  node wav.mjs ../out/notwin-beat.wav
  node export.mjs ../out/NotWin-promo-1080p.mp4 "" 60
  node export.mjs ../out/NotWin-promo-square.mp4 "&sq=1" 60
  node export.mjs ../out/NotWin-promo-vertical.mp4 "&v=1" 60
  node thumbs.mjs   # five 1600x900 X/Twitter thumbnails in out/thumbs
  ```

  `export.mjs` seeks every frame, screenshots it with Chrome and pipes it to ffmpeg (from the `ffmpeg-static`
  package), so no frame is dropped. Rendered files go to `out/`, which is gitignored.

All UI is rebuilt from the app (sizes and colours from the XAML and real screenshots) with neutral sample content.
Fonts: Space Grotesk and Space Mono (OFL, licences in `assets/fonts`).

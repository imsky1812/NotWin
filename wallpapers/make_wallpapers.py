"""Generate NotWin's Lucent wallpapers (soft, blurred, grainy; light and dark).

The Settings wallpaper picker and the older dot wallpapers were removed (user, 2026-11-26): the Lucent wallpaper now
follows the theme, and NotWin tints it from the photo frame at run time.

usage: python make_wallpapers.py [out_dir]
Writes 3840x2160 PNGs plus 1280x720 JPEG previews.
"""
import sys, os
import numpy as np
import cv2

W, H = 3840, 2160
OUT = sys.argv[1] if len(sys.argv) > 1 else os.path.dirname(os.path.abspath(__file__))


def radial(cx, cy, rad):
    y, x = np.mgrid[0:H, 0:W].astype(np.float32)
    d = np.sqrt((x - cx) ** 2 + (y - cy) ** 2) / rad
    return np.clip(1 - d, 0, 1)


def save(name, img):
    cv2.imwrite(os.path.join(OUT, name + '.png'), img)
    prev = cv2.resize(img, (1280, 720), interpolation=cv2.INTER_AREA)
    cv2.imwrite(os.path.join(OUT, name + '_preview.jpg'), prev, [cv2.IMWRITE_JPEG_QUALITY, 86])
    print('wrote', name)


# Lucent (Plan 13 follow-up, user 2026-11-26): soft, blurred, grainy fields like Nothing OS 5's "Lucent" home
# screens, one light and one dark. Large out-of-focus blobs with a faint tint, film grain, and nothing sharp behind
# the widgets (the frosted cards let this show through).
def lucent(seed, base, blobs, grain):
    img = np.zeros((H, W, 3), np.float32)
    img[:] = base
    r = np.random.default_rng(seed)
    for (fx, fy, fr, col, a) in blobs:
        m = radial(W * fx, H * fy, min(W, H) * fr) ** 2
        img += (np.array(col, np.float32) - base) * (m[:, :, None] * a)
    k = int(min(W, H) * 0.35) | 1
    img = cv2.GaussianBlur(img, (k, k), 0)
    img += r.normal(0, grain, (H, W, 1)).astype(np.float32)  # monochrome grain
    return np.clip(img, 0, 255).astype(np.uint8)


# BGR colours: soft warm grey, cool grey-lilac and a sage tint on light; charcoal with a faint warm and a deep red glow
# on dark (the one red accent).
img = lucent(51, np.array((226, 226, 228), np.float32),
             [(0.20, 0.22, 0.80, (206, 208, 220), 1.0), (0.80, 0.28, 0.75, (226, 196, 214), 1.0),
              (0.55, 0.90, 0.85, (196, 216, 200), 1.0), (0.98, 0.98, 0.45, (242, 242, 244), 0.9)], grain=3.0)
save('nothing_lucent_light', img)
img = lucent(53, np.array((18, 18, 20), np.float32),
             [(0.25, 0.30, 0.80, (38, 38, 42), 1.0), (0.80, 0.25, 0.70, (40, 32, 48), 0.9),
              (0.72, 0.92, 0.55, (26, 24, 84), 0.6), (0.10, 0.95, 0.55, (30, 34, 32), 0.9)], grain=2.6)
save('nothing_lucent_dark', img)

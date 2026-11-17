"""Generate Nothing-inspired wallpapers (dot-matrix halftone, monochrome + one red dot).

usage: python make_wallpapers.py [out_dir]
Writes 3840x2160 PNGs plus 1280x720 JPEG previews.
"""
import sys, os
import numpy as np
import cv2

W, H = 3840, 2160
OUT = sys.argv[1] if len(sys.argv) > 1 else os.path.dirname(os.path.abspath(__file__))
RED = (31, 35, 232)  # BGR for #e8231f
rng = np.random.default_rng(17)


def cloud_field(seed, scale=(9, 16), blur=0.18):
    """Smooth cloud-like field in 0..1 at full resolution."""
    r = np.random.default_rng(seed)
    acc = np.zeros((H, W), np.float32)
    amp, tot = 1.0, 0.0
    for octave in range(4):
        h, w = scale[0] * 2 ** octave, scale[1] * 2 ** octave
        n = r.random((h, w)).astype(np.float32)
        n = cv2.resize(n, (W, H), interpolation=cv2.INTER_CUBIC)
        acc += n * amp
        tot += amp
        amp *= 0.5
    acc /= tot
    k = int(min(W, H) * blur) | 1
    acc = cv2.GaussianBlur(acc, (k, k), 0)
    acc = (acc - acc.min()) / (acc.max() - acc.min())
    return acc


def radial(cx, cy, rad):
    y, x = np.mgrid[0:H, 0:W].astype(np.float32)
    d = np.sqrt((x - cx) ** 2 + (y - cy) ** 2) / rad
    return np.clip(1 - d, 0, 1)


def halftone(img, field, pitch, color, max_frac=0.46, min_r=0.6, gamma=1.0):
    rmax = pitch * max_frac
    for gy in range(pitch // 2, H, pitch):
        for gx in range(pitch // 2, W, pitch):
            v = float(field[gy, gx]) ** gamma
            r = v * rmax
            if r < min_r:
                continue
            cv2.circle(img, (gx * 4, gy * 4), int(round(r * 4)), color, -1, cv2.LINE_AA, shift=2)


def red_dot(img, gx, gy, r):
    cv2.circle(img, (gx * 4, gy * 4), int(r * 4), RED, -1, cv2.LINE_AA, shift=2)


def save(name, img):
    cv2.imwrite(os.path.join(OUT, name + '.png'), img)
    prev = cv2.resize(img, (1280, 720), interpolation=cv2.INTER_AREA)
    cv2.imwrite(os.path.join(OUT, name + '_preview.jpg'), prev, [cv2.IMWRITE_JPEG_QUALITY, 86])
    print('wrote', name)


# A · Halftone clouds, light ------------------------------------------------
P = 30
f = cloud_field(3)
f = np.clip((f - 0.32) / 0.68, 0, 1) * 0.85 + radial(W * 0.62, H * 0.45, W * 0.55) * 0.25
f = np.clip(f, 0, 1)
img = np.full((H, W, 3), 232, np.uint8)
halftone(img, f, P, (26, 26, 26), gamma=1.6)
red_dot(img, P // 2 + P * 108, P // 2 + P * 52, P * 0.46)
save('nothing_halftone_light', img)

# B · Halftone clouds, dark -------------------------------------------------
f = cloud_field(8)
f = np.clip((f - 0.38) / 0.62, 0, 1) * 0.9
img = np.full((H, W, 3), 10, np.uint8)
halftone(img, f, P, (236, 236, 236), gamma=1.8)
red_dot(img, P // 2 + P * 31, P // 2 + P * 18, P * 0.46)
save('nothing_halftone_dark', img)

# C · Glyph disc, dark -------------------------------------------------------
img = np.full((H, W, 3), 12, np.uint8)
Pg = 36
for gy in range(Pg // 2, H, Pg):            # faint background grid
    for gx in range(Pg // 2, W, Pg):
        cv2.circle(img, (gx * 4, gy * 4), int(2.2 * 4), (30, 30, 30), -1, cv2.LINE_AA, shift=2)
cx, cy, R = int(W * 0.66), int(H * 0.5), int(H * 0.36)
for gy in range(Pg // 2, H, Pg):            # the lit disc (Glyph Matrix style)
    for gx in range(Pg // 2, W, Pg):
        d = np.hypot(gx - cx, gy - cy)
        if d > R:
            continue
        ang = (np.degrees(np.arctan2(gy - cy, gx - cx)) + 360 + 90) % 360
        v = 0.25 + 0.75 * (1 - d / R) ** 0.6 * (0.55 + 0.45 * np.cos(np.radians(ang - 300)))
        c = int(40 + 210 * v)
        cv2.circle(img, (gx * 4, gy * 4), int(Pg * 0.36 * 4), (c, c, c), -1, cv2.LINE_AA, shift=2)
# ring outline of dots + red head
N = 96
for i in range(N):
    a = i / N * 2 * np.pi - np.pi / 2
    x, y = cx + (R + 70) * np.cos(a), cy + (R + 70) * np.sin(a)
    col = (60, 60, 60) if i > 70 else (225, 225, 225)
    if i == 70:
        col = RED
    cv2.circle(img, (int(x * 4), int(y * 4)), int(7 * 4), col, -1, cv2.LINE_AA, shift=2)
save('nothing_glyph_dark', img)

# D · Minimal light grid + one red dot -----------------------------------------
img = np.full((H, W, 3), 238, np.uint8)
Pm = 40
for gy in range(Pm // 2, H, Pm):
    for gx in range(Pm // 2, W, Pm):
        cv2.circle(img, (gx * 4, gy * 4), int(2.4 * 4), (205, 205, 205), -1, cv2.LINE_AA, shift=2)
red_dot(img, Pm // 2 + Pm * 71, Pm // 2 + Pm * 40, 14)
save('nothing_grid_light', img)

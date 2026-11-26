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


# E · Lucent (Plan 13 follow-up, user 2026-11-26): soft, blurred, grainy fields like Nothing OS 5's "Lucent" home
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

"""Bakes the data for the 3D colony island: a height grid, the river with its waterfall, and where trees and rocks stand.

Run with Blender's Python (it has numpy and Pillow):
  "C:/Program Files/Blender Foundation/Blender 5.2/5.2/python/bin/python.exe" Tools/Render/bake_island3d.py [ISLAND_ART.png]

ISLAND_ART.png is the painted island (default Tools/Render/reference/AlpineIsland.png): only its outline is used, so the
3D coast follows the painted one. Everything else comes from Assets/Resources/Island/IslandLayout.json (district
centers, roads) and the river traced below over the painting.

Island ground coordinates (a, b) are tiles: a picture pixel (px, py) of the 26-pixels-per-tile plate is
a = sx - 2 sy, b = -sx - 2 sy with sx = (px - W/2) / 26 and sy = (H/2 - py) / 26, the inverse of the 2:1 view.
A zone's tile (x, y) is (a0 + x - cx, b0 + y - cy) around its center. Unity places (a, b) at world (a, height, -b).

Outputs, in Assets/Resources/Island3D:
  Height.bytes   uint16 little endian, N x N, row j (b) by column i (a); height = HMIN + v / 65535 * (HMAX - HMIN)
  Features.json  grid description, river (points with water level), waterfalls, trees and rocks
"""
import json
import math
import struct
import sys
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / "Assets/Resources/Island3D"
LAYOUT = json.loads((ROOT / "Assets/Resources/Island/IslandLayout.json").read_text(encoding="utf-8"))
W, H, TILE = LAYOUT["width"], LAYOUT["height"], LAYOUT["pixelsPerTile"]

N, A0, STEP = 289, -72.0, .5
HMIN, HMAX = -3.0, 20.0
FLAT = 0.0
SEA = -1.9
rng = np.random.default_rng(9)


def ab(px, py):
    sx, sy = (px - W / 2) / TILE, (H / 2 - py) / TILE
    return sx - 2 * sy, -sx - 2 * sy


def pixel(a, b):
    return W / 2 + (a - b) * TILE * .5, H / 2 + (a + b) * TILE * .25


grid_a = A0 + np.arange(N) * STEP
GA, GB = np.meshgrid(grid_a, grid_a)  # GA[j, i] = a, GB[j, i] = b


# ---- Noise --------------------------------------------------------------------------------

def value_noise(scale, seed, octaves=3):
    total = np.zeros_like(GA)
    amp, freq, norm = 1.0, 1.0 / scale, 0
    r = np.random.default_rng(seed)
    for _ in range(octaves):
        size = int((grid_a[-1] - grid_a[0]) * freq) + 3
        lattice = r.random((size, size)) * 2 - 1
        x = (GA - grid_a[0]) * freq
        y = (GB - grid_a[0]) * freq
        x0, y0 = np.floor(x).astype(int), np.floor(y).astype(int)
        fx, fy = x - x0, y - y0
        fx, fy = fx * fx * (3 - 2 * fx), fy * fy * (3 - 2 * fy)
        v00, v10 = lattice[y0, x0], lattice[y0, x0 + 1]
        v01, v11 = lattice[y0 + 1, x0], lattice[y0 + 1, x0 + 1]
        total += amp * ((v00 * (1 - fx) + v10 * fx) * (1 - fy) + (v01 * (1 - fx) + v11 * fx) * fy)
        norm += amp
        amp *= .5
        freq *= 2
    return total / norm


def smooth(t):
    t = np.clip(t, 0, 1)
    return t * t * (3 - 2 * t)


# ---- Features -----------------------------------------------------------------------------

CENTERS = [ab(c["x"], c["y"]) for c in LAYOUT["centers"]]
ROADS = [[ab(p["x"], p["y"]) for p in r["points"]] for r in LAYOUT["roads"]]
# The painted river, from the snow under the peak down to the falls on the east coast (picture pixels).
RIVER_PX = [(925, 160), (955, 195), (995, 225), (1018, 262), (995, 300), (962, 332), (950, 380), (972, 422),
            (998, 468), (965, 515), (948, 560), (985, 598), (1055, 615), (1125, 638), (1185, 655), (1225, 695),
            (1265, 735), (1290, 785), (1300, 815)]
RIVER = [ab(*p) for p in RIVER_PX]
PEAK = ab(662, 128)
PEAK_HEIGHT, PEAK_SIGMA = 11.5, 7.5
MINES = 4  # Sector index whose landmark plaza is the open pit.


def densify(points, pitch=.35):
    out = []
    for (ax, ay), (bx, by) in zip(points, points[1:]):
        n = max(1, int(math.hypot(bx - ax, by - ay) / pitch))
        out += [(ax + (bx - ax) * k / n, ay + (by - ay) * k / n) for k in range(n)]
    out.append(points[-1])
    return np.array(out, dtype=np.float32)


def distance_to(points):
    """Distance from every grid point to the nearest of these points, in chunks."""
    best = np.full(GA.shape, 1e9, dtype=np.float32)
    flat_a, flat_b = GA.ravel().astype(np.float32), GB.ravel().astype(np.float32)
    out = best.ravel()
    for k in range(0, len(points), 256):
        chunk = points[k:k + 256]
        d = (flat_a[:, None] - chunk[None, :, 0]) ** 2 + (flat_b[:, None] - chunk[None, :, 1]) ** 2
        np.minimum(out, d.min(axis=1), out=out)
    return np.sqrt(best)


def district_distance():
    d = np.full(GA.shape, 1e9)
    for a0, b0 in CENTERS:
        zone = np.maximum(np.abs(GA - a0), np.abs(GB - b0)) - 4.5
        plaza = np.maximum(np.abs(GA - (a0 - 6)), np.abs(GB - (b0 - 6))) - 2.3
        d = np.minimum(d, np.maximum(0, np.minimum(zone, plaza)))
    return d


def land_mask(art_path):
    """1 on the painted island's land (river included), 0 on the sea, blurred so the coast is smooth."""
    art = np.asarray(Image.open(art_path).convert("RGB").resize((W, H)), dtype=np.int16)
    r, g, b = art[..., 0], art[..., 1], art[..., 2]
    sea_like = (b > r + 25) & (r < 90) & (g < 150)
    mask = Image.fromarray(np.where(sea_like, 0, 255).astype(np.uint8))
    # Only water reached from the picture's border is sea; the river and pools inside stay land.
    flood = mask.copy()
    for corner in ((0, 0), (W - 1, 0), (0, H - 1), (W - 1, H - 1)):
        if flood.getpixel(corner) == 0:
            ImageDraw.floodfill(flood, corner, 128)
    land = np.asarray(flood) != 128
    soft = Image.fromarray((land * 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(5))
    soft = np.asarray(soft, dtype=np.float32) / 255
    px, py = pixel(GA, GB)
    inside = (px >= 0) & (px < W) & (py >= 0) & (py < H)
    sample = np.zeros(GA.shape, dtype=np.float32)
    sample[inside] = soft[py[inside].astype(int), px[inside].astype(int)]
    return sample


def main():
    art = sys.argv[1] if len(sys.argv) > 1 else Path(__file__).resolve().parent / "reference/AlpineIsland.png"
    land = land_mask(art)
    dd = district_distance()
    dr = distance_to(np.concatenate([densify(r) for r in ROADS]))
    river_pts = densify(RIVER, .25)
    dw = distance_to(river_pts)

    open_ground = smooth((dd - .2) / 2.4) * smooth((dr - .6) / 1.8) * smooth((dw - .9) / 1.6)
    hills = np.maximum(0, value_noise(9, 1) + .15) * 1.6 + value_noise(2.2, 2) * .12
    pa, pb = PEAK
    d2 = (GA - pa) ** 2 + (GB - pb) ** 2
    mountain = PEAK_HEIGHT * np.exp(-d2 / (2 * PEAK_SIGMA ** 2))
    ridges = (1 - np.abs(value_noise(3.2, 5, 4))) ** 3 * mountain * .5
    # Rocky ridges along the back and side edges, as in the painting.
    rim = smooth((.85 - land) / .3) * smooth((land - .35) / .2) * (np.maximum(0, value_noise(5, 7)) * 2.4 + .4)
    z = FLAT + (hills + mountain + ridges + rim) * open_ground

    # River bed and banks; the water surface follows the bed down the mountain.
    bed = FLAT - .45
    z = z * smooth(dw / .9) + np.minimum(z, bed + (z - bed) * .3) * (1 - smooth(dw / .9))
    # The mines' open pit, in terraces, on the landmark plaza behind the district.
    ma, mb = CENTERS[MINES]
    r = np.hypot(GA - (ma - 6), GB - (mb - 6))
    pit = r < 2.4
    z[pit] = FLAT - .3 * (1 + np.floor((2.4 - r[pit]) / .45))

    # Cliffs into the sea along the painted coast.
    cliff = smooth((.6 - land) / .22)
    z = z * (1 - cliff) + (SEA - .8) * cliff

    OUT.mkdir(parents=True, exist_ok=True)
    q = np.clip((z - HMIN) / (HMAX - HMIN) * 65535, 0, 65535).astype("<u2")
    (OUT / "Height.bytes").write_bytes(q.tobytes())

    def height_at(a, b):
        i = min(N - 2, max(0, (a - A0) / STEP))
        j = min(N - 2, max(0, (b - A0) / STEP))
        i0, j0 = int(i), int(j)
        fi, fj = i - i0, j - j0
        return float(z[j0, i0] * (1 - fi) * (1 - fj) + z[j0, i0 + 1] * fi * (1 - fj) + z[j0 + 1, i0] * (1 - fi) * fj + z[j0 + 1, i0 + 1] * fi * fj)

    # River surface: just over its bed, never climbing on the way down; it ends at the coast in a waterfall.
    river, level = [], 1e9
    for a, b in river_pts[::2]:
        here = height_at(a, b)
        if here < SEA + .3:
            break
        level = min(level, here + .22)
        river.append([round(float(a), 2), round(float(b), 2), round(level, 3)])
    end = river[-1]
    before = river[-4]
    da, db = end[0] - before[0], end[1] - before[1]
    length = math.hypot(da, db)
    falls = [{"a": end[0], "b": end[1], "dirA": da / length, "dirB": db / length, "top": end[2], "bottom": SEA}]

    def scatter(count, ok, seed):
        r = np.random.default_rng(seed)
        found = []
        for _ in range(count):
            i, j = r.random() * (N - 1), r.random() * (N - 1)
            ii, jj = int(i), int(j)
            if not ok(ii, jj):
                continue
            a, b = A0 + i * STEP, A0 + j * STEP
            found.append((a, b, height_at(a, b), r))
        return found

    clumps = value_noise(6, 11)
    trees = []
    for a, b, y, r in scatter(26000, lambda i, j: land[j, i] > .8 and dd[j, i] > 1.0 and dr[j, i] > .9 and dw[j, i] > 1.0
                              and z[j, i] < 6.5 and z[j, i] > SEA + .5 and clumps[j, i] > -.15, 3):
        kind = 2 if y < 1.0 and r.random() < .25 else (0 if r.random() < .6 else 1)
        trees += [round(a, 2), round(b, 2), round(y, 2), round(float(r.uniform(.75, 1.3)), 2), kind]
    rocks = []
    for a, b, y, r in scatter(5000, lambda i, j: land[j, i] > .75 and dd[j, i] > .8 and dr[j, i] > 1.0 and dw[j, i] > 1.0, 4):
        rocks += [round(a, 2), round(b, 2), round(y, 2), round(float(r.uniform(.2, .6)), 2), int(r.integers(0, 3))]

    features = {
        "grid": {"n": N, "a0": A0, "step": STEP, "hmin": HMIN, "hmax": HMAX},
        "sea": SEA, "river": [v for p in river for v in p], "riverWidth": 1.1, "falls": falls,
        "trees": trees, "rocks": rocks, "peak": [round(pa, 2), round(pb, 2)],
    }
    (OUT / "Features.json").write_text(json.dumps(features, separators=(",", ":")), encoding="utf-8")
    print(f"BAKED trees={len(trees) // 5} rocks={len(rocks) // 5} river={len(river)} height {z.min():.2f}..{z.max():.2f}")


if __name__ == "__main__":
    main()

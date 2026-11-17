"""Renders the big colony island: the whole map as background pieces and a sharp close-up of every zone.

Usage:
  blender --background --factory-startup --python Tools/Render/render_island.py -- OUTPUT_DIR bg|zones [night] [preview] [names]

  bg     colony_bg_<i>_<j>.png: 2 x 2 pieces of 44 x 24 Unity units (4096 pixels wide each) around the map center.
  zones  colony_zone_<n>.png: 11 x 6.5 units (2048 pixels wide) centered on each zone, with soft edges.
  names  optional, to render only some: "bg_0_1", "zone_3".

Tile (x, y) of the 63 x 63 grid sits at Blender (x, -y); the camera is the same 2:1 dimetric view as the
sprites. Zones are 9 x 9 tiles; the list must match CampaignData.cs (ZoneCenters). The zones stand on raised
platforms with their paving, the two streets that cross them and a plaza for the landmark in the far quarter.
"""
import math
import os
import random
import sys

import bpy
from mathutils import Vector, noise

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import render_colony as rc  # noqa: E402
import render_sprites as rs  # noqa: E402
from render_sprites import box, cyl, glowing, material  # noqa: E402

OUT = rc.OUT
ARGS = rc.EXTRA
NIGHT = "night" in ARGS
PREVIEW = "preview" in ARGS
NAMES = [a for a in ARGS if a.startswith("bg_") or a.startswith("zone_")]
K = rc.K

N = 63
CENTER = (N - 1) / 2
ZONES = [(51, 51), (53, 35), (30, 48), (39, 25), (18, 35), (25, 16), (9, 21), (9, 7)]
HALF = 4.5  # Half the side of a zone.
COAST = 30.5  # Chebyshev distance from the map center (in tiles) where the land drops into the sea.
RIVER = [(19.5, 14.2), (16.0, 13.8), (12.0, 14.3), (8.0, 14.0), (4.0, 14.2), (0.0, 14.0), (-4.0, 14.2)]
GROUND = rc.GROUND
BG_W, BG_H, BG_PX = 44.0, 24.0, 4096
ZONE_W, ZONE_H, ZONE_PX = 11.0, 6.5, 2048


def zone_distance(gx, gy):
    """Chebyshev distance from a tile position to the nearest zone edge (0 inside)."""
    return min(max(0, max(abs(gx - cx), abs(gy - cy)) - HALF) for cx, cy in ZONES)


def in_zone(gx, gy):
    return zone_distance(gx, gy) <= 0


def road_tiles():
    tiles = set()
    for (x, y), (tx, ty) in zip(ZONES, ZONES[1:]):
        while x != tx:
            x += 1 if tx > x else -1
            tiles.add((x, y))
        while y != ty:
            y += 1 if ty > y else -1
            tiles.add((x, y))
    return {t for t in tiles if not in_zone(*t)}


ROADS = road_tiles()


def road_distance(gx, gy):
    """Chebyshev distance to the nearest road tile, looking only at the tiles around (9 when none is near)."""
    best = 9.0
    ix, iy = round(gx), round(gy)
    for x in range(ix - 3, ix + 4):
        for y in range(iy - 3, iy + 4):
            if (x, y) in ROADS:
                best = min(best, max(abs(gx - x), abs(gy - y)))
    return best


def river_distance(gx, gy):
    return min(rc.distance_to_segment(gx, gy, a, b) for a, b in zip(RIVER, RIVER[1:]))


def coast_metric(gx, gy):
    wobble = noise.noise(Vector((gx * .09, gy * .09, 3.1))) * 3.0 + noise.noise(Vector((gx * .4, gy * .4, 7))) * .6
    return max(abs(gx - CENTER), abs(gy - CENTER)) + wobble


def ground_z(gx, gy):
    """Height of the bare land at a tile position, below the platforms."""
    dz = zone_distance(gx, gy)
    dr = road_distance(gx, gy)
    dw = river_distance(gx, gy)
    # Rocky hills rise away from the platforms, walkways and river.
    open_ground = min(1, max(0, dz - .5) / 2.5) * min(1, max(0, dr - .6) / 1.5) * min(1, max(0, dw - .9) / 1.5)
    hill = (noise.noise(Vector((gx * .12, gy * .12, 1.7))) + .35) * 1.3 * open_ground
    rough = noise.noise(Vector((gx * .9, gy * .9, 4.2))) * .12 * open_ground
    z = GROUND + max(0, hill) + rough
    if dw < .55:
        z = GROUND - .18  # River bed.
    cliff = max(0, min(1, (coast_metric(gx, gy) - COAST) / .7))
    return z * (1 - cliff) + (-2.6) * (cliff * cliff * (3 - 2 * cliff))


def build_ground():
    size = 150
    bpy.ops.mesh.primitive_grid_add(x_subdivisions=440, y_subdivisions=440, size=size, location=(CENTER, -CENTER, 0))
    ground = bpy.context.active_object
    for v in ground.data.vertices:
        v.co.z = ground_z(v.co.x + CENTER, -(v.co.y - CENTER))
    bpy.ops.object.shade_smooth()
    ground.data.materials.append(rc.island_material())


def build_platforms():
    """A raised platform under every zone: walls with amber lights, paving, two streets and the landmark's plaza."""
    m = rc.platform_mats()
    seam = material("Seam", "#4c565d", 0, .8, .3)
    asphalt = material("Asphalt", "#2f353a", 0, .85, .5)
    mark = material("Mark", "#d8c27a", 0, .6, .2, emission="#d8c27a", strength=.25)
    plaza = material("Plaza", "#5b666e", 0, .7, .4)
    side = HALF * 2
    for cx, cy in ZONES:
        x, y = cx, -cy
        box((x, y, -.32), (side + .08, side + .08, .64), m["wall"], bevel=.03)
        box((x, y, -.01), (side, side, .04), m["deck"], bevel=.01)
        # Paving seams between the tiles.
        for k in range(1, 9):
            t = -HALF + k
            box((x + t, y, .011), (.025, side - .1, .004), seam, bevel=0)
            box((x, y + t, .011), (side - .1, .025, .004), seam, bevel=0)
        # The plaza of the far quarter (lower x and y in tiles: -x and +y here).
        box((x - 2.5, y + 2.5, .012), (3.9, 3.9, .006), plaza, bevel=.002)
        # Streets through the center, with dashed marks.
        box((x, y, .014), (side, .82, .008), asphalt, bevel=.002)
        box((x, y, .014), (.82, side, .008), asphalt, bevel=.002)
        for k in range(-4, 5):
            if k == 0:
                continue
            box((x + k, y, .02), (.36, .045, .004), mark, bevel=0)
            box((x, y + k, .02), (.045, .36, .004), mark, bevel=0)
        for s in (-1, 1):
            box((x + s * (HALF + .03), y, -.06), (.05, side + .1, .1), m["trim"], bevel=.01)
            box((x, y + s * (HALF + .03), -.06), (side + .1, .05, .1), m["trim"], bevel=.01)
            for k in range(int(side * 2) + 1):
                t = -HALF + k * .5
                box((x + s * (HALF + .06), y + t, -.09), (.02, .1, .04), m["amber"], bevel=0)
                box((x + t, y + s * (HALF + .06), -.09), (.1, .02, .04), m["amber"], bevel=0)
        for t in (-3.6, -1.2, 1.2, 3.6):  # Buttresses down the walls.
            for s in (-1, 1):
                box((x + s * (HALF + .08), y + t, -.4), (.12, .28, .6), m["trim"], bevel=.02)
                box((x + t, y + s * (HALF + .08), -.4), (.28, .12, .6), m["trim"], bevel=.02)


def build_walkways():
    """Raised walkways along the road tiles, with a dim line on each side that Unity lights once linked."""
    m = rc.platform_mats()
    for gx, gy in ROADS:
        x, y = gx, -gy
        dirs = [(dx, dy) for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)) if (gx + dx, gy + dy) in ROADS or in_zone(gx + dx, gy + dy)]
        box((x, y, -.1), (.74, .74, .2), m["deck"], bevel=.02)
        for dx, dy in dirs:
            ox, oy = dx * .37, -dy * .37
            box((x + ox, y + oy, -.1), (.26, .74, .2) if dx else (.74, .26, .2), m["deck"], bevel=.01)
            for s in (-1, 1):
                if dx:
                    box((x + ox, y + s * .3, .002), (.5, .03, .004), m["line"], bevel=0)
                else:
                    box((x + s * .3, y + oy, .002), (.03, .5, .004), m["line"], bevel=0)
        cyl((x, y, (GROUND - .2) / 2 - .1), .09, abs(GROUND) + .2, m["pillar"], bevel=.01, vertices=10)
        box((x, y, -.22), (.8, .8, .04), m["trim"], bevel=.01)


def joined(make, name):
    """Runs a builder that adds objects and joins them into one template object, moved out of sight."""
    before = set(bpy.data.objects)
    make()
    made = [o for o in bpy.data.objects if o not in before]
    bpy.ops.object.select_all(action="DESELECT")
    for o in made:
        o.select_set(True)
    bpy.context.view_layer.objects.active = made[0]
    if len(made) > 1:
        bpy.ops.object.join()
    obj = bpy.context.view_layer.objects.active
    obj.name = name
    obj.location = (0, 0, -50)
    return obj


def copy_at(template, location, scale=(1, 1, 1), rotation=(0, 0, 0)):
    """A cheap copy that shares the template's mesh."""
    obj = template.copy()
    obj.location = location
    obj.scale = scale
    obj.rotation_euler = rotation
    bpy.context.collection.objects.link(obj)
    return obj


def build_nature():
    rng = random.Random(5)
    trunk = material("Trunk", "#3a2a22", .1, .9, .3)
    pines = [joined(lambda leaf=leaf: rc.pine(0, 0, 1, leaf, trunk), "PineTemplate")
             for leaf in (material("Pine", "#2e4630", .05, .8, .4), material("PineLight", "#3b5a37", .05, .8, .4))]

    def stone(subdivisions, mat):
        bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=subdivisions, radius=1, location=(0, 0, 0))
        rs.finish(bpy.context.active_object, mat, 0)

    pebble = joined(lambda: stone(2, material("Rock", "#4f453e", .1, .85, .6)), "PebbleTemplate")
    crag = joined(lambda: stone(1, material("Crag", "#5a524b", .1, .88, .7)), "CragTemplate")
    lo, hi = -4, N + 3
    for _ in range(6200):  # Pine woods on open ground, in clumps.
        gx, gy = rng.uniform(lo, hi), rng.uniform(lo, hi)
        if coast_metric(gx, gy) > COAST - .6 or zone_distance(gx, gy) < .7 or river_distance(gx, gy) < 1 or road_distance(gx, gy) < .9:
            continue
        if noise.noise(Vector((gx * .14, gy * .14, 9))) < -.05:
            continue
        s = rng.uniform(.7, 1.3)
        copy_at(pines[0 if rng.random() < .6 else 1], (gx, -gy, ground_z(gx, gy)), (s, s, s), (0, 0, rng.uniform(0, 6.3)))
    for _ in range(3200):  # Loose rocks.
        gx, gy = rng.uniform(lo, hi), rng.uniform(lo, hi)
        if coast_metric(gx, gy) > COAST - .3 or zone_distance(gx, gy) < .35 or road_distance(gx, gy) < .5:
            continue
        r = rng.uniform(.06, .24)
        copy_at(pebble, (gx, -gy, ground_z(gx, gy) + r * .3), (r, r * rng.uniform(.6, 1.3), r * rng.uniform(.4, .8)))
    for _ in range(1700):  # Big jagged boulders and crags between the platforms.
        gx, gy = rng.uniform(lo, hi), rng.uniform(lo, hi)
        if coast_metric(gx, gy) > COAST - .8 or zone_distance(gx, gy) < .6 or river_distance(gx, gy) < 1.1 or road_distance(gx, gy) < 1.0:
            continue
        if noise.noise(Vector((gx * .2, gy * .2, 2.2))) < .05:
            continue
        r = rng.uniform(.25, .7)
        copy_at(crag, (gx, -gy, ground_z(gx, gy) + r * .25), (r * rng.uniform(.8, 1.3), r * rng.uniform(.8, 1.3), r * rng.uniform(.6, 1.2)),
                (rng.uniform(0, 1), rng.uniform(0, 1), rng.uniform(0, 6.3)))


def build_water():
    water = rc.water_material()
    bpy.ops.mesh.primitive_plane_add(size=1, location=(CENTER, -CENTER, -1.9))
    sea = bpy.context.active_object
    sea.scale = (700, 700, 1)
    sea.data.materials.append(water)
    foam = material("Foam", "#d8eef2", 0, .3, 0, emission="#bfe8f2", strength=.25)
    for (ax, ay), (bx, by) in zip(RIVER, RIVER[1:]):
        length = math.hypot(bx - ax, by - ay)
        box(((ax + bx) / 2, -(ay + by) / 2, GROUND - .11), (length + .2, .7, .01), water, bevel=0, rot=math.atan2(-(by - ay), bx - ax))
    # A pond where the river springs, and a waterfall where it leaves the island.
    cyl((RIVER[0][0] + .6, -RIVER[0][1], GROUND - .11), 1.3, .01, water, bevel=0, vertices=32)
    for gx in [g / 10 for g in range(120, -90, -1)]:
        if coast_metric(gx, 14.1) > COAST:
            drop = Vector((gx - 1.1, -14.1, -1.9)) - Vector((gx + .2, -14.1, GROUND - .1))
            bpy.ops.mesh.primitive_plane_add(size=1, location=(gx - .45, -14.1, (GROUND - 2) / 2))
            sheet = bpy.context.active_object
            sheet.scale = (drop.length, .7, 1)
            sheet.rotation_euler = (0, math.atan2(-drop.z, -drop.x), 0)
            rs.finish(sheet, foam, 0)
            for k in range(7):
                rs.ball((gx - 1.15 - (k % 3) * .15, -14.1 + (k % 3 - 1) * .25, -1.88), .14 + (k % 2) * .05, foam)
            break


def build_scene():
    rs.clear()
    rs.SCENE_SIZE = (512, 512)
    rs.setup(10)
    if NIGHT:
        rc.apply_night()
    scene = bpy.context.scene
    scene.render.film_transparent = False
    for obj in list(scene.objects):
        if obj.type == "MESH":
            bpy.data.objects.remove(obj)
    build_ground()
    build_platforms()
    build_walkways()
    build_water()
    build_nature()


def aim(target, width_units, pixels, height_units, samples):
    """Points the dimetric camera at a ground point, framing width x height Unity units."""
    scene = bpy.context.scene
    cam = scene.camera
    cam.rotation_euler = rc.ISO_ROT
    bpy.context.view_layer.update()
    cam.location = Vector(target) - rc.camera_forward() * 260
    cam.data.ortho_scale = width_units * K
    cam.data.clip_start = 1
    cam.data.clip_end = 1200
    scale = 4 if PREVIEW else 1
    scene.render.resolution_x = pixels // scale
    scene.render.resolution_y = round(pixels * height_units / width_units) // scale
    scene.render.resolution_percentage = 100
    scene.cycles.samples = 16 if PREVIEW else samples


def soften_edges(path, margin=.07):
    """Fades the picture's alpha to nothing along its edges, so the close-up melts into the background."""
    import numpy as np
    img = bpy.data.images.load(path)
    w, h = img.size
    px = np.array(img.pixels[:], dtype=np.float32).reshape(h, w, 4)
    ramp_x = np.clip(np.minimum(np.arange(w), w - 1 - np.arange(w)) / (w * margin), 0, 1)
    ramp_y = np.clip(np.minimum(np.arange(h), h - 1 - np.arange(h)) / (h * margin * w / h), 0, 1)
    px[:, :, 3] = np.outer(ramp_y, ramp_x)
    img.pixels = px.ravel().tolist()
    img.filepath_raw = path
    img.file_format = "PNG"
    img.save()
    bpy.data.images.remove(img)


def render(name):
    path = os.path.join(OUT, name + ("_night" if NIGHT else "") + ("_preview" if PREVIEW else "") + ".png")
    bpy.context.scene.render.filepath = path
    bpy.ops.render.render(write_still=True)
    return path


if __name__ == "__main__":
    os.makedirs(OUT, exist_ok=True)
    build_scene()
    scene = bpy.context.scene
    scene.render.image_settings.color_mode = "RGBA"
    if "zones" in ARGS:
        for n, (cx, cy) in enumerate(ZONES, 1):
            name = f"zone_{n}"
            if NAMES and name not in NAMES:
                continue
            aim((cx, -cy, 0), ZONE_W, ZONE_PX, ZONE_H, 64)
            soften_edges(render("colony_" + name))
    if "bg" in ARGS:
        aim((CENTER, -CENTER, 0), BG_W, BG_PX, BG_H, 36)
        cam = scene.camera
        base = cam.location.copy()
        right = cam.matrix_world.to_3x3() @ Vector((1, 0, 0))
        up = cam.matrix_world.to_3x3() @ Vector((0, 1, 0))
        for j in range(2):  # j counts rows from the top, i columns from the left.
            for i in range(2):
                name = f"bg_{i}_{j}"
                if NAMES and name not in NAMES:
                    continue
                cam.location = base + right * (i - .5) * BG_W * K + up * (.5 - j) * BG_H * K
                render("colony_" + name)
    print("RENDER_DONE")

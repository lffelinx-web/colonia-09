"""Renders the isometric colony: the terrain and every building, tile and decoration sprite.

Usage:
  blender --background --factory-startup --python Tools/Render/render_colony.py -- OUTPUT_DIR [names] [preview]

Tiles: tile (x, y) of the 26 x 26 grid sits at Blender (x, -y) on the ground. The camera looks
down at 30 degrees from the south-east corner (a 2:1 dimetric view), so one tile is a diamond one
Unity unit wide and half a unit tall; one Unity unit is sqrt(2) Blender units. The zone list must
match CampaignData.cs (ZoneX, ZoneY).
"""
import math
import os
import random
import sys

import bpy
from mathutils import Vector

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import render_sprites as rs  # noqa: E402
import render_map  # noqa: E402
from render_sprites import ball, box, cyl, glowing, glowing_hot, material, prism, regular, torus, transparent  # noqa: E402

OUT = sys.argv[sys.argv.index("--") + 1]
EXTRA = sys.argv[sys.argv.index("--") + 2:]
PREVIEW = "preview" in EXTRA
OVERVIEW = "overview" in EXTRA
CENTER = "center" in EXTRA
ONLY = [a for a in EXTRA if a not in ("preview", "night", "overview", "center")]
N = 26
ZONES = [(22, 22), (23, 15), (13, 21), (17, 11), (8, 15), (11, 7), (4, 9), (4, 3)]
K = math.sqrt(2)
ISO_ROT = (math.radians(60), 0, math.radians(45))
TERRAIN_W, TERRAIN_H = 28.0, 15.0
# The terrain is rendered in 3 x 3 pieces so it stays sharp when the player zooms in.
CHUNKS = 3
CHUNK_PX = 2048
# A wide, softer picture of the whole island and the sea around it, drawn behind the sharp pieces so the
# player can zoom out past their edges. Every edge of it is open sea, which Unity continues as a flat color.
OVERVIEW_W, OVERVIEW_H, OVERVIEW_PX = 44.0, 30.0, 3072


def camera_forward():
    cam = bpy.context.scene.camera
    return (cam.matrix_world.to_3x3() @ Vector((0, 0, -1))).normalized()


def iso_camera(target, ortho):
    cam = bpy.context.scene.camera
    cam.rotation_euler = ISO_ROT
    bpy.context.view_layer.update()
    cam.location = Vector(target) - camera_forward() * 60
    cam.data.ortho_scale = ortho
    cam.data.clip_end = 300


def in_zone(x, y, margin=0.0):
    return any(abs(x - cx) <= 2.5 + margin and abs(-y - cy) <= 2.5 + margin for cx, cy in ZONES)


def beam(p0, p1, radius, mat):
    """Round bar between two 3D points."""
    a, b = Vector(p0), Vector(p1)
    d = b - a
    bpy.ops.mesh.primitive_cylinder_add(vertices=12, radius=radius, depth=d.length, location=(a + b) / 2)
    obj = bpy.context.active_object
    obj.rotation_mode = "QUATERNION"
    obj.rotation_quaternion = d.to_track_quat("Z", "Y")
    return rs.finish(obj, mat, 0, smooth=True)


# ---- Lighting: day and night --------------------------------------------------------

NIGHT = "night" in EXTRA


def apply_night():
    """Moonlit night: dark blue sky light and a pale moon; glowing materials keep their light."""
    scene = bpy.context.scene
    bg = scene.world.node_tree.nodes["Background"]
    bg.inputs[0].default_value = (.06, .1, .2, 1)
    bg.inputs[1].default_value = .55
    for obj in scene.objects:
        if obj.type == "LIGHT":
            if obj.name.startswith("Key"):
                obj.data.energy = .85
                obj.data.color = (.62, .74, 1)
            else:
                obj.data.energy = .18
                obj.data.color = (.5, .6, 1)


def suffix():
    return "_night" if NIGHT else ""


# ---- Island terrain -------------------------------------------------------------------

COAST = 16.3  # Chebyshev distance from the map center (in tiles) where the land drops into the sea.
RIVER = [(8.6, 6.0), (6.5, 6.2), (4.0, 5.9), (1.0, 6.1), (-2.5, 6.0), (-6.0, 6.2)]  # Tile coordinates.


def road_tiles():
    tiles = set()
    for (x, y), (tx, ty) in zip(ZONES, ZONES[1:]):
        while x != tx:
            x += 1 if tx > x else -1
            tiles.add((x, y))
        while y != ty:
            y += 1 if ty > y else -1
            tiles.add((x, y))
    return tiles


def distance_to_segment(px, py, a, b):
    ax, ay = a
    bx, by = b
    dx, dy = bx - ax, by - ay
    t = max(0, min(1, ((px - ax) * dx + (py - ay) * dy) / (dx * dx + dy * dy)))
    return math.hypot(px - ax - t * dx, py - ay - t * dy)


def river_distance(gx, gy):
    return min(distance_to_segment(gx, gy, a, b) for a, b in zip(RIVER, RIVER[1:]))


def zone_distance(gx, gy):
    """Chebyshev distance from a tile position to the nearest zone edge (0 inside)."""
    return min(max(0, max(abs(gx - cx), abs(gy - cy)) - 2.5) for cx, cy in ZONES)


def coast_metric(gx, gy):
    from mathutils import noise
    wobble = noise.noise(Vector((gx * .18, gy * .18, 3.1))) * 2.2 + noise.noise(Vector((gx * .6, gy * .6, 7))) * .5
    return max(abs(gx - 12.5), abs(gy - 12.5)) + wobble


def island_material():
    """Rocky soil with patches of moss, bare rock on steep faces."""
    mat = bpy.data.materials.new("Island")
    mat.use_nodes = True
    nodes, links = mat.node_tree.nodes, mat.node_tree.links
    bsdf = next(n for n in nodes if n.type == "BSDF_PRINCIPLED")
    bsdf.inputs["Roughness"].default_value = .93
    coords = nodes.new("ShaderNodeTexCoord")
    broad = nodes.new("ShaderNodeTexNoise")
    broad.inputs["Scale"].default_value = .22
    broad.inputs["Detail"].default_value = 8
    links.new(coords.outputs["Object"], broad.inputs["Vector"])
    soil = rs.mix_color(nodes, links, broad.outputs[0], rs.srgb("#4f3d30"), rs.srgb("#6b5139"))
    moss_mask = nodes.new("ShaderNodeMapRange")
    moss_mask.inputs["From Min"].default_value = .48
    moss_mask.inputs["From Max"].default_value = .62
    moss_noise = nodes.new("ShaderNodeTexNoise")
    moss_noise.inputs["Scale"].default_value = .45
    moss_noise.inputs["Detail"].default_value = 10
    links.new(coords.outputs["Object"], moss_noise.inputs["Vector"])
    links.new(moss_noise.outputs[0], moss_mask.inputs["Value"])
    color = rs.mix_color(nodes, links, moss_mask.outputs[0], soil, rs.srgb("#3d5232"))
    geometry = nodes.new("ShaderNodeNewGeometry")
    split = nodes.new("ShaderNodeSeparateXYZ")
    links.new(geometry.outputs["Normal"], split.inputs[0])
    steep = nodes.new("ShaderNodeMapRange")
    steep.inputs["From Min"].default_value = .85
    steep.inputs["From Max"].default_value = .6
    links.new(split.outputs["Z"], steep.inputs["Value"])
    color = rs.mix_color(nodes, links, steep.outputs[0], color, rs.srgb("#4a4440"))
    links.new(color, bsdf.inputs["Base Color"])
    grain = nodes.new("ShaderNodeTexVoronoi")
    grain.inputs["Scale"].default_value = 12
    links.new(coords.outputs["Object"], grain.inputs["Vector"])
    bump = nodes.new("ShaderNodeBump")
    bump.inputs["Strength"].default_value = .45
    links.new(grain.outputs["Distance"], bump.inputs["Height"])
    links.new(bump.outputs[0], bsdf.inputs["Normal"])
    return mat


def water_material():
    return material("Water", "#123e52", .1, .06, 0, emission="#0f6f8f", strength=.12 if not NIGHT else .3)


# The zones stand on raised platforms and the roads on walkways at the playable level (z = 0, where the
# sprites sit); the land around them lies this much lower.
GROUND = -.5


def ground_z(gx, gy, roads):
    """Height of the bare land at a tile position, below the platforms."""
    from mathutils import noise
    m = coast_metric(gx, gy)
    dz = zone_distance(gx, gy)
    dr = min((max(abs(gx - x), abs(gy - y)) for x, y in roads), default=9)
    dw = river_distance(gx, gy)
    # Rocky hills rise away from the platforms, walkways and river.
    open_ground = min(1, max(0, dz - .5) / 2.5) * min(1, max(0, dr - .6) / 1.5) * min(1, max(0, dw - .9) / 1.5)
    hill = (noise.noise(Vector((gx * .16, gy * .16, 1.7))) + .35) * .95 * open_ground
    rough = noise.noise(Vector((gx * .9, gy * .9, 4.2))) * .12 * open_ground
    z = GROUND + max(0, hill) + rough
    if dw < .55:
        z = GROUND - .18  # River bed.
    cliff = max(0, min(1, (m - COAST) / .7))
    return z * (1 - cliff) + (-2.6) * (cliff * cliff * (3 - 2 * cliff))


def build_island_ground(roads):
    bpy.ops.mesh.primitive_grid_add(x_subdivisions=300, y_subdivisions=300, size=64, location=(12.5, -12.5, 0))
    ground = bpy.context.active_object
    for v in ground.data.vertices:
        v.co.z = ground_z(v.co.x + 12.5, -(v.co.y - 12.5), roads)
    bpy.ops.object.shade_smooth()
    ground.data.materials.append(island_material())


def platform_mats():
    return {
        "deck": material("Deck", "#7a858c", 0, .7, .3),
        "wall": material("Wall", "#505a61", 0, .75, .7),
        "trim": material("Trim", "#2e363c", .2, .55, .4),
        "amber": glowing("Amber", "#ffb648", 2.6),
        "line": material("Line", "#1d3c48", .3, .4, .2, emission="#1d6c80", strength=.35),
        "pillar": material("Pillar", "#40484e", .1, .6, .6),
    }


def build_platforms(roads):
    """A raised concrete platform under every zone, with walls, a dark trim and amber lights on its edge."""
    m = platform_mats()
    for cx, cy in ZONES:
        x, y = cx, -cy
        box((x, y, -.32), (5.08, 5.08, .64), m["wall"], bevel=.03)
        box((x, y, -.01), (5.0, 5.0, .04), m["deck"], bevel=.01)
        for side in (-1, 1):
            box((x + side * 2.53, y, -.06), (.05, 5.1, .1), m["trim"], bevel=.01)
            box((x, y + side * 2.53, -.06), (5.1, .05, .1), m["trim"], bevel=.01)
            for k in range(11):
                t = -2.5 + k * .5
                box((x + side * 2.56, y + t, -.09), (.02, .1, .04), m["amber"], bevel=0)
                box((x + t, y + side * 2.56, -.09), (.1, .02, .04), m["amber"], bevel=0)
        # Buttresses down the walls.
        for t in (-2.2, 0, 2.2):
            for side in (-1, 1):
                box((x + side * 2.58, y + t, -.4), (.12, .28, .6), m["trim"], bevel=.02)
                box((x + t, y + side * 2.58, -.4), (.28, .12, .6), m["trim"], bevel=.02)


def build_walkways(roads):
    """Raised walkways along the road tiles, with a dim line on each side that Unity lights once linked."""
    m = platform_mats()
    for gx, gy in roads:
        x, y = gx, -gy
        dirs = [(dx, dy) for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)) if (gx + dx, gy + dy) in roads or in_zone(gx + dx, -(gy + dy), -.01)]
        box((x, y, -.1), (.74, .74, .2), m["deck"], bevel=.02)
        for dx, dy in dirs:
            ox, oy = dx * .37, -dy * .37
            size = (.26, .74, .2) if dx else (.74, .26, .2)
            box((x + ox * 1.0, y + oy * 1.0, -.1), size, m["deck"], bevel=.01)
            for side in (-1, 1):
                if dx:
                    box((x + ox, y + side * .3, .002), (.5, .03, .004), m["line"], bevel=0)
                else:
                    box((x + side * .3, y + oy, .002), (.03, .5, .004), m["line"], bevel=0)
        cyl((x, y, (GROUND - .2) / 2 - .1), .09, abs(GROUND) + .2, m["pillar"], bevel=.01, vertices=10)
        box((x, y, -.22), (.8, .8, .04), m["trim"], bevel=.01)


def pine(x, y, scale, leaf, trunk):
    cyl((x, y, .12 * scale), .05 * scale, .24 * scale, trunk, bevel=0, vertices=8)
    for k, (r, z) in enumerate(((.32, .38), (.25, .62), (.16, .84))):
        bpy.ops.mesh.primitive_cone_add(vertices=9, radius1=r * scale, depth=.42 * scale, location=(x, y, z * scale))
        rs.finish(bpy.context.active_object, leaf, 0)


def pine_at(gx, gy, scale, leaf, trunk, roads):
    """A pine standing on the land at this tile position."""
    z = ground_z(gx, gy, roads)
    before = set(bpy.data.objects)
    pine(gx, -gy, scale, leaf, trunk)
    for obj in set(bpy.data.objects) - before:
        obj.location.z += z


def build_terrain():
    rs.clear()
    chunk_w, chunk_h = TERRAIN_W / CHUNKS, TERRAIN_H / CHUNKS
    rs.SCENE_SIZE = (CHUNK_PX, round(CHUNK_PX * chunk_h / chunk_w))
    if PREVIEW:
        rs.SCENE_SIZE = (rs.SCENE_SIZE[0] // 4, rs.SCENE_SIZE[1] // 4)
    rs.setup(10)
    if NIGHT:
        apply_night()
    scene = bpy.context.scene
    scene.render.film_transparent = False
    scene.cycles.samples = 24 if PREVIEW else 80
    for obj in list(scene.objects):
        if obj.type == "MESH":
            bpy.data.objects.remove(obj)
    center = ((N - 1) / 2, -(N - 1) / 2, 0)
    iso_camera(center, chunk_w * K)

    roads = {t for t in road_tiles() if zone_distance(*t) > 0}
    build_island_ground(roads)
    build_platforms(roads)
    build_walkways(roads)
    bpy.ops.mesh.primitive_plane_add(size=1, location=(12.5, -12.5, -1.9))
    sea = bpy.context.active_object
    sea.scale = (300, 300, 1)  # Reaches past every corner of the wide overview too.
    sea.data.materials.append(water_material())

    # River with a waterfall where it leaves the island.
    water = water_material()
    foam = material("Foam", "#d8eef2", 0, .3, 0, emission="#bfe8f2", strength=.25)
    for a, b in zip(RIVER, RIVER[1:]):
        (ax, ay), (bx, by) = a, b
        length = math.hypot(bx - ax, by - ay)
        box(((ax + bx) / 2, -(ay + by) / 2, GROUND - .11), (length + .2, .7, .01), water, bevel=0, rot=math.atan2(-(by - ay), bx - ax))
    for gx in [g / 10 for g in range(90, -90, -1)]:  # Walk the river west until it reaches the cliff.
        if coast_metric(gx, 6.1) > COAST:
            # The water pours down the cliff face as a steep sheet, with foam where it meets the sea.
            drop = Vector((gx - 1.1, -6.1, -1.9)) - Vector((gx + .2, -6.1, GROUND - .1))
            bpy.ops.mesh.primitive_plane_add(size=1, location=(gx - .45, -6.1, (GROUND - 2) / 2))
            sheet = bpy.context.active_object
            sheet.scale = (drop.length, .7, 1)
            sheet.rotation_euler = (0, math.atan2(-drop.z, -drop.x), 0)
            rs.finish(sheet, foam, 0)
            for k in range(7):
                ball((gx - 1.15 - (k % 3) * .15, -6.1 + (k % 3 - 1) * .25, -1.88), .14 + (k % 2) * .05, foam)
            break

    rng = random.Random(5)
    rock = material("Rock", "#4f453e", .1, .85, .6)
    leaf = material("Pine", "#2e4630", .05, .8, .4)
    leaf_light = material("PineLight", "#3b5a37", .05, .8, .4)
    trunk = material("Trunk", "#3a2a22", .1, .9, .3)
    for _ in range(950):  # Pine woods on open ground, in clumps.
        gx, gy = rng.uniform(-6, 31), rng.uniform(-6, 31)
        if coast_metric(gx, gy) > COAST - .6 or zone_distance(gx, gy) < .7 or river_distance(gx, gy) < 1:
            continue
        if any(max(abs(gx - x), abs(gy - y)) < .9 for x, y in roads):
            continue
        from mathutils import noise
        if noise.noise(Vector((gx * .2, gy * .2, 9))) < -.05:
            continue
        pine_at(gx, gy, rng.uniform(.7, 1.25), leaf if rng.random() < .6 else leaf_light, trunk, roads)
    for _ in range(520):  # Loose rocks.
        gx, gy = rng.uniform(-6, 31), rng.uniform(-6, 31)
        if coast_metric(gx, gy) > COAST - .3 or zone_distance(gx, gy) < .35:
            continue
        if any(max(abs(gx - x), abs(gy - y)) < .5 for x, y in roads):
            continue
        r = rng.uniform(.06, .24)
        z = ground_z(gx, gy, roads)
        bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=2, radius=r, location=(gx, -gy, z + r * .3))
        stone = bpy.context.active_object
        stone.scale = (1, rng.uniform(.6, 1.3), rng.uniform(.4, .8))
        rs.finish(stone, rock, 0)
    # Big jagged boulders and crags between the platforms, as in a rocky highland.
    from mathutils import noise
    crag = material("Crag", "#5a524b", .1, .88, .7)
    for _ in range(260):
        gx, gy = rng.uniform(-4, 29), rng.uniform(-4, 29)
        if coast_metric(gx, gy) > COAST - .8 or zone_distance(gx, gy) < .6 or river_distance(gx, gy) < 1.1:
            continue
        if any(max(abs(gx - x), abs(gy - y)) < 1.0 for x, y in roads):
            continue
        if noise.noise(Vector((gx * .25, gy * .25, 2.2))) < .05:
            continue
        r = rng.uniform(.25, .6)
        z = ground_z(gx, gy, roads)
        bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=1, radius=r, location=(gx, -gy, z + r * .25))
        stone = bpy.context.active_object
        stone.scale = (rng.uniform(.8, 1.3), rng.uniform(.8, 1.3), rng.uniform(.6, 1.2))
        stone.rotation_euler = (rng.uniform(0, 1), rng.uniform(0, 1), rng.uniform(0, 6.3))
        rs.finish(stone, crag, 0)


# ---- Sprites (one tile, isometric) --------------------------------------------------

def sprite_scene():
    rs.clear()
    rs.SCENE_SIZE = (512, 512)  # 256 pixels per Unity unit: sharp when zoomed in.
    rs.setup(2 * K)  # 2 Unity units across: a tile is the middle half.
    iso_camera((0, 0, .35), 2 * K)  # Aimed a little above the ground so tall buildings fit.


def pad(mat, size=.84, height=.08):
    box((0, 0, height / 2), (size, size, height), mat, bevel=.03)


def build_tile_pave():
    concrete = material("Concrete", "#5a646b", .3, .6, .6)
    seam = material("Seam", "#2b3238", .5, .7, .3)
    steel = material("Steel", "#b9c5ca", 1, .3, .3)
    box((0, 0, .025), (.98, .98, .05), concrete, bevel=.015)
    box((0, 0, .052), (.96, .02, .004), seam, bevel=0)
    box((0, 0, .052), (.02, .96, .004), seam, bevel=0)
    for dx, dy in ((-.42, -.42), (.42, -.42), (-.42, .42), (.42, .42)):
        cyl((dx, dy, .055), .02, .01, steel, bevel=.002, vertices=8)


def build_tile_road():
    gravel = material("Gravel", "#3a3430", .1, .9, .5)
    edge = material("Edge", "#6a5a48", .1, .85, .4)
    box((0, 0, .015), (.98, .98, .03), gravel, bevel=.01)
    for k in range(-2, 3):
        box((k * .2, k * .2, .033), (.08, .08, .004), edge, bevel=0)


def build_deco_lamp():
    steel = material("Steel", "#b9c5ca", 1, .3, .3)
    dark = material("Dark", "#151c21", .8, .5, .3)
    warm = glowing("Warm", "#ffc77a", 2.4)
    cyl((0, 0, .04), .1, .08, dark, bevel=.02)
    cyl((0, 0, .5), .025, .9, steel, bevel=.005)
    box((.08, 0, .95), (.22, .07, .05), dark, bevel=.01)
    box((.12, 0, .92), (.12, .05, .02), warm, bevel=.004)
    ball((.12, 0, .9), .04, glowing_hot("Hot", "#fff1d0"))


def build_deco_ship():
    white = material("White", "#d4dcde", .4, .35, .4)
    panel = material("Panel", "#1a2c4a", .4, .18, .2)
    dark = material("Dark", "#151c21", .8, .5, .3)
    warm = glowing("Warm", "#ffc77a", 2.2)
    hull = [(.0, .62), (.22, .1), (.5, -.38), (.16, -.32), (0, -.5), (-.16, -.32), (-.5, -.38), (-.22, .1)]
    prism(hull, .22, .24, white, .04)
    prism([(x * .7, y * .7 + .05) for x, y in hull], .38, .1, white, .03)
    prism([(0, .45), (.1, .1), (-.1, .1)], .45, .04, panel, .01)
    for side in (-1, 1):
        cyl((side * .16, -.42, .22), .08, .1, dark, bevel=.02, rot=(math.radians(90), 0, 0))
        cyl((side * .16, -.48, .22), .05, .02, warm, bevel=.005, rot=(math.radians(90), 0, 0))
        beam((side * .3, -.2, .1), (side * .42, -.3, 0), .02, dark)


def build_deco_barricade():
    """Hostile outpost: spiked barricade with red lights and enemy crates."""
    iron = material("Iron", "#3c3236", .8, .5, .5)
    red = glowing("Red", "#ff3a2a", 2.6)
    rust = material("Rust", "#7a3d2a", .5, .6, .6)
    for i in range(5):
        x = -.36 + i * .18
        beam((x - .12, -.1, 0), (x + .12, .1, .45), .03, iron)
        beam((x + .12, -.1, 0), (x - .12, .1, .45), .03, iron)
    box((0, 0, .2), (.8, .05, .05), iron, bevel=.01)
    for x in (-.3, .3):
        ball((x, 0, .48), .04, red)
    box((.2, .3, .12), (.25, .2, .24), rust, bevel=.03)
    box((-.25, -.3, .1), (.2, .2, .2), rust, bevel=.03)


def build_bld_extractor():
    concrete = material("Concrete", "#5a646b", .3, .6, .6)
    rust = material("Rust", "#a85a34", .5, .55, .5)
    steel = material("Steel", "#c7d2d7", 1, .28, .25)
    dark = material("Dark", "#151c21", .8, .5, .3)
    hot = glowing("Hot", "#ffa040", 2.6)
    ore = material("Ore", "#7a6a5a", .2, .85, .3)
    pad(concrete)
    apex = (0, 0, 1.15)
    for x, y in ((-.3, -.3), (.3, -.3), (.3, .3), (-.3, .3)):
        beam((x, y, .08), apex, .03, rust)
    for z in (.4, .7):
        s = .3 * (1 - (z - .08) / 1.07)
        for (x0, y0), (x1, y1) in (((-s, -s), (s, -s)), ((s, -s), (s, s)), ((s, s), (-s, s)), ((-s, s), (-s, -s))):
            beam((x0, y0, z), (x1, y1, z), .015, steel)
    box((0, 0, 1.15), (.14, .14, .1), dark, bevel=.02)
    cyl((0, 0, .3), .04, .5, steel, bevel=.005)
    cyl((0, 0, .1), .12, .04, hot, bevel=.008)
    box((.28, -.28, .18), (.24, .2, .2), steel, bevel=.03)
    for i in range(4):
        ball((.22 + (i % 2) * .1, -.3 + (i // 2) * .08, .3), .04, ore)


def build_bld_laboratory():
    concrete = material("Concrete", "#5a646b", .3, .6, .6)
    white = material("White", "#dde5e8", .3, .3, .35)
    steel = material("Steel", "#c7d2d7", 1, .28, .25)
    glow = glowing("Glow", "#5ee8ff", 2.6)
    dark = material("Dark", "#151c21", .8, .5, .3)
    pad(concrete)
    box((0, 0, .17), (.7, .7, .18), white, bevel=.05)
    for side in (-1, 1):
        box((side * .351, 0, .17), (.01, .5, .06), glow, bevel=0)
        box((0, side * .351, .17), (.5, .01, .06), glow, bevel=0)
    bpy.ops.mesh.primitive_uv_sphere_add(segments=48, ring_count=24, radius=.3, location=(0, 0, .26))
    dome = bpy.context.active_object
    dome.scale = (1, 1, .9)
    rs.finish(dome, white, 0, smooth=True)
    torus((0, 0, .3), .3, .025, glow)
    cyl((.25, .25, .65), .02, .8, steel, bevel=.004)
    cyl((.25, .25, 1.02), .12, .02, steel, bevel=.008, rot=(math.radians(35), 0, math.radians(45)))
    ball((.25, .25, 1.06), .03, glow)
    box((-.25, -.25, .3), (.12, .12, .1), dark, bevel=.02)


def build_bld_refinery():
    concrete = material("Concrete", "#5a646b", .3, .6, .6)
    white = material("White", "#d4dcde", .4, .35, .4)
    steel = material("Steel", "#c7d2d7", 1, .28, .25)
    rust = material("Rust", "#8a4b2f", .5, .6, .6)
    flame = glowing("Flame", "#ff8a2a", 2.8)
    pad(concrete)
    for x, y in ((-.2, .18), (-.2, -.18)):
        cyl((x, y, .38), .16, .6, white, bevel=.04)
        torus((x, y, .68), .13, .015, steel)
        for z in (.25, .5):
            torus((x, y, z), .165, .012, rust)
    cyl((.18, .1, .58), .09, 1.0, steel, bevel=.02)
    for z in (.3, .55, .8):
        torus((.18, .1, z), .095, .014, rust)
    beam((-.05, .18, .55), (.1, .1, .55), .03, rust)
    beam((-.05, -.18, .4), (.12, .05, .4), .03, rust)
    cyl((.3, -.28, .6), .025, 1.1, steel, bevel=.005)
    ball((.3, -.28, 1.18), .06, flame)


def build_bld_fortification():
    concrete = material("Concrete", "#6c7378", .3, .7, .6)
    plate = material("Plate", "#4f5b62", .85, .45, .5)
    steel = material("Steel", "#d0d8dc", 1, .28, .2)
    red = glowing("Red", "#ff6a52", 2.4)
    prism(regular(6, .46, math.pi / 6), .16, .32, concrete, .04)
    for i in range(6):
        a = i * math.tau / 6
        box((math.cos(a) * .4, math.sin(a) * .4, .38), (.14, .18, .12), plate, bevel=.02, rot=a)
    cyl((0, 0, .42), .17, .2, plate, bevel=.03)
    beam((0, 0, .48), (.32, .32, .62), .035, steel)
    cyl((0, 0, .54), .05, .02, red, bevel=.005)


def build_bld_housing():
    concrete = material("Concrete", "#5a646b", .3, .6, .6)
    white = material("White", "#dfe6e8", .3, .35, .35)
    teal = material("Teal", "#2f6a72", .6, .48, .5)
    warm = glowing("Warm", "#ffc77a", 2.2)
    pad(concrete)
    for x, y, h in ((-.18, .18, .75), (.2, .16, .55), (0, -.22, .45)):
        box((x, y, .08 + h / 2), (.3, .3, h), white, bevel=.04)
        box((x, y, .09 + h), (.22, .22, .03), teal, bevel=.01)
        floors = int(h / .14)
        for f in range(floors):
            z = .2 + f * .14
            box((x + .151, y, z), (.01, .2, .05), warm, bevel=0)
            box((x, y - .151, z), (.2, .01, .05), warm, bevel=0)
    beam((-.18, .18, .3), (.2, .16, .3), .04, teal)


def build_bld_solar():
    concrete = material("Concrete", "#5a646b", .3, .6, .6)
    panel = material("Panel", "#1a2c4a", .4, .12, .2)
    steel = material("Steel", "#c7d2d7", 1, .28, .25)
    glow = glowing("Glow", "#ffd36a", 2.2)
    pad(concrete)
    for x in (-.2, .2):
        for y in (-.2, .2):
            cyl((x, y, .18), .02, .2, steel, bevel=.004)
            box((x, y, .3), (.34, .3, .02), panel, bevel=.008, rot=0)
            obj = bpy.context.active_object
            obj.rotation_euler = (math.radians(-25), 0, 0)
            for k in range(3):
                box((x, y - .1 + k * .1, .315), (.32, .008, .004), steel, bevel=0)
    cyl((0, 0, .14), .05, .1, steel, bevel=.01)
    ball((0, 0, .21), .03, glow)


def build_road_half(sign):
    """The lit edge lines of a walkway from the tile center to its +x (or -x) edge; Unity mirrors it for y."""
    line = glowing("Line", "#5ee8ff", 3.0)
    for side in (-1, 1):
        box((sign * .25, side * .3, .004), (.52, .035, .006), line, bevel=0)
    box((sign * .47, 0, .004), (.03, .64, .006), glowing("Seam", "#2fb8d8", 1.6), bevel=0)


def build_road_node():
    """Round glowing pad where a walkway meets a platform."""
    ring = glowing("Ring", "#5ee8ff", 3.2)
    soft = glowing("Soft", "#2b9ec0", 1.2)
    dark = material("Dark", "#151c21", .8, .5, .3)
    cyl((0, 0, .01), .3, .02, dark, bevel=.005, vertices=40)
    torus((0, 0, .025), .25, .025, ring)
    cyl((0, 0, .024), .17, .006, soft, bevel=0, vertices=40)


def build_deco_site():
    """Foundation of a building under construction: a slab with rebar and a pile of materials."""
    concrete = material("Concrete", "#6a7378", .1, .7, .6)
    rebar = material("Rebar", "#8a4b2f", .7, .5, .5)
    crate = material("Crate", "#c49a5a", .1, .7, .4)
    box((0, 0, .03), (.84, .84, .06), concrete, bevel=.02)
    for k in range(5):
        t = -.32 + k * .16
        beam((t, -.36, .08), (t, .36, .08), .008, rebar)
        beam((-.36, t, .09), (.36, t, .09), .008, rebar)
    for x, y in ((-.3, -.3), (.3, -.3), (.3, .3), (-.3, .3)):
        cyl((x, y, .2), .012, .3, rebar, bevel=0, vertices=6)
    box((.24, -.26, .12), (.18, .16, .12), crate, bevel=.015)
    box((.3, -.1, .1), (.12, .14, .08), crate, bevel=.015)


def build_deco_scaffold():
    """Open scaffold with a striped safety rail and a small crane, light enough to see the building through."""
    pole = material("Pole", "#e0ac1a", .4, .45, .4)
    steel = material("Steel", "#b9c5ca", 1, .3, .3)
    dark = material("Dark", "#151c21", .8, .5, .3)
    warn = glowing("Warn", "#ff9a2a", 2.4)
    s = .42
    for x, y in ((-s, -s), (s, -s), (s, s), (-s, s)):
        cyl((x, y, .5), .014, 1.0, pole, bevel=0, vertices=8)
    for z in (.3, .62, .94):
        for (x0, y0), (x1, y1) in (((-s, -s), (s, -s)), ((s, -s), (s, s)), ((s, s), (-s, s)), ((-s, s), (-s, -s))):
            beam((x0, y0, z), (x1, y1, z), .009, steel)
    for (x0, y0), (x1, y1) in (((-s, -s), (s, -s)), ((s, -s), (s, s))):
        beam((x0, y0, .32), (x1, y1, .62), .007, steel)  # Diagonal braces on the two faces in view.
    # Crane on the back corner.
    cyl((-s, s, .62), .025, 1.24, pole, bevel=0, vertices=8)
    beam((-s - .1, s + .1, 1.2), (s * .6, -s * .4, 1.2), .02, pole)
    box((s * .6, -s * .4, 1.0), (.004, .004, .4), steel, bevel=0)
    box((s * .6, -s * .4, .8), (.06, .06, .04), dark, bevel=.005)
    ball((-s, s, 1.26), .03, warn)


def build_deco_beacon():
    """Enemy pylon: a dark lattice mast with a red light on top."""
    iron = material("Iron", "#2c2427", .8, .5, .5)
    red = glowing("Red", "#ff2a1a", 3.4)
    for x, y in ((-.1, -.1), (.1, -.1), (.1, .1), (-.1, .1)):
        beam((x * 1.8, y * 1.8, 0), (x * .4, y * .4, .95), .014, iron)
    for z in (.25, .5, .75):
        r = .18 * (1 - z / 1.1)
        for (x0, y0), (x1, y1) in (((-r, -r), (r, -r)), ((r, -r), (r, r)), ((r, r), (-r, r)), ((-r, r), (-r, -r))):
            beam((x0, y0, z), (x1, y1, z), .008, iron)
    cyl((0, 0, .98), .05, .06, iron, bevel=.01)
    ball((0, 0, 1.05), .05, red)
    cyl((0, 0, .03), .2, .06, iron, bevel=.02)


def build_deco_drone():
    """Cargo drone with four rotors carrying a crate."""
    white = material("White", "#dde5e8", .3, .32, .35)
    dark = material("Dark", "#151c21", .8, .5, .3)
    crate = material("Crate", "#c49a5a", .1, .7, .4)
    cyan = glowing("Cyan", "#5ee8ff", 3)
    box((0, 0, .42), (.2, .2, .07), white, bevel=.02)
    for x, y in ((-.16, -.16), (.16, -.16), (.16, .16), (-.16, .16)):
        beam((0, 0, .44), (x, y, .46), .012, dark)
        cyl((x, y, .47), .08, .006, transparent("Rotor", "#c8d8e0", .35), bevel=0, vertices=24)
        cyl((x, y, .46), .015, .03, dark, bevel=0, vertices=8)
    box((0, 0, .28), (.15, .15, .12), crate, bevel=.015)
    beam((0, 0, .38), (0, 0, .34), .006, dark)
    ball((0, -.1, .42), .02, cyan)


SPRITES = {
    "road_half_pos": lambda: build_road_half(1), "road_half_neg": lambda: build_road_half(-1), "road_node": build_road_node,
    "deco_site": build_deco_site, "deco_scaffold": build_deco_scaffold, "deco_beacon": build_deco_beacon, "deco_drone": build_deco_drone,
    "tile_pave": build_tile_pave, "tile_road": build_tile_road, "deco_lamp": build_deco_lamp, "deco_ship": build_deco_ship,
    "deco_barricade": build_deco_barricade, "bld_extractor": build_bld_extractor, "bld_laboratory": build_bld_laboratory,
    "bld_refinery": build_bld_refinery, "bld_fortification": build_bld_fortification, "bld_housing": build_bld_housing,
    "bld_solar": build_bld_solar,
}

# ---- Zone landmarks: one per zone, on the 3 x 3 tiles around its center -------------------

def landmark_scene():
    rs.clear()
    rs.SCENE_SIZE = (768, 768)  # 4 Unity units across at 192 pixels per unit.
    rs.setup(4 * K)
    if NIGHT:
        apply_night()
    iso_camera((0, 0, .6), 4 * K)


def lm_mats():
    return {
        "concrete": material("Concrete", "#5a646b", .3, .6, .6),
        "white": material("White", "#dde5e8", .3, .32, .35),
        "steel": material("Steel", "#c3ced3", 1, .28, .25),
        "dark": material("Dark", "#151c21", .8, .5, .3),
        "teal": material("Teal", "#2f6a72", .6, .48, .5),
        "rust": material("Rust", "#8a4b2f", .5, .6, .6),
        "yellow": material("Yellow", "#e0ac1a", .3, .45, .45),
        "warm": glowing("Warm", "#ffc77a", 2.4),
        "cyan": glowing("Cyan", "#5ee8ff", 2.4),
        "green": glowing("Green", "#7dff9e", 2.2),
        "red": glowing("RedLight", "#ff3a2a", 3.0),
        "crate": material("Crate", "#c49a5a", .1, .7, .4),
        "blue": material("Pipe", "#2c7fc0", .5, .35, .4),
    }


def windows(x, y, z0, w, d, h, warm, floors_gap=.16):
    """Lit window bands on the two faces the camera sees."""
    for f in range(int((h - .1) / floors_gap)):
        z = z0 + .12 + f * floors_gap
        box((x + w / 2 + .005, y, z), (.01, d * .8, .05), warm, bevel=0)
        box((x, y - d / 2 - .005, z), (w * .8, .01, .05), warm, bevel=0)


def lattice_mast(x, y, base, top, height, mat, levels=4):
    """Tapered four-legged lattice tower."""
    for sx, sy in ((-1, -1), (1, -1), (1, 1), (-1, 1)):
        beam((x + sx * base, y + sy * base, .1), (x + sx * top, y + sy * top, height), .018, mat)
    for k in range(1, levels + 1):
        z = .1 + (height - .1) * k / (levels + 1)
        r = base + (top - base) * k / (levels + 1)
        for (x0, y0), (x1, y1) in (((-r, -r), (r, -r)), ((r, -r), (r, r)), ((r, r), (-r, r)), ((-r, r), (-r, -r))):
            beam((x + x0, y + y0, z), (x + x1, y + y1, z), .01, mat)


# Moving parts of landmarks: rendered alone, frame by frame, in the landmark's own framing, so Unity plays
# them over the still picture. name -> (part, frames, builder(materials, phase from 0 to 1)).
PARTS = {}

RADAR_AT = (-1.05, -1.0, 1.62)


def part_radar(m, phase):
    x, y, z = RADAR_AT
    a = phase * math.tau
    cyl((x, y, z - .04), .05, .08, m["dark"], bevel=.01)
    bpy.ops.mesh.primitive_uv_sphere_add(segments=32, ring_count=16, radius=.24, location=(x, y, z + .1))
    dish = bpy.context.active_object
    dish.scale = (1, 1, .32)
    dish.rotation_euler = (math.radians(70), 0, a)
    rs.finish(dish, m["white"], 0, smooth=True)
    beam((x, y, z + .1), (x + math.sin(a) * -.25, y + math.cos(a) * .25, z + .22), .012, m["steel"])
    ball((x + math.sin(a) * -.25, y + math.cos(a) * .25, z + .22), .025, m["red"])


def build_lm_landing():
    m = lm_mats()
    box((0, 0, .05), (2.8, 2.8, .1), m["concrete"], bevel=.04)
    torus((0, 0, .11), 1.0, .03, m["yellow"])
    for side in (-1, 1):  # Approach chevrons.
        for k in range(3):
            prism([(side * (1.15 + k * .12), -.12), (side * (1.25 + k * .12), 0), (side * (1.15 + k * .12), .12),
                   (side * (1.1 + k * .12), .12), (side * (1.2 + k * .12), 0), (side * (1.1 + k * .12), -.12)], .1, .012, m["yellow"], .002)
    for i in range(12):
        a = i * math.tau / 12
        cyl((math.cos(a) * 1.25, math.sin(a) * 1.25, .12), .04, .03, m["cyan"], bevel=.005, vertices=12)
    box((1.05, -1.05, .7), (.42, .42, 1.3), m["white"], bevel=.05)  # Control tower.
    cyl((1.05, -1.05, 1.45), .32, .2, m["dark"], bevel=.03)
    torus((1.05, -1.05, 1.45), .32, .03, m["cyan"])
    windows(1.05, -1.05, .1, .42, .42, 1.2, m["warm"])
    cyl((1.05, -1.05, 1.75), .02, .4, m["steel"], bevel=0)
    ball((1.05, -1.05, 1.96), .035, m["red"])
    # Radar mast; its dish turns (a moving part).
    lattice_mast(RADAR_AT[0], RADAR_AT[1], .22, .07, RADAR_AT[2] - .08, m["steel"])
    # Sensor dome and supply crates.
    bpy.ops.mesh.primitive_uv_sphere_add(segments=40, ring_count=20, radius=.32, location=(1.0, .95, .1))
    rs.finish(bpy.context.active_object, m["white"], 0, smooth=True)
    torus((1.0, .95, .14), .32, .03, m["cyan"])
    for k, (x, y, s) in enumerate(((-1.05, .75, .26), (-.8, .95, .22), (-1.05, 1.05, .2))):
        box((x, y, .1 + s / 2), (s, s, s), m["crate"] if k % 2 == 0 else m["teal"], bevel=.02)


PARTS["lm_landing"] = ("radar", 12, part_radar)


def build_lm_camp():
    m = lm_mats()
    box((0, 0, .05), (2.8, 2.8, .1), m["concrete"], bevel=.04)
    for x, y, h in ((-.7, .7, .9), (.6, .75, .7), (-.75, -.55, .6), (.65, -.6, 1.1), (0, 0, .5)):
        box((x, y, .1 + h / 2), (.7, .55, h), m["white"], bevel=.05)
        box((x, y, .12 + h), (.6, .45, .04), m["teal"], bevel=.01)
        windows(x, y, .1, .7, .55, h, m["warm"])


def build_lm_well():
    m = lm_mats()
    box((0, 0, .05), (2.8, 2.8, .1), m["concrete"], bevel=.04)
    tanks = ((-.7, .6, .45), (.55, .7, .42), (-.6, -.6, .38))
    for x, y, r in tanks:
        cyl((x, y, .6), r, 1.0, m["white"], bevel=.05)
        bpy.ops.mesh.primitive_uv_sphere_add(segments=40, ring_count=20, radius=r, location=(x, y, 1.1))
        cap = bpy.context.active_object
        cap.scale = (1, 1, .3)
        rs.finish(cap, m["white"], 0, smooth=True)
        torus((x, y, 1.05), r + .005, .025, m["steel"])
        torus((x, y, .55), r + .01, .035, m["cyan"])
        for k in range(4):  # Legs and a ladder.
            a = k * math.tau / 4 + .4
            cyl((x + math.cos(a) * r * .9, y + math.sin(a) * r * .9, .12), .03, .1, m["steel"], bevel=0, vertices=8)
        box((x + r + .02, y, .6), (.03, .08, .95), m["steel"], bevel=0)
        # A water drop painted on the side in view.
        prism([(0, .1), (.06, -.02), (0, -.08), (-.06, -.02)], .0, .01, m["cyan"], .002)
        drop = bpy.context.active_object
        drop.rotation_euler = (math.radians(90), 0, math.radians(-45))
        drop.location = (x + r * .72, y - r * .72, .78)
    # Blue pipes between the tanks and the pump house, and down into the pool.
    for (x0, y0), (x1, y1) in (((-.7, .6), (.1, 0)), ((.55, .7), (.1, 0)), ((-.6, -.6), (.1, 0)), ((.1, 0), (.7, -.65))):
        beam((x0, y0, .3), (x1, y1, .3), .05, m["blue"])
        for t in (.25, .75):
            cyl((x0 + (x1 - x0) * t, y0 + (y1 - y0) * t, .18), .02, .2, m["steel"], bevel=0, vertices=8)
    cyl((.75, -.7, .14), .5, .06, material("Pool", "#1b6f8f", .1, .05, 0, emission="#1aa8d8", strength=.6), bevel=.01)
    torus((.75, -.7, .17), .5, .03, m["steel"])
    box((.1, 0, .32), (.44, .34, .44), m["teal"], bevel=.04)
    windows(.1, 0, .1, .44, .34, .44, m["warm"])
    cyl((.1, 0, .62), .08, .16, m["steel"], bevel=.02)


def build_lm_greenhouse():
    m = lm_mats()
    box((0, 0, .05), (2.8, 2.8, .1), m["concrete"], bevel=.04)
    glass = transparent("Glass", "#c8f5dc", .3)
    plant = material("Plant", "#3f9a4a", 0, .7, .2, emission="#5bd46a", strength=.5)
    for x in (-.78, 0, .78):
        box((x, 0, .13), (.66, 2.3, .06), m["dark"], bevel=.015)
        bpy.ops.mesh.primitive_cylinder_add(vertices=32, radius=.33, depth=2.2, location=(x, 0, .14), rotation=(math.radians(90), 0, 0))
        hall = bpy.context.active_object
        rs.finish(hall, glass, 0, smooth=True)
        for y in (-1.1, -.73, -.37, 0, .37, .73, 1.1):
            torus((x, y, .14), .33, .016, m["steel"])
        beam((x, -1.1, .47), (x, 1.1, .47), .014, m["steel"])
        # Rows of lit plants inside.
        for row in (-.14, .14):
            box((x + row, 0, .2), (.12, 2.0, .06), m["green"], bevel=.01)
            for k in range(9):
                bpy.ops.mesh.primitive_cone_add(vertices=7, radius1=.06, depth=.16, location=(x + row, -.95 + k * .24, .3))
                rs.finish(bpy.context.active_object, plant, 0)
    for k, (x, y) in enumerate(((1.2, -1.2), (-1.2, -1.2))):
        box((x, y, .2), (.2, .2, .2), m["crate"], bevel=.02)


MINE_ARM = (.68, -.62, .55)  # The boom's pivot on the excavator.


def part_mine_arm(m, phase):
    """Excavator boom, stick and bucket digging back and forth."""
    swing = math.sin(phase * math.tau)
    x, y, z = MINE_ARM
    ax, ay = -.62, .5  # Direction toward the pit.
    lift = .35 + .2 * swing
    elbow = (x + ax * .5, y + ay * .5, z + lift)
    reach = .78 + .12 * swing
    tip = (x + ax * reach, y + ay * reach, .32 + .18 * swing)
    beam((x, y, z), elbow, .045, m["yellow"])
    beam(elbow, tip, .035, m["yellow"])
    beam((x + ax * .12, y + ay * .12, z - .05), (elbow[0] - ax * .1, elbow[1] - ay * .1, elbow[2] - .05), .02, m["steel"])  # Hydraulic ram.
    box((tip[0], tip[1], tip[2] - .06), (.2, .16, .12), m["steel"], bevel=.02)
    for k in range(3):
        box((tip[0] - .07 + k * .07, tip[1] + .06, tip[2] - .13), (.03, .03, .05), m["dark"], bevel=0)


def build_lm_mines():
    m = lm_mats()
    pit = material("Pit", "#3a2d26", .1, .9, .5)
    ore = material("Ore", "#7a6a5a", .2, .85, .3)
    box((0, 0, .03), (2.8, 2.8, .06), pit, bevel=.04)
    for i, r in enumerate((1.0, .75, .5)):
        torus((-.2, .2, .06 - i * .02), r, .07, material("Terrace", "#5b4636", .1, .9, .5))
    # Excavator: tracks, turning body and cab; the boom is a moving part.
    for side in (-1, 1):
        box((.8 + side * .02, -.68 + side * .17, .1), (.62, .1, .1), m["dark"], bevel=.03)
    box((.78, -.68, .3), (.5, .4, .24), m["yellow"], bevel=.04)
    box((.92, -.82, .5), (.2, .18, .2), m["dark"], bevel=.03)
    box((.93, -.92, .5), (.16, .01, .12), m["cyan"], bevel=0)
    box((.62, -.55, .45), (.18, .18, .1), m["yellow"], bevel=.02)
    # Ore heaps, a conveyor and crates.
    for x, y, r in ((.8, .7, .3), (1.05, .3, .22), (.45, .95, .18)):
        bpy.ops.mesh.primitive_cone_add(vertices=12, radius1=r, depth=r * 1.1, location=(x, y, .06 + r * .55))
        rs.finish(bpy.context.active_object, ore, 0)
    beam((-.1, .2, .1), (.75, .65, .55), .05, m["dark"])
    for t in (.3, .7):
        cyl((-.1 + .85 * t, .2 + .45 * t, .16 + .3 * t), .02, .3 * t + .1, m["steel"], bevel=0, vertices=8)
    for x, y in ((-1.1, -.9), (-.88, -1.1), (-1.1, -1.1)):
        box((x, y, .16), (.18, .18, .18), m["crate"], bevel=.02)
    # Headframe over the shaft, with its lamp.
    lattice_mast(-1.0, 1.0, .2, .12, 1.0, m["steel"], 3)
    cyl((-1.0, 1.0, 1.05), .14, .05, m["dark"], bevel=.01, rot=(math.radians(90), 0, math.radians(45)))
    ball((-1.0, 1.0, 1.12), .035, m["warm"])


PARTS["lm_mines"] = ("arm", 8, part_mine_arm)


def build_lm_workshop():
    m = lm_mats()
    box((0, 0, .05), (2.8, 2.8, .1), m["concrete"], bevel=.04)
    box((-.35, .2, .45), (1.6, 1.2, .7), material("Shed", "#56636b", .8, .45, .5), bevel=.05)
    for k in range(6):
        box((-.35, -.35 + k * .22, .82), (1.55, .1, .04), m["yellow"] if k % 2 else m["dark"], bevel=.01)
    windows(-.35, .2, .1, 1.6, 1.2, .7, m["warm"], .22)
    cyl((.75, .75, .9), .12, 1.6, m["steel"], bevel=.02)
    torus((.75, .75, 1.6), .13, .02, m["rust"])
    box((.8, -.8, .9), (.08, .08, 1.6), m["yellow"], bevel=.01)  # Crane.
    beam((.8, -.8, 1.7), (-.4, -.6, 1.7), .04, m["yellow"])
    box((-.4, -.6, 1.2), (.03, .03, .9), m["steel"], bevel=0)


GANTRY_Z = 1.55  # Height of the gantry crane's top beam.


def part_port_crane(m, phase):
    """The gantry's trolley runs along the beam, lowering a container over the harbor and lifting it to the quay."""
    t = .5 - .5 * math.cos(phase * math.tau)
    x = -1.0 + 1.45 * t
    y = -.15
    drop = .55 + .35 * math.sin(phase * math.tau * 2) ** 2
    box((x, y, GANTRY_Z + .02), (.22, .5, .1), m["dark"], bevel=.02)
    for dy in (-.12, .12):
        box((x, y + dy, GANTRY_Z - drop / 2), (.008, .008, drop), m["steel"], bevel=0)
    box((x, y, GANTRY_Z - drop - .02), (.3, .5, .04), m["yellow"], bevel=.01)
    box((x, y, GANTRY_Z - drop - .16), (.28, .52, .24), material("Hoisted", "#c14a32", .6, .5, .5), bevel=.02)


def build_lm_port():
    m = lm_mats()
    box((.25, 0, .05), (2.3, 2.8, .1), m["concrete"], bevel=.04)
    box((-1.15, 0, .0), (.55, 2.8, .04), material("Harbor", "#123e52", .1, .06, 0, emission="#0f6f8f", strength=.25), bevel=.01)
    box((-.88, 0, .08), (.06, 2.8, .1), m["dark"], bevel=.01)  # Quay edge.
    colors = ("#8a4b2f", "#2f6a72", "#c9a23a", "#3d5f8a", "#c14a32", "#5d7a3a")
    rng = random.Random(4)
    for row, y in enumerate((.55, .95)):
        for col, x in enumerate((.25, .62, 1.0)):
            for level in range(1 + (row + col) % 2):
                box((x, y, .25 + level * .28), (.34, .52, .26), material(f"Box{row}{col}{level}", colors[rng.randrange(6)], .6, .5, .5), bevel=.02)
    # Gantry crane: two A-frames over the quay and the harbor, joined by the beam the trolley runs on.
    for y in (-.55, .25):
        beam((-1.25, y, .0), (-1.15, y, GANTRY_Z), .04, m["yellow"])
        beam((.55, y, .1), (.45, y, GANTRY_Z), .04, m["yellow"])
        beam((-1.15, y, GANTRY_Z * .6), (.45, y, GANTRY_Z * .6), .02, m["yellow"])
    for y in (-.4, .1):
        beam((-1.3, y, GANTRY_Z + .05), (.6, y, GANTRY_Z + .05), .04, m["yellow"])
    box((.55, -.15, GANTRY_Z + .2), (.3, .6, .25), m["white"], bevel=.03)  # Machine house.
    box((.4, -.15, GANTRY_Z + .2), (.01, .4, .1), m["warm"], bevel=0)
    ball((-1.3, -.15, GANTRY_Z + .1), .04, m["red"])
    for y in (-1.15, 1.15):
        cyl((-.82, y, .35), .03, .5, m["steel"], bevel=0)
        ball((-.82, y, .62), .05, m["warm"])
    for y in (-.9, -.3, .3, .9):
        cyl((-.84, y, .13), .04, .06, m["dark"], bevel=.01)  # Bollards.


PARTS["lm_port"] = ("crane", 12, part_port_crane)


def build_lm_core():
    m = lm_mats()
    box((0, 0, .05), (2.8, 2.8, .1), m["concrete"], bevel=.04)
    cyl((0, 0, .2), 1.15, .2, m["steel"], bevel=.04)
    bpy.ops.mesh.primitive_uv_sphere_add(segments=48, ring_count=24, radius=.95, location=(0, 0, .3))
    dome = bpy.context.active_object
    dome.scale = (1, 1, .75)
    rs.finish(dome, m["white"], 0, smooth=True)
    for z, r in ((.45, .98), (.75, .85)):
        torus((0, 0, z), r, .03, m["green"])
    cyl((0, 0, 1.05), .22, .1, m["green"], bevel=.02)
    for i in range(6):
        a = i * math.tau / 6
        box((math.cos(a) * 1.25, math.sin(a) * 1.25, .2), (.35, .12, .2), m["dark"], bevel=.02, rot=a)


LANDMARKS = {
    "lm_landing": build_lm_landing, "lm_camp": build_lm_camp, "lm_well": build_lm_well, "lm_greenhouse": build_lm_greenhouse,
    "lm_mines": build_lm_mines, "lm_workshop": build_lm_workshop, "lm_port": build_lm_port, "lm_core": build_lm_core,
}


def build_tile_road_glow():
    gravel = material("Gravel", "#2f2b29", .1, .9, .5)
    line = glowing("Line", "#5ee8ff", 1.6)
    box((0, 0, .015), (.98, .98, .03), gravel, bevel=.01)
    for side in (-1, 1):
        box((side * .38, 0, .033), (.04, .98, .004), line, bevel=0)
        box((0, side * .38, .033), (.98, .04, .004), line, bevel=0)


if __name__ == "__main__":
    os.makedirs(OUT, exist_ok=True)
    if OVERVIEW:
        build_terrain()
        rs.SCENE_SIZE = (OVERVIEW_PX, round(OVERVIEW_PX * OVERVIEW_H / OVERVIEW_W))
        if PREVIEW:
            rs.SCENE_SIZE = (rs.SCENE_SIZE[0] // 4, rs.SCENE_SIZE[1] // 4)
        scene = bpy.context.scene
        scene.render.resolution_x, scene.render.resolution_y = rs.SCENE_SIZE
        scene.camera.data.ortho_scale = OVERVIEW_W * K
        scene.camera.data.clip_end = 600
        scene.cycles.samples = 16 if PREVIEW else 48  # Seen small and from far away.
        scene.render.filepath = os.path.join(OUT, "colony_overview" + suffix() + ("_preview" if PREVIEW else "") + ".png")
        bpy.ops.render.render(write_still=True)
        print("RENDER_DONE")
        sys.exit(0)
    if not ONLY or "terrain" in ONLY:
        build_terrain()
        cam = bpy.context.scene.camera
        base = cam.location.copy()
        right = cam.matrix_world.to_3x3() @ Vector((1, 0, 0))
        up = cam.matrix_world.to_3x3() @ Vector((0, 1, 0))
        for j in range(CHUNKS):  # j counts rows from the top, i columns from the left.
            for i in range(CHUNKS):
                if CENTER and (i, j) != (1, 1):
                    continue
                dx = (i - (CHUNKS - 1) / 2) * TERRAIN_W / CHUNKS
                dy = ((CHUNKS - 1) / 2 - j) * TERRAIN_H / CHUNKS
                cam.location = base + right * dx * K + up * dy * K
                name = f"colony_terrain_{i}_{j}" + suffix() + ("_preview" if PREVIEW else "")
                bpy.context.scene.render.filepath = os.path.join(OUT, name + ".png")
                bpy.ops.render.render(write_still=True)
    SPRITES["tile_road"] = build_tile_road_glow
    for name, builder in SPRITES.items():
        if ONLY and name not in ONLY:
            continue
        sprite_scene()
        if NIGHT:
            apply_night()
        builder()
        bpy.context.scene.render.filepath = os.path.join(OUT, name + suffix() + ".png")
        bpy.ops.render.render(write_still=True)
    for name, builder in LANDMARKS.items():
        if ONLY and name not in ONLY:
            continue
        landmark_scene()
        builder()
        bpy.context.scene.render.filepath = os.path.join(OUT, name + suffix() + ".png")
        bpy.ops.render.render(write_still=True)
        if name in PARTS:
            # The moving part alone, frame by frame; the still scene is hidden but still lights it.
            part, frames, make = PARTS[name]
            still = [o for o in bpy.context.scene.objects if o.type == "MESH"]
            for o in still:
                o.hide_render = True
            m = lm_mats()
            for f in range(frames):
                before = set(bpy.context.scene.objects)
                make(m, f / frames)
                bpy.context.scene.render.filepath = os.path.join(OUT, f"{name}_{part}_{f}" + suffix() + ".png")
                bpy.ops.render.render(write_still=True)
                for o in set(bpy.context.scene.objects) - before:
                    bpy.data.objects.remove(o)
    print("RENDER_DONE")

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
ONLY = [a for a in EXTRA if a not in ("preview", "night")]
N = 26
ZONES = [(22, 22), (23, 15), (13, 21), (17, 11), (8, 15), (11, 7), (4, 9), (4, 3)]
K = math.sqrt(2)
ISO_ROT = (math.radians(60), 0, math.radians(45))
TERRAIN_W, TERRAIN_H = 28.0, 15.0
# The terrain is rendered in 3 x 3 pieces so it stays sharp when the player zooms in.
CHUNKS = 3
CHUNK_PX = 2048


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


def build_island_ground(roads):
    from mathutils import noise
    bpy.ops.mesh.primitive_grid_add(x_subdivisions=300, y_subdivisions=300, size=64, location=(12.5, -12.5, 0))
    ground = bpy.context.active_object
    for v in ground.data.vertices:
        gx, gy = v.co.x + 12.5, -(v.co.y - 12.5)
        m = coast_metric(gx, gy)
        # Hills only away from the zones, roads and river; the playable ground stays flat.
        dz = zone_distance(gx, gy)
        dr = min((max(abs(gx - x), abs(gy - y)) for x, y in roads), default=9)
        dw = river_distance(gx, gy)
        open_ground = min(1, max(0, dz - .8) / 2.5) * min(1, max(0, dr - .8) / 1.5) * min(1, max(0, dw - .9) / 1.5)
        hill = (noise.noise(Vector((gx * .16, gy * .16, 1.7))) + .35) * .8 * open_ground
        z = max(0, hill)
        if dw < .55:
            z = -.18  # River bed.
        cliff = max(0, min(1, (m - COAST) / .7))
        z = z * (1 - cliff) + (-2.6) * (cliff * cliff * (3 - 2 * cliff))
        v.co.z = z
    bpy.ops.object.shade_smooth()
    ground.data.materials.append(island_material())


def pine(x, y, scale, leaf, trunk):
    cyl((x, y, .12 * scale), .05 * scale, .24 * scale, trunk, bevel=0, vertices=8)
    for k, (r, z) in enumerate(((.32, .38), (.25, .62), (.16, .84))):
        bpy.ops.mesh.primitive_cone_add(vertices=9, radius1=r * scale, depth=.42 * scale, location=(x, y, z * scale))
        rs.finish(bpy.context.active_object, leaf, 0)


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

    roads = road_tiles()
    build_island_ground(roads)
    bpy.ops.mesh.primitive_plane_add(size=1, location=(12.5, -12.5, -1.9))
    sea = bpy.context.active_object
    sea.scale = (90, 90, 1)
    sea.data.materials.append(water_material())

    # River with a waterfall where it leaves the island.
    water = water_material()
    foam = material("Foam", "#d8eef2", 0, .3, 0, emission="#bfe8f2", strength=.25)
    for a, b in zip(RIVER, RIVER[1:]):
        (ax, ay), (bx, by) = a, b
        length = math.hypot(bx - ax, by - ay)
        box(((ax + bx) / 2, -(ay + by) / 2, -.11), (length + .2, .7, .01), water, bevel=0, rot=math.atan2(-(by - ay), bx - ax))
    for gx in [g / 10 for g in range(90, -90, -1)]:  # Walk the river west until it reaches the cliff.
        if coast_metric(gx, 6.1) > COAST:
            # The water pours down the cliff face as a steep sheet, with foam where it meets the sea.
            drop = Vector((gx - 1.1, -6.1, -1.9)) - Vector((gx + .2, -6.1, -.1))
            bpy.ops.mesh.primitive_plane_add(size=1, location=(gx - .45, -6.1, -1.0))
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
        pine(gx, -gy, rng.uniform(.7, 1.25), leaf if rng.random() < .6 else leaf_light, trunk)
    for _ in range(320):  # Loose rocks.
        gx, gy = rng.uniform(-6, 31), rng.uniform(-6, 31)
        if coast_metric(gx, gy) > COAST - .3 or zone_distance(gx, gy) < .6:
            continue
        r = rng.uniform(.06, .24)
        bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=2, radius=r, location=(gx, -gy, r * .3))
        stone = bpy.context.active_object
        stone.scale = (1, rng.uniform(.6, 1.3), rng.uniform(.4, .8))
        rs.finish(stone, rock, 0)


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


SPRITES = {
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
    }


def windows(x, y, z0, w, d, h, warm, floors_gap=.16):
    """Lit window bands on the two faces the camera sees."""
    for f in range(int((h - .1) / floors_gap)):
        z = z0 + .12 + f * floors_gap
        box((x + w / 2 + .005, y, z), (.01, d * .8, .05), warm, bevel=0)
        box((x, y - d / 2 - .005, z), (w * .8, .01, .05), warm, bevel=0)


def build_lm_landing():
    m = lm_mats()
    box((0, 0, .05), (2.8, 2.8, .1), m["concrete"], bevel=.04)
    torus((0, 0, .11), 1.0, .03, m["yellow"])
    for i in range(12):
        a = i * math.tau / 12
        cyl((math.cos(a) * 1.25, math.sin(a) * 1.25, .12), .04, .03, m["cyan"], bevel=.005, vertices=12)
    box((1.05, -1.05, .7), (.42, .42, 1.3), m["white"], bevel=.05)  # Control tower.
    cyl((1.05, -1.05, 1.45), .32, .2, m["dark"], bevel=.03)
    torus((1.05, -1.05, 1.45), .32, .03, m["cyan"])
    windows(1.05, -1.05, .1, .42, .42, 1.2, m["warm"])
    cyl((1.05, -1.05, 1.75), .02, .4, m["steel"], bevel=0)


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
    for x, y, r in ((-.7, .6, .45), (.6, .65, .4), (-.6, -.65, .38)):
        cyl((x, y, .55), r, .9, m["white"], bevel=.05)
        torus((x, y, 1.0), r - .03, .03, m["steel"])
        torus((x, y, .5), r + .01, .03, m["cyan"])
    cyl((.65, -.6, .14), .5, .06, material("Pool", "#1b6f8f", .1, .05, 0, emission="#1aa8d8", strength=.5), bevel=.01)
    box((.1, 0, .3), (.4, .3, .4), m["teal"], bevel=.04)
    windows(.1, 0, .1, .4, .3, .4, m["warm"])


def build_lm_greenhouse():
    m = lm_mats()
    box((0, 0, .05), (2.8, 2.8, .1), m["concrete"], bevel=.04)
    glass = transparent("Glass", "#b8f0cf", .4)
    for x in (-.75, 0, .75):
        bpy.ops.mesh.primitive_cylinder_add(vertices=24, radius=.32, depth=2.2, location=(x, 0, .1), rotation=(math.radians(90), 0, 0))
        hall = bpy.context.active_object
        rs.finish(hall, glass, 0, smooth=True)
        box((x, 0, .14), (.5, 2.1, .06), m["green"], bevel=.01)
        for y in (-.8, -.4, 0, .4, .8):
            torus((x, y, .1), .32, .015, m["steel"])


def build_lm_mines():
    m = lm_mats()
    pit = material("Pit", "#3a2d26", .1, .9, .5)
    box((0, 0, .03), (2.8, 2.8, .06), pit, bevel=.04)
    for i, r in enumerate((1.0, .75, .5)):
        torus((-.2, .2, .06 - i * .02), r, .07, material("Terrace", "#5b4636", .1, .9, .5))
    box((.75, -.7, .3), (.6, .35, .3), m["yellow"], bevel=.04)  # Excavator body.
    box((.75, -.7, .52), (.25, .25, .15), m["dark"], bevel=.03)
    beam((.55, -.6, .5), (.1, -.2, .9), .05, m["yellow"])
    beam((.1, -.2, .9), (-.15, .05, .35), .04, m["yellow"])
    box((-.15, .05, .3), (.2, .15, .12), m["steel"], bevel=.02)
    for x, y in ((.8, .7), (1.05, .35)):
        bpy.ops.mesh.primitive_cone_add(radius1=.3, depth=.35, location=(x, y, .17))
        rs.finish(bpy.context.active_object, material("Ore", "#7a6a5a", .2, .85, .3), 0)
    beam((-1.0, -1.0, .1), (-.4, -.4, .6), .05, m["steel"])
    cyl((-.4, -.4, .62), .06, .06, m["warm"], bevel=.01)


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


def build_lm_port():
    m = lm_mats()
    box((0, 0, .05), (2.8, 2.8, .1), m["concrete"], bevel=.04)
    box((-1.0, 0, .02), (.9, 2.8, .04), material("Harbor", "#123e52", .1, .06, 0, emission="#0f6f8f", strength=.2), bevel=.01)
    for k, (x, y, color) in enumerate(((.2, .7, "#8a4b2f"), (.2, .35, "#2f6a72"), (.65, .7, "#c9a23a"), (.2, 0, "#2f6a72"), (.65, .35, "#8a4b2f"))):
        box((x, y, .25), (.4, .3, .28), material(f"Box{k}", color, .6, .5, .5), bevel=.02)
    box((-.35, -.7, 1.0), (.1, .1, 1.8), m["yellow"], bevel=.01)
    beam((-.35, -.7, 1.85), (-1.2, -.2, 1.85), .05, m["yellow"])
    beam((-.35, -.7, 1.85), (.4, -.9, 1.85), .05, m["yellow"])
    for y in (-1.1, 1.1):
        cyl((-.5, y, .4), .03, .6, m["steel"], bevel=0)
        ball((-.5, y, .72), .05, m["warm"])


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
    if not ONLY or "terrain" in ONLY:
        build_terrain()
        cam = bpy.context.scene.camera
        base = cam.location.copy()
        right = cam.matrix_world.to_3x3() @ Vector((1, 0, 0))
        up = cam.matrix_world.to_3x3() @ Vector((0, 1, 0))
        for j in range(CHUNKS):  # j counts rows from the top, i columns from the left.
            for i in range(CHUNKS):
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
    print("RENDER_DONE")

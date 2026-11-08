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
ONLY = [a for a in EXTRA if a != "preview"]
N = 26
ZONES = [(22, 22), (23, 15), (13, 21), (17, 11), (8, 15), (11, 7), (4, 9), (4, 3)]
K = math.sqrt(2)
ISO_ROT = (math.radians(60), 0, math.radians(45))
TERRAIN_W, TERRAIN_H, TERRAIN_PPU = 28.0, 15.0, 72


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


# ---- Terrain ------------------------------------------------------------------------

def build_terrain():
    rs.clear()
    rs.SCENE_SIZE = (int(TERRAIN_W * TERRAIN_PPU), int(TERRAIN_H * TERRAIN_PPU))
    if PREVIEW:
        rs.SCENE_SIZE = (rs.SCENE_SIZE[0] // 2, rs.SCENE_SIZE[1] // 2)
    rs.setup(10)
    scene = bpy.context.scene
    scene.render.film_transparent = False
    scene.cycles.samples = 24 if PREVIEW else 96
    for obj in list(scene.objects):
        if obj.type == "MESH":
            bpy.data.objects.remove(obj)
    center = ((N - 1) / 2, -(N - 1) / 2, 0)
    iso_camera(center, TERRAIN_W * K)

    bpy.ops.mesh.primitive_plane_add(size=1, location=(center[0], center[1], -.05))
    ground = bpy.context.active_object
    ground.scale = (N + 70, N + 70, 1)
    bpy.ops.object.transform_apply(scale=True)
    ground.data.materials.append(render_map.terrain())

    rng = random.Random(5)
    rock = material("Rock", "#4f3a2e", .1, .85, .6)
    cliff = material("Cliff", "#5b4232", .1, .9, .6)
    for x, y, r in ((6, -20, 1.4), (19, -4, 1.1), (1, -15, .9), (25, -9, .8), (15, -17, .7), (2, 2, 1.6), (27, -27, 1.5), (-3, -8, 1.2)):
        if not in_zone(x, y, .5):
            render_map.crater(x, y, r, rock)
    for _ in range(420):  # Loose rocks, kept off the zones so their tiles stay clear.
        x, y = rng.uniform(-8, N + 8), rng.uniform(-N - 8, 8)
        if in_zone(x, y, .6):
            continue
        r = rng.uniform(.06, .22)
        bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=2, radius=r, location=(x, y, r * .3))
        stone = bpy.context.active_object
        stone.scale = (1, rng.uniform(.6, 1.3), rng.uniform(.4, .8))
        rs.finish(stone, rock, 0)
    for _ in range(26):  # Mesas around the edge of the valley give the map depth.
        a = rng.uniform(0, math.tau)
        dist = rng.uniform(19, 24)
        x, y = center[0] + math.cos(a) * dist, center[1] + math.sin(a) * dist
        h = rng.uniform(.6, 1.8)
        prism(regular(7, rng.uniform(1, 2.2), rng.uniform(0, 1), x, y, 1, rng.uniform(.7, 1.2)), h / 2, h, cliff, .15)


# ---- Sprites (one tile, isometric) --------------------------------------------------

def sprite_scene():
    rs.clear()
    rs.SCENE_SIZE = (256, 256)
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


SPRITES = {
    "tile_pave": build_tile_pave, "tile_road": build_tile_road, "deco_lamp": build_deco_lamp, "deco_ship": build_deco_ship,
    "deco_barricade": build_deco_barricade, "bld_extractor": build_bld_extractor, "bld_laboratory": build_bld_laboratory,
    "bld_refinery": build_bld_refinery, "bld_fortification": build_bld_fortification, "bld_housing": build_bld_housing,
}

if __name__ == "__main__":
    os.makedirs(OUT, exist_ok=True)
    if not ONLY or "terrain" in ONLY:
        build_terrain()
        bpy.context.scene.render.filepath = os.path.join(OUT, "colony_terrain_preview.png" if PREVIEW else "colony_terrain.png")
        bpy.ops.render.render(write_still=True)
    for name, builder in SPRITES.items():
        if ONLY and name not in ONLY:
            continue
        sprite_scene()
        builder()
        bpy.context.scene.render.filepath = os.path.join(OUT, name + ".png")
        bpy.ops.render.render(write_still=True)
    print("RENDER_DONE")

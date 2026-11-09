"""Renders the battlefield background: the 7 x 11 deck plus colony scenery on both sides.

Usage:
  blender --background --factory-startup --python Tools/Render/render_board.py -- OUTPUT_DIR [WxH] [preview]

Without WxH it renders the 7 x 11 Quick Play deck as board.png; with it, a campaign deck as board_WxH.png.

One cell is one Blender unit, centered on (column, -row), exactly as in Unity. The image is
17 x 11.6 units at 96 pixels per unit, wider than any phone so the sides are always covered.
"""
import math
import os
import random
import sys

import bpy

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import render_sprites as rs  # noqa: E402  Shared materials and shapes; its render loop only runs as main.
from render_sprites import along_y, box, cyl, glowing, material, prism, regular, srgb, torus, transparent  # noqa: E402

OUT = sys.argv[sys.argv.index("--") + 1]
EXTRA = sys.argv[sys.argv.index("--") + 2:]
PREVIEW = "preview" in EXTRA
SIZE_ARG = next((a for a in EXTRA if "x" in a and a.replace("x", "").isdigit()), None)
W, H = (int(v) for v in SIZE_ARG.split("x")) if SIZE_ARG else (7, 11)
# Campaign decks stand out in the open on the planet, framed by its ground on every side.
CAMPAIGN = SIZE_ARG is not None
WIDTH, HEIGHT, PPU = (12.0, H + 8.0, 96) if CAMPAIGN else (17.0, H + .6, 96)
SY = (H - 1) / 10  # Scenery positions were laid out for 11 rows; shorter decks pull them in.
# Narrow decks show less ground on screen, so the colony buildings move in toward the deck.
G = 1.0 if W >= 7 else .55 if W <= 3 else .72
CENTER = ((W - 1) / 2, -(H - 1) / 2)
random.seed(9)


def soil():
    """Dark rocky ground of the planet around the colony."""
    mat = bpy.data.materials.new("Soil")
    mat.use_nodes = True
    nodes, links = mat.node_tree.nodes, mat.node_tree.links
    bsdf = next(n for n in nodes if n.type == "BSDF_PRINCIPLED")
    bsdf.inputs["Roughness"].default_value = .92
    coords = nodes.new("ShaderNodeTexCoord")
    noise = nodes.new("ShaderNodeTexNoise")
    noise.inputs["Scale"].default_value = 1.6
    noise.inputs["Detail"].default_value = 10
    links.new(coords.outputs["Object"], noise.inputs["Vector"])
    color = rs.mix_color(nodes, links, noise.outputs[0], srgb("#15181b"), srgb("#2c2826"))
    links.new(color, bsdf.inputs["Base Color"])
    pebbles = nodes.new("ShaderNodeTexVoronoi")
    pebbles.inputs["Scale"].default_value = 9
    links.new(coords.outputs["Object"], pebbles.inputs["Vector"])
    bump = nodes.new("ShaderNodeBump")
    bump.inputs["Strength"].default_value = .8
    links.new(pebbles.outputs["Distance"], bump.inputs["Height"])
    links.new(bump.outputs[0], bsdf.inputs["Normal"])
    return mat


def outpost(frame, steel, dark, warm, cyan):
    """A tidy field base: the deck sits on a concrete apron with railings, lights and consoles; pine woods beyond."""
    concrete = material("Apron", "#4c565d", .3, .62, .35)
    seam = material("ApronSeam", "#2b3338", .5, .7, .2)
    yellow = material("Yellow", "#e0ac1a", .2, .5, .4)
    black = material("Black", "#15171a", .2, .6, .3)
    teal = material("Teal", "#2f6a72", .6, .45, .4)
    leaf = material("Pine", "#2e4630", .05, .8, .4)
    leaf_light = material("PineLight", "#3b5a37", .05, .8, .4)
    trunk = material("Trunk", "#3a2a22", .1, .9, .3)
    rock = material("Rock", "#4f453e", .1, .85, .6)
    screen = glowing("Screen", "#5ee8ff", 1.8)

    half_w, top, bottom = W / 2 + 2.8, 2.0, -(H - 1) - 2.2
    cx, cy = CENTER[0], (top + bottom) / 2
    box((cx, cy, .02), (half_w * 2, top - bottom, .06), concrete, bevel=.03)
    # Panel seams on the apron, a slab every unit, never over the deck itself.
    x0, x1 = cx - half_w, cx + half_w
    for k in range(int(half_w * 2) + 1):
        x = x0 + k
        box((x, cy, .052), (.03, top - bottom - .1, .004), seam, bevel=0)
    for k in range(int(top - bottom) + 1):
        y = bottom + k
        box((cx, y, .052), (half_w * 2 - .1, .03, .004), seam, bevel=0)
    # Hazard band and railing around the apron's edge.
    for (ax, ay), (bx, by) in (((x0, top), (x1, top)), ((x1, top), (x1, bottom)), ((x1, bottom), (x0, bottom)), ((x0, bottom), (x0, top))):
        length = math.hypot(bx - ax, by - ay)
        steps = int(length / .3)
        for i in range(steps):
            t = (i + .5) / steps
            px, py = ax + (bx - ax) * t, ay + (by - ay) * t
            vertical = abs(bx - ax) < .01
            box((px, py, .06), (.12 if vertical else length / steps * .9, length / steps * .9 if vertical else .12, .012),
                yellow if i % 2 else black, bevel=.002)
        for i in range(steps // 3 + 1):  # Railing posts.
            t = i / max(1, steps // 3)
            px, py = ax + (bx - ax) * t, ay + (by - ay) * t
            cyl((px, py, .2), .025, .32, steel, bevel=.004, vertices=10)
        box(((ax + bx) / 2, (ay + by) / 2, .34), (length if abs(by - ay) < .01 else .04, length if abs(bx - ax) < .01 else .04, .03), steel, bevel=.008)
    # Floodlight towers at the corners.
    for x, y in ((x0 + .35, top - .35), (x1 - .35, top - .35), (x0 + .35, bottom + .35), (x1 - .35, bottom + .35)):
        cyl((x, y, .7), .05, 1.4, steel, bevel=.01)
        box((x, y, 1.42), (.3, .2, .08), dark, bevel=.02)
        box((x, y - .02, 1.38), (.22, .14, .02), warm, bevel=.004)
    # Conduits and consoles along each side of the deck.
    for side in (-1, 1):
        px = cx + side * (W / 2 + .75)
        bpy.ops.mesh.primitive_cylinder_add(vertices=16, radius=.07, depth=H + 1.2, location=(px, CENTER[1], .12), rotation=(math.radians(90), 0, 0))
        rs.finish(bpy.context.active_object, teal, 0, smooth=True)
        for k in range(H):
            box((px, -k, .12), (.2, .06, .18), steel, bevel=.01)
        for y in (CENTER[1] + 1.2, CENTER[1] - 1.2):
            kx = cx + side * (W / 2 + 1.7)
            box((kx, y, .25), (.55, .4, .45), dark, bevel=.04)
            box((kx - side * .2, y, .36), (.02, .3, .2), screen, bevel=0)
            cyl((kx + side * .15, y + .12, .7), .015, .5, steel, bevel=0, vertices=8)
            cyl((kx + side * .15, y + .12, .96), .03, .02, cyan, bevel=.003, vertices=12)
    # Pine woods beyond the apron, kept clear of it.
    rng = random.Random(W * 31 + H)
    for _ in range(int(WIDTH * HEIGHT * .55)):
        x = rng.uniform(CENTER[0] - WIDTH / 2 - 1, CENTER[0] + WIDTH / 2 + 1)
        y = rng.uniform(CENTER[1] - HEIGHT / 2 - 1, CENTER[1] + HEIGHT / 2 + 1)
        if x0 - .5 < x < x1 + .5 and bottom - .5 < y < top + .5:
            continue
        if rng.random() < .25:
            r = rng.uniform(.06, .2)
            bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=2, radius=r, location=(x, y, r * .3))
            stone = bpy.context.active_object
            stone.scale = (1, rng.uniform(.6, 1.3), rng.uniform(.4, .8))
            rs.finish(stone, rock, 0)
            continue
        s = rng.uniform(.7, 1.2)
        cyl((x, y, .12 * s), .05 * s, .24 * s, trunk, bevel=0, vertices=8)
        for r, z in ((.32, .38), (.25, .62), (.16, .84)):
            bpy.ops.mesh.primitive_cone_add(vertices=9, radius1=r * s, depth=.42 * s, location=(x, y, z * s))
            rs.finish(bpy.context.active_object, leaf if rng.random() < .6 else leaf_light, 0)


def build():
    rs.clear()
    rs.SCENE_SIZE = (int(WIDTH * PPU), int(round(HEIGHT * PPU)))
    if PREVIEW:
        rs.SCENE_SIZE = (rs.SCENE_SIZE[0] // 2, rs.SCENE_SIZE[1] // 2)
    rs.setup(max(WIDTH, HEIGHT))  # Blender's ortho scale spans the longer side of the image.
    scene = bpy.context.scene
    scene.render.film_transparent = False
    scene.cycles.samples = 24 if PREVIEW else 80
    for obj in list(scene.objects):
        if obj.type == "MESH":
            bpy.data.objects.remove(obj)
    scene.camera.location = (CENTER[0], CENTER[1], 10)

    bpy.ops.mesh.primitive_plane_add(size=1, location=(CENTER[0], CENTER[1], -.02))
    ground = bpy.context.active_object
    ground.scale = (WIDTH + 2, HEIGHT + 2, 1)
    if CAMPAIGN:
        import render_colony  # The island's mossy ground, so missions match the colony map.
        ground.data.materials.append(render_colony.island_material())
    else:
        ground.data.materials.append(soil())

    deck = material("Deck", "#30404a", .85, .5, .55)
    deck_alt = material("DeckAlt", "#2b3842", .85, .52, .55)
    threat = material("Threat", "#40272b", .8, .55, .6)
    base = material("Base", "#1f3b36", .8, .5, .5)
    frame = material("Frame", "#16202a", .8, .55, .4)
    steel = material("Steel", "#a9b6bc", 1, .32, .3)
    dark = material("Dark", "#11181d", .8, .6, .3)
    yellow = material("Yellow", "#e0ac1a", .2, .5, .5)
    black = material("Black", "#15171a", .2, .6, .3)
    cyan = glowing("Cyan", "#58e6f2", 1.6)
    warm = glowing("Warm", "#ffc77a", 1.8)
    green = glowing("Green", "#6df0b4", 1.4)

    # ---- The deck: one plate per cell -------------------------------------------------
    box((CENTER[0], CENTER[1], .02), (W + .3, H + .3, .06), frame, bevel=.03)
    for r in range(H):
        for c in range(W):
            mat = threat if r == 0 else base if r == H - 1 else (deck if (r + c) % 2 == 0 else deck_alt)
            box((c, -r, .075), (.955, .955, .05), mat, bevel=.018)
            for dx, dy in ((-.39, -.39), (.39, -.39), (-.39, .39), (.39, .39)):
                cyl((c + dx, -r + dy, .102), .022, .012, steel, bevel=.003, vertices=6)
            if 0 < r < H - 1 and random.random() < .16:  # Occasional vent grilles.
                for i in range(4):
                    box((c, -r - .15 + i * .1, .104), (.46, .045, .01), dark, bevel=.003)
    for i in range(28):  # Hazard edge where the enemies cross into the colony.
        x = -.5 + (i + .5) * W / 28
        box((x, -.47, .11), (W / 28 * .92, .06, .012), yellow if i % 2 else black, bevel=.002)
    box((CENTER[0], -(H - 1) + .47, .11), (W - .1, .035, .012), green, bevel=.003)
    for c in range(W):  # Landing marks on the base row.
        cyl((c, -(H - 1), .104), .18, .008, transparent("Mark", "#9de6c9", .35), bevel=0, vertices=32)

    # ---- Rails with lights along both sides of the deck ---------------------------------
    for side in (-1, 1):
        x = CENTER[0] + side * (W / 2 + .2)
        box((x, CENTER[1], .14), (.1, H + .3, .1), steel, bevel=.02)
        for r in range(H):
            box((x, -r, .1), (.16, .16, .16), frame, bevel=.02)
            cyl((x, -r, .19), .035, .012, cyan if r % 2 else warm, bevel=.003, vertices=16)

    if CAMPAIGN:
        outpost(frame, steel, dark, warm, cyan)
        return

    # ---- Colony scenery on both sides ---------------------------------------------------
    pipe = material("Pipe", "#6f7d84", .9, .38, .5)
    rust = material("Rust", "#8a4b2f", .5, .6, .7)
    teal = material("Teal", "#2f6a72", .6, .48, .6)
    grate = material("Grate", "#2a3238", .85, .5, .5)
    panel = material("Panel", "#1a2c4a", .4, .18, .2)
    white = material("White", "#c9d3d6", .4, .35, .4)
    top, bottom = .9, -(H - 1) - .9
    length = top - bottom
    for side in (-1, 1):
        x0 = CENTER[0] + side * (W / 2 + .55)
        for i, (offset, radius) in enumerate(((0, .09), (.24, .12), (.5, .08))):
            along_y((x0 + side * offset, (top + bottom) / 2, .16), radius, length, pipe if i != 1 else rust, bevel=.01)
        for k in range(max(2, int(length / 1.55))):
            box((x0 + side * .25, top - .6 - k * 1.55, .2), (.68, .09, .07), dark, bevel=.015)
        walk = CENTER[0] + side * (W / 2 + 1.55)
        if G == 1.0:  # The service walkway only fits beside the full-width deck.
            box((walk, (top + bottom) / 2, .04), (.8, length, .04), grate, bevel=.01)
            for k in range(int(length / .2)):
                box((walk, top - .1 - k * .2, .07), (.76, .03, .01), dark, bevel=.002)
        else:
            walk = CENTER[0] + side * (W / 2 + 1.25)
        for y in (-1.5 * SY, -5.5 * SY, -9.2 * SY):  # Floodlights.
            cyl((walk + side * .52, y, .3), .05, .6, steel, bevel=.01)
            box((walk + side * .38, y, .62), (.3, .16, .08), dark, bevel=.02)
            box((walk + side * .3, y, .57), (.16, .12, .02), warm, bevel=.005)

    left = CENTER[0] - W / 2 - 2.9 * G - (.35 if G < 1 else 0)
    for y in (-.8, -2.2):  # Fuel tanks.
        cyl((left - .35, y, .45), .62, .9, white, bevel=.04)
        torus((left - .35, y, .9), .5, .04, steel)
        torus((left - .35, y, .55), .63, .03, rust)
        cyl((left - .35, y, .92), .15, .04, dark, bevel=.01)
    for i, (y, mat) in enumerate(((-4.4 * SY, rust), (-4.4 * SY - .8, teal), (-4.4 * SY - .4, white))):  # Container stack.
        box((left - .2 - (i == 2) * .9, y, .3 + (i == 2) * .0), (1.3, .7, .6), mat, bevel=.03)
        for k in range(6):
            box((left - .2 - (i == 2) * .9 - .55 + k * .22, y, .61), (.05, .66, .015), dark, bevel=.003)
    dish = -8.2 * SY
    cyl((left - .1, dish, .35), .7, .1, steel, bevel=.02)   # Communications dish.
    cyl((left - .1, dish, .45), .55, .08, white, bevel=.03)
    cyl((left - .1, dish, .5), .07, .2, dark, bevel=.01)
    cyl((left - .1, dish, .62), .04, .02, warm, bevel=.004)
    for y in (-6.6 * SY, -6.6 * SY - .5):
        box((left + .7, y, .2), (.45, .4, .4), rust, bevel=.03)

    right = CENTER[0] + W / 2 + 2.9 * G + (.2 if G < 1 else 0)
    for row in range(max(2, round(5 * SY))):  # Solar panel array.
        for col in range(2):
            x, y = right - .1 + col * 1.05, -5.6 * SY - row * .95
            box((x, y, .12), (.12, .12, .24), steel, bevel=.02)
            box((x, y, .27), (.95, .82, .03), panel, bevel=.012)
            for k in range(3):
                box((x, y - .27 + k * .27, .29), (.92, .015, .006), steel, bevel=.002)
    bpy.ops.mesh.primitive_uv_sphere_add(segments=48, ring_count=24, radius=1.05, location=(right + .55, -1.3, 0))
    dome = bpy.context.active_object  # Habitat dome.
    rs.finish(dome, white, 0, smooth=True)
    torus((right + .55, -1.3, .05), 1.05, .07, steel)
    for a in (200, 250):
        r = math.radians(a)
        box((right + .55 + math.cos(r) * .95, -1.3 + math.sin(r) * .95, .25), (.18, .1, .2), dark, bevel=.02)
        box((right + .55 + math.cos(r) * 1.0, -1.3 + math.sin(r) * 1.0, .3), (.12, .06, .06), warm, bevel=.01)
    mast = -3.6 * SY
    cyl((right - .5, mast, .6), .06, 1.2, steel, bevel=.01)  # Antenna mast.
    for z in (.5, .8, 1.1):
        box((right - .5, mast, z), (.4, .03, .03), steel, bevel=.005)
    cyl((right - .5, mast, 1.22), .05, .03, warm, bevel=.005)
    for y in (-3.0 * SY, -3.0 * SY - 1.3):
        box((right + .5, y, .2), (.55, .45, .4), teal, bevel=.03)


build()
name = f"board_{W}x{H}" if SIZE_ARG else "board"
bpy.context.scene.render.filepath = os.path.join(OUT, name + ("_preview.png" if PREVIEW else ".png"))
bpy.ops.render.render(write_still=True)
print("RENDER_DONE")

"""Renders the campaign map: the planet's surface seen from above, with the colony's sectors.

Usage:
  blender --background --factory-startup --python Tools/Render/render_map.py -- OUTPUT_DIR [preview]

The image is portrait, 1 : 2. Sector sites sit at the same normalized positions as
CampaignData.cs (MapX, MapY from the bottom-left), so the Unity markers land on them.
"""
import math
import os
import random
import sys

import bpy

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import render_sprites as rs  # noqa: E402
from render_sprites import ball, box, cyl, glowing, material, prism, regular, srgb, torus, transparent  # noqa: E402

OUT = sys.argv[sys.argv.index("--") + 1] if "--" in sys.argv else "."
PREVIEW = "--" in sys.argv and "preview" in sys.argv[sys.argv.index("--") + 2:]
MAP_W, MAP_H = 9.0, 18.0
random.seed(17)

# Same order and positions as CampaignData.Sectors.
SECTORS = [(.3, .14), (.66, .25), (.36, .37), (.68, .5), (.3, .62), (.64, .72), (.34, .82), (.6, .91)]


def at(nx, ny):
    return ((nx - .5) * MAP_W, (ny - .5) * MAP_H)


def terrain():
    """Rust and ochre regolith with dark dust bands and pebbles."""
    mat = bpy.data.materials.new("Terrain")
    mat.use_nodes = True
    nodes, links = mat.node_tree.nodes, mat.node_tree.links
    bsdf = next(n for n in nodes if n.type == "BSDF_PRINCIPLED")
    bsdf.inputs["Roughness"].default_value = .95
    coords = nodes.new("ShaderNodeTexCoord")
    broad = nodes.new("ShaderNodeTexNoise")
    broad.inputs["Scale"].default_value = .35
    broad.inputs["Detail"].default_value = 8
    links.new(coords.outputs["Object"], broad.inputs["Vector"])
    color = rs.mix_color(nodes, links, broad.outputs[0], srgb("#3a2a22"), srgb("#6b4a33"))
    fine = nodes.new("ShaderNodeTexNoise")
    fine.inputs["Scale"].default_value = 6
    fine.inputs["Detail"].default_value = 12
    links.new(coords.outputs["Object"], fine.inputs["Vector"])
    ramp = nodes.new("ShaderNodeMapRange")
    ramp.inputs["From Min"].default_value = .45
    ramp.inputs["From Max"].default_value = .7
    ramp.inputs["To Max"].default_value = .45
    links.new(fine.outputs[0], ramp.inputs["Value"])
    color = rs.mix_color(nodes, links, ramp.outputs[0], color, srgb("#22191a"))
    links.new(color, bsdf.inputs["Base Color"])
    pebbles = nodes.new("ShaderNodeTexVoronoi")
    pebbles.inputs["Scale"].default_value = 14
    links.new(coords.outputs["Object"], pebbles.inputs["Vector"])
    bump = nodes.new("ShaderNodeBump")
    bump.inputs["Strength"].default_value = .55
    links.new(pebbles.outputs["Distance"], bump.inputs["Height"])
    links.new(bump.outputs[0], bsdf.inputs["Normal"])
    return mat


def ground():
    bpy.ops.mesh.primitive_plane_add(size=1, location=(0, 0, -.1))
    plane = bpy.context.active_object
    plane.scale = (MAP_W + 2, MAP_H + 2, 1)
    bpy.ops.object.transform_apply(scale=True)
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.subdivide(number_cuts=120)
    bpy.ops.object.mode_set(mode="OBJECT")
    tex = bpy.data.textures.new("Dunes", type="CLOUDS")
    tex.noise_scale = 2.4
    disp = plane.modifiers.new("Relief", "DISPLACE")
    disp.texture = tex
    disp.strength = .18
    disp.mid_level = .5
    plane.data.materials.append(terrain())
    bpy.ops.object.shade_smooth()


def crater(x, y, radius, rock):
    torus((x, y, .02), radius, radius * .16, rock)
    cyl((x, y, -.02), radius * .9, .03, material("Pit", "#24191a", .1, .95, .2), bevel=.01)


def rocks(rock, count):
    for _ in range(count):
        x, y = random.uniform(-MAP_W / 2, MAP_W / 2), random.uniform(-MAP_H / 2, MAP_H / 2)
        if any(math.hypot(x - at(*s)[0], y - at(*s)[1]) < 1.2 for s in SECTORS):
            continue
        r = random.uniform(.05, .16)
        bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=2, radius=r, location=(x, y, r * .3))
        stone = bpy.context.active_object
        stone.scale = (1, random.uniform(.6, 1.2), random.uniform(.4, .7))
        rs.finish(stone, rock, 0)


def road(points, mat, width=.22):
    for (x0, y0), (x1, y1) in zip(points, points[1:]):
        length = math.hypot(x1 - x0, y1 - y0)
        box(((x0 + x1) / 2, (y0 + y1) / 2, .03), (length + width, width, .03), mat, bevel=.01, rot=math.atan2(y1 - y0, x1 - x0))


def pad(x, y, radius, deck, rim, light):
    """Every sector stands on a cleared, plated base."""
    prism(regular(8, radius, math.pi / 8, x, y), .05, .08, deck, .03)
    prism(regular(8, radius * .93, math.pi / 8, x, y), .095, .01, rim, .004)
    for i in range(8):
        a = i * math.tau / 8 + math.pi / 8
        cyl((x + math.cos(a) * radius * .86, y + math.sin(a) * radius * .86, .11), .04, .02, light, bevel=.004, vertices=12)


def build():
    rs.clear()
    rs.SCENE_SIZE = (540, 1080) if PREVIEW else (1080, 2160)
    rs.setup(MAP_H)
    scene = bpy.context.scene
    scene.render.film_transparent = False
    scene.cycles.samples = 24 if PREVIEW else 96
    for obj in list(scene.objects):
        if obj.type == "MESH":
            bpy.data.objects.remove(obj)
    scene.camera.location = (0, 0, 20)
    scene.camera.data.ortho_scale = MAP_H

    ground()
    rock = material("Rock", "#4f3a2e", .1, .85, .6)
    deck = material("Deck", "#3a4850", .85, .5, .5)
    rim = material("Rim", "#2a343b", .8, .55, .4)
    steel = material("Steel", "#b9c5ca", 1, .3, .3)
    white = material("White", "#d4dcde", .4, .35, .4)
    dark = material("Dark", "#151c21", .8, .55, .3)
    rust = material("Rust", "#8a4b2f", .5, .6, .6)
    teal = material("Teal", "#2f6a72", .6, .48, .5)
    panel = material("Panel", "#1a2c4a", .4, .18, .2)
    trail = material("Trail", "#2a211d", .1, .95, .4)
    cyan = glowing("Cyan", "#58e6f2", 1.6)
    warm = glowing("Warm", "#ffc77a", 1.8)
    green = glowing("Green", "#6df0b4", 1.5)

    for x, y, r in ((-3.2, 7.4, .8), (3.4, 3.1, .6), (-3.6, -3.4, .9), (3.1, -6.8, .7), (.6, -1.4, .45), (-1.4, 4.6, .4), (2.5, 8.3, .5)):
        crater(x, y, r, rock)
    rocks(rock, 160)
    road([at(*s) for s in SECTORS], trail)

    sites = [at(*s) for s in SECTORS]
    for x, y in sites:
        pad(x, y, .95, deck, rim, cyan)

    # 1 Landing zone: the colony ship on its pad.
    x, y = sites[0]
    prism([(x, y + .75), (x + .32, y + .1), (x + .62, y - .45), (x + .2, y - .38), (x, y - .6), (x - .2, y - .38), (x - .62, y - .45), (x - .32, y + .1)], .3, .16, white, .03)
    prism([(x, y + .5), (x + .14, y + .05), (x - .14, y + .05)], .4, .04, panel, .01)
    for side in (-1, 1):
        cyl((x + side * .2, y - .5, .3), .09, .12, dark, bevel=.02, rot=(math.radians(90), 0, 0))
        cyl((x + side * .2, y - .58, .3), .06, .02, warm, bevel=.005, rot=(math.radians(90), 0, 0))

    # 2 Camp: prefab shelters around a fire-lit yard.
    x, y = sites[1]
    for dx, dy, rot in ((-.45, .3, .2), (.45, .32, -.2), (-.42, -.35, -.15), (.4, -.36, .15)):
        box((x + dx, y + dy, .2), (.42, .26, .22), teal if dx < 0 else rust, bevel=.06, rot=rot)
        box((x + dx, y + dy, .32), (.38, .05, .02), white, bevel=.01, rot=rot)
    cyl((x, y, .12), .12, .04, dark, bevel=.01)
    ball((x, y, .16), .06, warm)

    # 3 Water well: drilling tower over a pool, with storage tanks.
    x, y = sites[2]
    cyl((x - .25, y, .08), .38, .04, material("Water", "#1b6f8f", .1, .05, 0, emission="#1aa8d8", strength=.4), bevel=.01)
    for dx, dy in ((-.45, -.2), (-.05, -.2), (-.45, .2), (-.05, .2)):
        cyl((x + dx, y + dy, .35), .03, .6, steel, bevel=.005)
    box((x - .25, y, .66), (.5, .5, .04), steel, bevel=.01)
    for dy in (-.35, .35):
        cyl((x + .45, y + dy, .25), .22, .4, white, bevel=.03)
        torus((x + .45, y + dy, .45), .17, .02, steel)

    # 4 Greenhouses: glass domes with green light.
    x, y = sites[3]
    glass = transparent("Glass", "#9de6c0", .45)
    for dx, dy, r in ((-.35, .2, .32), (.35, .25, .28), (0, -.35, .3)):
        cyl((x + dx, y + dy, .1), r * .7, .04, green, bevel=.005)
        bpy.ops.mesh.primitive_uv_sphere_add(segments=32, ring_count=16, radius=r, location=(x + dx, y + dy, .08))
        dome = bpy.context.active_object
        dome.scale = (1, 1, .7)
        rs.finish(dome, glass, 0, smooth=True)

    # 5 Mines: open pit with a conveyor and ore piles.
    x, y = sites[4]
    for i, r in enumerate((.6, .45, .3)):
        torus((x, y, .06 - i * .03), r, .05, rock)
    cyl((x, y, -.04), .25, .04, material("Ore", "#2a1f1c", .2, .9, .2), bevel=.01)
    box((x + .55, y + .2, .3), (.9, .1, .05), steel, bevel=.01, rot=.5)
    for dx, dy in ((.75, -.3), (.95, .1)):
        bpy.ops.mesh.primitive_cone_add(radius1=.2, depth=.2, location=(x + dx, y + dy, .1))
        rs.finish(bpy.context.active_object, material("Pile", "#7a6a5a", .2, .9, .3), 0)

    # 6 Central workshop: sheds with hazard roofs and a crane.
    x, y = sites[5]
    for dx, dy in ((-.35, 0), (.35, .05)):
        box((x + dx, y + dy, .25), (.5, .75, .3), material("Shed", "#56636b", .8, .45, .5), bevel=.03)
        for k in range(5):
            box((x + dx, y + dy - .3 + k * .15, .41), (.48, .05, .01), material("Yellow", "#e0ac1a", .2, .5, .5) if k % 2 else dark, bevel=.003)
    box((x, y - .55, .5), (1.1, .06, .06), material("Crane", "#e8b24a", .6, .34, .35), bevel=.01)
    cyl((x - .5, y - .55, .25), .05, .5, steel, bevel=.01)

    # 7 Port: two landing platforms with beacon lights.
    x, y = sites[6]
    for dx in (-.35, .4):
        cyl((x + dx, y, .14), .34, .06, deck, bevel=.02)
        torus((x + dx, y, .18), .3, .02, warm)
        box((x + dx, y, .18), (.3, .04, .01), white, bevel=.002)
        box((x + dx, y, .18), (.04, .3, .01), white, bevel=.002)

    # 8 Colony core: the great reactor dome.
    x, y = sites[7]
    cyl((x, y, .15), .7, .1, steel, bevel=.03)
    bpy.ops.mesh.primitive_uv_sphere_add(segments=48, ring_count=24, radius=.55, location=(x, y, .18))
    dome = bpy.context.active_object
    dome.scale = (1, 1, .6)
    rs.finish(dome, white, 0, smooth=True)
    torus((x, y, .3), .58, .04, green)
    cyl((x, y, .52), .14, .04, green, bevel=.01)
    for i in range(6):
        a = i * math.tau / 6
        box((x + math.cos(a) * .8, y + math.sin(a) * .8, .12), (.3, .08, .06), dark, bevel=.01, rot=a)

    # Solar fields and pylons along the road give the colony its scale.
    for (x0, y0), (x1, y1) in zip(sites, sites[1:]):
        mx, my = (x0 + x1) / 2, (y0 + y1) / 2
        cyl((mx + .5, my, .3), .03, .6, steel, bevel=.005)
        cyl((mx + .5, my, .62), .04, .02, warm, bevel=.004)
    for bx, by in ((-3.0, -6.6), (2.9, 1.0), (-2.9, 5.6)):
        for r in range(3):
            for c in range(3):
                box((bx + c * .42, by - r * .36, .1), (.38, .3, .02), panel, bevel=.006)


if __name__ == "__main__":
    build()
    bpy.context.scene.render.filepath = os.path.join(OUT, "map_preview.png" if PREVIEW else "map.png")
    bpy.ops.render.render(write_still=True)
    print("RENDER_DONE")

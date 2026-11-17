"""Renders the colony buildings: every kind in each of its looks and its three levels.

Usage:
  blender --background --factory-startup --python Tools/Render/render_buildings.py -- OUTPUT_DIR [names] [night]

Writes bld_<kind>_<look>_<level>.png (512 pixels for 2 Unity units, like the other tile sprites). A name
filter may be a kind ("housing") or a full name ("bld_housing_1_3"). The looks per kind must match
ColonyData.cs (Styles).
"""
import math
import os
import sys

import bpy

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import render_colony as rc  # noqa: E402
import render_sprites as rs  # noqa: E402
from render_colony import beam, lattice_mast  # noqa: E402
from render_sprites import ball, box, cyl, glowing, material, prism, regular, torus, transparent  # noqa: E402

OUT = rc.OUT
NAMES = [a for a in rc.EXTRA if a not in ("night", "preview")]
R90 = math.radians(90)


def mats():
    return {
        "concrete": material("Concrete", "#5a646b", .3, .6, .6),
        "white": material("White", "#dde5e8", .3, .32, .35),
        "cream": material("Cream", "#e6dcc8", .1, .5, .4),
        "steel": material("Steel", "#c3ced3", 1, .28, .25),
        "dark": material("Dark", "#151c21", .8, .5, .3),
        "teal": material("Teal", "#2f6a72", .6, .48, .5),
        "roof": material("Roof", "#a8553a", .2, .6, .5),
        "blue": material("Blue", "#2c7fc0", .5, .35, .4),
        "rust": material("Rust", "#a85a34", .5, .55, .5),
        "yellow": material("Yellow", "#e0ac1a", .3, .45, .45),
        "plate": material("Plate", "#4f5b62", .85, .45, .5),
        "panel": material("Panel", "#2a5fb0", .3, .15, .1, emission="#1c4f9a", strength=.35),
        "ore": material("Ore", "#7a6a5a", .2, .85, .3),
        "crate": material("Crate", "#c49a5a", .1, .7, .4),
        "soil": material("Soil", "#3a2d26", .1, .9, .4),
        "leaf": material("Leaf", "#3f9a4a", 0, .7, .2, emission="#5bd46a", strength=.45),
        "glass": transparent("Glass", "#c8f5dc", .3),
        "warm": glowing("Warm", "#ffc77a", 2.3),
        "cyan": glowing("Cyan", "#5ee8ff", 2.5),
        "green": glowing("Green", "#7dff9e", 2.2),
        "hot": glowing("Hot", "#ffa040", 2.6),
        "red": glowing("Red", "#ff5a42", 2.6),
        "sun": glowing("Sun", "#ffd36a", 2.4),
    }


def pad(m, size=.86):
    box((0, 0, .04), (size, size, .08), m["concrete"], bevel=.03)


def win(m, x, y, z0, w, d, h, gap=.13, mat="warm"):
    """Lit window bands on the two faces in view, floor by floor."""
    for f in range(max(0, int((h - .06) / gap))):
        z = z0 + .09 + f * gap
        box((x + w / 2 + .004, y, z), (.008, d * .78, .045), m[mat], bevel=0)
        box((x, y - d / 2 - .004, z), (w * .78, .008, .045), m[mat], bevel=0)


def gable(x, y, z, w, d, h, mat):
    """Pitched roof with its ridge along y."""
    v = [(-w / 2, -d / 2, 0), (w / 2, -d / 2, 0), (0, -d / 2, h), (-w / 2, d / 2, 0), (w / 2, d / 2, 0), (0, d / 2, h)]
    f = [(0, 1, 2), (3, 5, 4), (0, 3, 4, 1), (1, 4, 5, 2), (2, 5, 3, 0)]
    mesh = bpy.data.meshes.new("Gable")
    mesh.from_pydata(v, [], f)
    obj = bpy.data.objects.new("Gable", mesh)
    obj.location = (x, y, z)
    bpy.context.collection.objects.link(obj)
    bpy.context.view_layer.objects.active = obj
    return rs.finish(obj, mat, 0)


def dome(x, y, z, r, mat, squash=.9):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=40, ring_count=20, radius=r, location=(x, y, z))
    obj = bpy.context.active_object
    obj.scale = (1, 1, squash)
    return rs.finish(obj, mat, 0, smooth=True)


def cone(x, y, z, r, h, mat, vertices=12):
    bpy.ops.mesh.primitive_cone_add(vertices=vertices, radius1=r, depth=h, location=(x, y, z + h / 2))
    return rs.finish(bpy.context.active_object, mat, 0)


def house(m, x, y, w, d, h, wall="cream", roof="roof"):
    box((x, y, .08 + h / 2), (w, d, h), m[wall], bevel=.015)
    gable(x, y, .08 + h, w + .05, d + .05, w * .42, m[roof])
    box((x + w / 2 + .004, y - d * .15, .08 + h * .55), (.008, d * .3, h * .35), m["warm"], bevel=0)
    box((x - w * .12, y - d / 2 - .004, .08 + h * .55), (w * .3, .008, h * .35), m["warm"], bevel=0)
    box((x + w * .22, y - d / 2 - .004, .08 + h * .3), (w * .2, .008, h * .6), m["dark"], bevel=0)


def block(m, x, y, w, d, h, wall="white", top="teal", gap=.13):
    box((x, y, .08 + h / 2), (w, d, h), m[wall], bevel=.025)
    box((x, y, .09 + h), (w * .82, d * .82, .025), m[top], bevel=.008)
    win(m, x, y, .08, w, d, h, gap)


# ---- Housing: cottages, apartment blocks, round habitat towers ---------------------------

def housing_0(m, level):
    pad(m)
    if level == 1:
        house(m, 0, .05, .42, .5, .26)
    elif level == 2:
        house(m, -.18, .16, .36, .42, .26)
        house(m, .2, -.14, .36, .42, .3, roof="teal")
    else:
        house(m, -.2, .2, .34, .38, .28)
        house(m, .2, .2, .34, .38, .4, roof="teal")
        house(m, -.18, -.2, .34, .34, .24, roof="teal")
        house(m, .2, -.22, .3, .3, .3)
        cyl((0, 0, .3), .012, .44, m["steel"], bevel=0, vertices=8)
        ball((0, 0, .54), .035, m["warm"])
    cone(.37, -.37, .08, .06, .2, m["leaf"])  # A small tree by the corner.


def housing_1(m, level):
    pad(m)
    if level == 1:
        block(m, 0, .04, .56, .46, .3)
    elif level == 2:
        block(m, -.1, .1, .46, .46, .58)
        block(m, .24, -.2, .3, .3, .28, top="roof")
    else:
        block(m, -.14, .14, .44, .44, .98)
        block(m, .22, .14, .3, .44, .6)
        block(m, -.14, -.24, .44, .28, .42, top="roof")
        beam((-.14, .14, 1.08), (-.14, .14, 1.4), .01, m["steel"])
        ball((-.14, .14, 1.42), .025, m["red"])
        for x in (.16, .28):  # Roof garden.
            cone(x, .14, .7, .05, .12, m["leaf"])


def housing_2(m, level):
    pad(m)
    h = (.28, .7, 1.15)[level - 1]
    r = (.3, .26, .27)[level - 1]
    cyl((0, 0, .08 + h / 2), r, h, m["white"], bevel=.03)
    for f in range(max(1, int(h / .14))):
        torus((0, 0, .17 + f * .14), r + .002, .014, m["warm"])
    if level == 1:
        dome(0, 0, .08 + h, r, m["white"], .45)
        torus((0, 0, .08 + h), r, .02, m["teal"])
    else:
        cyl((0, 0, .1 + h), r + .05, .04, m["teal"], bevel=.012)
        dome(0, 0, .12 + h, r * .6, m["glass"], .6)
    if level == 3:
        for z in (.5, .85):
            torus((0, 0, z), r + .05, .025, m["teal"])
        beam((0, 0, 1.3), (0, 0, 1.62), .01, m["steel"])
        ball((0, 0, 1.64), .025, m["red"])
        cyl((.3, -.3, .14), .1, .12, m["white"], bevel=.02)
        torus((.3, -.3, .2), .1, .012, m["cyan"])


# ---- Extractor: a derrick, or a pump with its silo ---------------------------------------

def derrick(m, x, y, s, h):
    apex = (x, y, h)
    for dx, dy in ((-s, -s), (s, -s), (s, s), (-s, s)):
        beam((x + dx, y + dy, .08), apex, .026, m["rust"])
    for z in (h * .35, h * .62):
        r = s * (1 - (z - .08) / (h - .08))
        for (x0, y0), (x1, y1) in (((-r, -r), (r, -r)), ((r, -r), (r, r)), ((r, r), (-r, r)), ((-r, r), (-r, -r))):
            beam((x + x0, y + y0, z), (x + x1, y + y1, z), .013, m["steel"])
    box((x, y, h), (.12, .12, .09), m["dark"], bevel=.02)
    cyl((x, y, h * .3), .035, h * .45, m["steel"], bevel=.005)
    cyl((x, y, .1), .1, .04, m["hot"], bevel=.008)


def extractor_0(m, level):
    pad(m)
    if level == 1:
        derrick(m, 0, 0, .26, .85)
    elif level == 2:
        derrick(m, -.08, .08, .28, 1.1)
        box((.28, -.28, .19), (.24, .2, .22), m["steel"], bevel=.03)
        for i in range(4):
            ball((.22 + (i % 2) * .1, -.3 + (i // 2) * .08, .32), .04, m["ore"])
    else:
        derrick(m, -.12, .12, .26, 1.35)
        derrick(m, .22, -.2, .16, .7)
        beam((-.12, -.3, .14), (.3, .2, .5), .04, m["dark"])
        cone(.32, .26, .08, .14, .2, m["ore"])
        box((-.3, -.3, .17), (.18, .18, .18), m["yellow"], bevel=.02)
        ball((-.12, .12, 1.44), .03, m["red"])


def extractor_1(m, level):
    pad(m)
    n = level
    # Pump jacks nodding over their wells.
    for k in range(n):
        y = (-.0, -.2, -.28)[n - 1] + k * (.0, .4, .28)[n - 1]
        x = -.14
        box((x, y, .12), (.3, .1, .08), m["dark"], bevel=.015)
        beam((x, y, .12), (x, y, .42 + .06 * level), .022, m["yellow"])
        beam((x - .22, y, .44 + .06 * level), (x + .24, y, .38 + .06 * level), .022, m["yellow"])
        box((x + .25, y, .34 + .06 * level), (.07, .06, .14), m["yellow"], bevel=.015)
        beam((x + .25, y, .3), (x + .25, y, .1), .008, m["steel"])
        cyl((x + .25, y, .1), .04, .04, m["hot"], bevel=.005)
    h = (.34, .55, .8)[level - 1]
    cyl((.26, .24 if level > 1 else .26, .08 + h / 2), .13, h, m["steel"], bevel=.03)
    cone(.26, .24 if level > 1 else .26, .08 + h, .14, .1, m["rust"], 24)
    if level == 3:
        cyl((.28, -.26, .3), .1, .44, m["steel"], bevel=.03)
        beam((.26, .24, .5), (.28, -.26, .5), .025, m["rust"])


# ---- Laboratory: a dome, or stacked modules ----------------------------------------------

def laboratory_0(m, level):
    pad(m)
    r = (.24, .28, .32)[level - 1]
    box((0, 0, .15), (.68, .68, .14), m["white"], bevel=.05)
    for side in (-1, 1):
        box((side * .341, 0, .15), (.008, .5, .05), m["cyan"], bevel=0)
        box((0, side * .341, .15), (.5, .008, .05), m["cyan"], bevel=0)
    zc = .22 if level < 3 else .34
    if level == 3:
        cyl((0, 0, .28), r, .14, m["white"], bevel=.02)
        torus((0, 0, .24), r + .004, .014, m["cyan"])
    dome(0, 0, zc, r, m["white"])
    torus((0, 0, zc + .04), r, .022, m["cyan"])
    if level >= 2:
        cyl((.26, .26, .55), .018, .7, m["steel"], bevel=.004)
        cyl((.26, .26, .92), .11, .018, m["steel"], bevel=.006, rot=(math.radians(35), 0, math.radians(45)))
        ball((.26, .26, .96), .028, m["cyan"])
    if level == 3:
        torus((0, 0, zc + .2), r * .72, .018, m["cyan"])
        box((-.26, -.26, .3), (.14, .14, .2), m["dark"], bevel=.02)
        ball((0, 0, zc + r * .9), .04, m["cyan"])


def laboratory_1(m, level):
    pad(m)
    mods = [(0, .02, .08, .6, .5, .24)]
    if level >= 2:
        mods.append((-.08, .08, .32, .44, .4, .24))
    if level == 3:
        mods.append((-.14, .12, .56, .3, .3, .26))
    for x, y, z, w, d, h in mods:
        box((x, y, z + h / 2), (w, d, h), m["white"], bevel=.03)
        box((x + w / 2 + .004, y, z + h * .55), (.008, d * .8, h * .3), m["cyan"], bevel=0)
        box((x, y - d / 2 - .004, z + h * .55), (w * .8, .008, h * .3), m["cyan"], bevel=0)
    top = mods[-1][2] + mods[-1][5]
    if level >= 2:
        dome(.22, -.16, .32, .12, m["glass"], .8)
    beam((mods[-1][0], mods[-1][1], top), (mods[-1][0], mods[-1][1], top + .3), .01, m["steel"])
    ball((mods[-1][0], mods[-1][1], top + .32), .028, m["cyan"])
    if level == 3:
        for y in (.28, .16):
            cyl((.26, y, .5), .04, .28, m["steel"], bevel=.01)
        box((.26, .22, .38), (.16, .3, .1), m["dark"], bevel=.02)


# ---- Solar: fields of tilted panels, or sun-tracking dishes around a tower ---------------

def solar_0(m, level):
    pad(m)
    rows = {1: [(-.0, 0)], 2: [(-.2, 0), (.2, 0)], 3: [(-.26, 0), (0, 0), (.26, 0)]}[level]
    for x, _ in rows:
        for y in ((-.2, .2) if level > 1 else (-.18, .18)):
            z = .26 + .05 * level
            cyl((x, y, z / 2 + .04), .018, z - .06, m["steel"], bevel=.004)
            w = .4 if level == 1 else .34 if level == 2 else .24
            box((x, y, z - .012), (w + .03, .37, .016), m["steel"], bevel=.004)
            bpy.context.active_object.rotation_euler = (math.radians(-25), 0, 0)
            box((x, y, z), (w, .34, .02), m["panel"], bevel=.006)
            bpy.context.active_object.rotation_euler = (math.radians(-25), 0, 0)
            for k in range(3):
                box((x, y - .1 + k * .1, z + .014 + (k - 1) * -.045), (w - .02, .006, .004), m["steel"], bevel=0)
    if level == 1:
        cyl((.3, .3, .14), .05, .1, m["steel"], bevel=.01)
        ball((.3, .3, .21), .03, m["sun"])
    else:
        box((.0 if level == 2 else .13, 0, .15), (.08, .14, .14), m["dark"], bevel=.02)
        ball((.0 if level == 2 else .13, 0, .25), .03, m["sun"])
    if level == 3:
        box((-.13, 0, .15), (.08, .14, .14), m["dark"], bevel=.02)
        ball((-.13, 0, .25), .03, m["sun"])


def solar_1(m, level):
    pad(m)
    h = (.5, .8, 1.15)[level - 1]
    cyl((0, 0, .08 + h / 2), .05 + .01 * level, h, m["white"], bevel=.01)
    cyl((0, 0, .1 + h), .09 + .01 * level, .1, m["dark"], bevel=.02)
    ball((0, 0, .18 + h), .06 + .012 * level, m["sun"])
    torus((0, 0, .1 + h), .1 + .01 * level, .012, m["sun"])
    n = (3, 5, 8)[level - 1]
    for i in range(n):
        a = i * math.tau / n + .4
        x, y = math.cos(a) * .3, math.sin(a) * .3
        cyl((x, y, .16), .012, .16, m["steel"], bevel=0, vertices=8)
        box((x, y, .26), (.15, .15, .012), m["panel"], bevel=.004, rot=a)
        obj = bpy.context.active_object
        obj.rotation_euler = (math.sin(a) * .5, -math.cos(a) * .5, a)


# ---- Refinery, fortification -------------------------------------------------------------

def refinery_0(m, level):
    pad(m)
    tanks = {1: [(-.14, .12)], 2: [(-.2, .18), (-.2, -.18)], 3: [(-.22, .2), (-.22, -.18), (.1, .26)]}[level]
    th = .5 + .08 * level
    for x, y in tanks:
        cyl((x, y, .08 + th / 2), .15, th, m["white"], bevel=.04)
        torus((x, y, .06 + th), .12, .014, m["steel"])
        for z in (.25, .08 + th * .7):
            torus((x, y, z), .155, .011, m["rust"])
    ch = (.7, 1.0, 1.35)[level - 1]
    cx, cy = (.2, -.12) if level < 3 else (.2, -.14)
    cyl((cx, cy, .08 + ch / 2), .08, ch, m["steel"], bevel=.02)
    for k in range(1, 2 + level):
        torus((cx, cy, .08 + ch * k / (2 + level)), .085, .013, m["rust"])
    for x, y in tanks:
        beam((x, y, .4), (cx, cy, .4), .026, m["rust"])
    fx, fy = .32, -.32
    fh = ch + .12
    cyl((fx, fy, .08 + fh / 2), .022, fh, m["steel"], bevel=.004)
    ball((fx, fy, .14 + fh), .05 + .008 * level, m["hot"])


def fortification_0(m, level):
    r = .4 + .03 * level
    prism(regular(6, r, math.pi / 6), .14 + .03 * level, .3 + .08 * level, m["concrete"], .04)
    top = .3 + .1 * level
    for i in range(6):
        a = i * math.tau / 6
        box((math.cos(a) * (r - .06), math.sin(a) * (r - .06), top + .05), (.13, .17, .11), m["plate"], bevel=.02, rot=a)
    cyl((0, 0, top + .1), .16, .2, m["plate"], bevel=.03)
    beam((0, 0, top + .16), (.32, .32, top + .3), .032, m["steel"])
    if level >= 2:
        beam((0, 0, top + .16), (.34, -.2, top + .28), .026, m["steel"])
        torus((0, 0, top - .04), r - .02, .016, m["red"])
    if level == 3:
        cyl((0, 0, top + .3), .1, .2, m["plate"], bevel=.02)
        cyl((0, 0, top + .46), .14, .02, m["steel"], bevel=.006, rot=(math.radians(30), 0, math.radians(45)))
        for i in range(3):
            a = i * math.tau / 3 + .5
            box((math.cos(a) * (r + .04), math.sin(a) * (r + .04), .16), (.06, .3, .3), m["plate"], bevel=.02, rot=a)
    cyl((0, 0, top + .22 + (.2 if level == 3 else 0)), .045, .02, m["red"], bevel=.005)


# ---- New kinds: hydroponic farm, water tower, works yard ---------------------------------

def farm_0(m, level):
    pad(m)
    if level < 3:
        halls = [0] if level == 1 else [-.2, .2]
        w = .28 if level == 1 else .17
        for x in halls:
            box((x, 0, .1), (w * 2 + .04, .76, .05), m["dark"], bevel=.01)
            bpy.ops.mesh.primitive_cylinder_add(vertices=28, radius=w, depth=.72, location=(x, 0, .11), rotation=(R90, 0, 0))
            rs.finish(bpy.context.active_object, m["glass"], 0, smooth=True)
            for y in (-.36, -.12, .12, .36):
                torus((x, y, .11), w, .012, m["steel"])
                bpy.context.active_object.rotation_euler = (R90, 0, 0)
            for row in ((-.1, .1) if level == 1 else (0,)):
                box((x + row, 0, .15), (.09, .66, .04), m["green"], bevel=.008)
                for k in range(6):
                    cone(x + row, -.3 + k * .12, .16, .04, .11, m["leaf"], 7)
        if level == 2:
            cyl((0, .0, .2), .05, .26, m["blue"], bevel=.01)
    else:
        # Vertical farm: glass floors of lit crops around a core.
        box((0, 0, .7), (.2, .2, 1.24), m["white"], bevel=.02)
        for f in range(5):
            z = .16 + f * .24
            box((0, 0, z), (.66, .66, .03), m["dark"], bevel=.008)
            box((0, 0, z + .03), (.6, .6, .03), m["green"], bevel=.006)
            for i in range(8):
                a = i * math.tau / 8
                cone(math.cos(a) * .24, math.sin(a) * .24, z + .04, .045, .11, m["leaf"], 7)
            for dx, dy in ((-.31, -.31), (.31, -.31), (.31, .31), (-.31, .31)):
                cyl((dx, dy, z + .12), .012, .24, m["steel"], bevel=0, vertices=8)
        box((0, 0, 1.36), (.68, .68, .04), m["teal"], bevel=.012)
        ball((0, 0, 1.42), .04, m["green"])


def watertower_0(m, level):
    pad(m)
    leg = (.36, .6, .8)[level - 1]
    r = (.2, .25, .3)[level - 1]
    s = r * .75
    for dx, dy in ((-s, -s), (s, -s), (s, s), (-s, s)):
        beam((dx * 1.25, dy * 1.25, .08), (dx, dy, .08 + leg), .02, m["steel"])
    for z in (.08 + leg * .5,):
        q = s * 1.12
        for (x0, y0), (x1, y1) in (((-q, -q), (q, -q)), ((q, -q), (q, q)), ((q, q), (-q, q)), ((-q, q), (-q, -q))):
            beam((x0, y0, z), (x1, y1, z), .011, m["steel"])
    if level == 1:
        cyl((0, 0, .08 + leg + .13), r, .26, m["white"], bevel=.03)
        cone(0, 0, .08 + leg + .26, r + .02, .12, m["blue"], 28)
    else:
        dome(0, 0, .08 + leg + r * .75, r, m["white"], .8)
        torus((0, 0, .08 + leg + r * .75), r + .003, .02, m["blue"])
    torus((0, 0, .08 + leg + (.06 if level == 1 else r * .45)), r * (1 if level == 1 else .9) + .004, .014, m["cyan"])
    cyl((0, 0, .08 + leg / 2), .035, leg, m["blue"], bevel=.006)
    if level >= 2:
        box((.28, -.28, .16), (.16, .16, .16), m["teal"], bevel=.02)
        beam((0, 0, .14), (.28, -.28, .14), .022, m["blue"])
    if level == 3:
        cyl((-.3, .3, .26), .1, .36, m["white"], bevel=.02)
        torus((-.3, .3, .3), .102, .012, m["cyan"])
        ball((0, 0, .08 + leg + r * 1.58), .028, m["red"])


def works_0(m, level):
    pad(m)
    w, d, h = ((.44, .4, .24), (.5, .5, .32), (.56, .6, .4))[level - 1]
    x, y = -.12, .1
    box((x, y, .08 + h / 2), (w, d, h), m["plate"], bevel=.03)
    for k in range(int(d / .11)):
        box((x, y - d / 2 + .06 + k * .11, .1 + h), (w - .02, .06, .03), m["yellow"] if k % 2 else m["dark"], bevel=.008)
    box((x + w / 2 + .004, y, .08 + h * .42), (.008, d * .55, h * .7), m["warm"], bevel=0)  # Open door, lit inside.
    # Tower crane: taller with each level.
    ch = (.7, 1.05, 1.4)[level - 1]
    cx, cy = .3, -.3
    for dx, dy in ((-.03, -.03), (.03, -.03), (.03, .03), (-.03, .03)):
        beam((cx + dx, cy + dy, .08), (cx + dx, cy + dy, ch), .009, m["yellow"])
    for k in range(int(ch / .14)):
        z = .12 + k * .14
        beam((cx - .03, cy - .03, z), (cx + .03, cy + .03, z + .14), .006, m["yellow"])
    beam((cx + .16, cy - .1, ch + .02), (cx - .5, cy + .32, ch + .02), .018, m["yellow"])
    box((cx + .14, cy - .09, ch - .03), (.1, .1, .08), m["dark"], bevel=.015)
    hx, hy = cx - .36, cy + .23
    beam((hx, hy, ch), (hx, hy, ch - .3), .004, m["steel"])
    box((hx, hy, ch - .34), (.1, .1, .08), m["crate"], bevel=.012)
    ball((cx, cy, ch + .07), .025, m["red"])
    for k, (bx, by) in enumerate(((.3, .28), (.14, .34), (.32, .1))[:level]):
        box((bx, by, .15), (.13, .13, .13), m["crate"] if k % 2 == 0 else m["teal"], bevel=.015)
    if level == 3:
        box((-.3, -.32, .14), (.22, .14, .1), m["yellow"], bevel=.02)  # Loader.
        box((-.34, -.32, .22), (.1, .12, .08), m["dark"], bevel=.015)


BUILDERS = {
    "housing": [housing_0, housing_1, housing_2], "extractor": [extractor_0, extractor_1], "laboratory": [laboratory_0, laboratory_1],
    "solar": [solar_0, solar_1], "refinery": [refinery_0], "fortification": [fortification_0], "farm": [farm_0],
    "watertower": [watertower_0], "works": [works_0],
}

if __name__ == "__main__":
    os.makedirs(OUT, exist_ok=True)
    for kind, looks in BUILDERS.items():
        for look, builder in enumerate(looks):
            for level in (1, 2, 3):
                name = f"bld_{kind}_{look}_{level}"
                if NAMES and kind not in NAMES and name not in NAMES:
                    continue
                rc.sprite_scene()
                if rc.NIGHT:
                    rc.apply_night()
                builder(mats(), level)
                bpy.context.scene.render.filepath = os.path.join(OUT, name + rc.suffix() + ".png")
                bpy.ops.render.render(write_still=True)
    print("RENDER_DONE")

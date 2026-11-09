"""Renders the game sprites for Colonia 09 from procedural 3D models, seen from straight above.

Usage:
  blender --background --factory-startup --python Tools/Render/render_sprites.py -- OUTPUT_DIR [names]
"""
import math
import os
import sys

import bpy
from mathutils import Vector

OUT = sys.argv[sys.argv.index("--") + 1]
SIZE = 384
SAMPLES = 96
LAYER = "base"  # Moving parts (fan, barrel, rotor) are rendered as separate layers that Unity animates.
PHASE = 0.0  # Animation position of the frame being rendered, from 0 to 1.
os.makedirs(OUT, exist_ok=True)


def clear():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def srgb(html):
    html = html.lstrip("#")
    result = []
    for i in (0, 2, 4):
        c = int(html[i:i + 2], 16) / 255
        result.append(c / 12.92 if c <= .04045 else ((c + .055) / 1.055) ** 2.4)
    return (*result, 1)


def material(name, color, metallic=.9, rough=.42, wear=.35, emission=None, strength=0):
    """Principled material with procedural scratches and fine surface bumps."""
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    nodes, links = mat.node_tree.nodes, mat.node_tree.links
    bsdf = next(n for n in nodes if n.type == "BSDF_PRINCIPLED")
    bsdf.inputs["Base Color"].default_value = srgb(color)
    bsdf.inputs["Metallic"].default_value = metallic
    bsdf.inputs["Roughness"].default_value = rough
    if emission:
        bsdf.inputs["Emission Color"].default_value = srgb(emission)
        bsdf.inputs["Emission Strength"].default_value = strength
    if wear <= 0:
        bsdf.inputs["Base Color"].default_value = srgb(color)
        return mat
    coords = nodes.new("ShaderNodeTexCoord")
    noise = nodes.new("ShaderNodeTexNoise")
    noise.inputs["Scale"].default_value = 70
    noise.inputs["Detail"].default_value = 9
    links.new(coords.outputs["Object"], noise.inputs["Vector"])
    ramp = nodes.new("ShaderNodeMapRange")
    ramp.inputs["From Min"].default_value = .35
    ramp.inputs["From Max"].default_value = .72
    ramp.inputs["To Min"].default_value = rough * (1 - wear * .5)
    ramp.inputs["To Max"].default_value = min(1, rough * (1 + wear * .8))
    links.new(noise.outputs[0], ramp.inputs["Value"])
    links.new(ramp.outputs[0], bsdf.inputs["Roughness"])
    bump = nodes.new("ShaderNodeBump")
    bump.inputs["Strength"].default_value = .05 * wear
    bump.inputs["Distance"].default_value = .02
    links.new(noise.outputs[0], bump.inputs["Height"])
    links.new(bump.outputs[0], bsdf.inputs["Normal"])

    # Realism: grime collects in crevices (ambient occlusion) and paint wears off the edges.
    base = srgb(color)
    grime = (base[0] * .18, base[1] * .17, base[2] * .16, 1)
    worn = tuple(min(1, c * .55 + .4) for c in base[:3]) + (1,)
    ao = nodes.new("ShaderNodeAmbientOcclusion")
    ao.inputs["Distance"].default_value = .09
    ao.only_local = True
    grime_mask = nodes.new("ShaderNodeMapRange")
    grime_mask.inputs["From Min"].default_value = 1
    grime_mask.inputs["From Max"].default_value = .55
    grime_mask.inputs["To Max"].default_value = 1
    links.new(ao.outputs["AO"], grime_mask.inputs["Value"])
    bevel = nodes.new("ShaderNodeBevel")
    bevel.inputs["Radius"].default_value = .014
    geometry = nodes.new("ShaderNodeNewGeometry")
    dot = nodes.new("ShaderNodeVectorMath")
    dot.operation = "DOT_PRODUCT"
    links.new(bevel.outputs["Normal"], dot.inputs[0])
    links.new(geometry.outputs["Normal"], dot.inputs[1])
    edge = nodes.new("ShaderNodeMapRange")
    edge.inputs["From Min"].default_value = .995
    edge.inputs["From Max"].default_value = .9
    edge.inputs["To Max"].default_value = min(1, wear * 3.2)
    links.new(dot.outputs["Value"], edge.inputs["Value"])
    chips = nodes.new("ShaderNodeMath")  # Breaks the edge wear up so it looks chipped, not outlined.
    chips.operation = "MULTIPLY"
    links.new(edge.outputs[0], chips.inputs[0])
    links.new(noise.outputs[0], chips.inputs[1])
    dirty = mix_color(nodes, links, grime_mask.outputs[0], base, grime)
    # Large dirt patches and streaks, so surfaces do not look factory clean.
    dirt_noise = nodes.new("ShaderNodeTexNoise")
    dirt_noise.inputs["Scale"].default_value = 7
    dirt_noise.inputs["Detail"].default_value = 6
    links.new(coords.outputs["Object"], dirt_noise.inputs["Vector"])
    dirt_mask = nodes.new("ShaderNodeMapRange")
    dirt_mask.inputs["From Min"].default_value = .5
    dirt_mask.inputs["From Max"].default_value = .75
    dirt_mask.inputs["To Max"].default_value = .45 * min(1, wear * 2)
    links.new(dirt_noise.outputs[0], dirt_mask.inputs["Value"])
    stained = mix_color(nodes, links, dirt_mask.outputs[0], dirty, (base[0] * .45, base[1] * .4, base[2] * .34, 1))
    final = mix_color(nodes, links, chips.outputs[0], stained, worn)
    links.new(final, bsdf.inputs["Base Color"])
    return mat


def mix_color(nodes, links, factor, a, b):
    """Color mix where a and b can be colors or node sockets. Returns the result socket."""
    mix = nodes.new("ShaderNodeMix")
    mix.data_type = "RGBA"
    socket = lambda name: next(s for s in mix.inputs if s.name == name and s.type == "RGBA")
    links.new(factor, mix.inputs[0])
    for name, value in (("A", a), ("B", b)):
        if isinstance(value, tuple):
            socket(name).default_value = value
        else:
            links.new(value, socket(name))
    return next(s for s in mix.outputs if s.type == "RGBA")

def finish(obj, mat, bevel=0, smooth=False):
    obj["layer"] = LAYER
    obj.data.materials.append(mat)
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    if bevel:
        mod = obj.modifiers.new("Bevel", "BEVEL")
        mod.width = bevel
        mod.segments = 3
        mod.limit_method = "ANGLE"
    if smooth:
        try:
            bpy.ops.object.shade_auto_smooth(angle=math.radians(40))
        except Exception:
            bpy.ops.object.shade_smooth()
    return obj


def box(loc, size, mat, bevel=.012, rot=0):
    bpy.ops.mesh.primitive_cube_add(size=1, location=loc, rotation=(0, 0, rot))
    obj = bpy.context.active_object
    obj.scale = size
    bpy.ops.object.transform_apply(scale=True, location=False, rotation=False)
    return finish(obj, mat, bevel)


def cyl(loc, radius, depth, mat, bevel=.008, vertices=64, rot=(0, 0, 0)):
    bpy.ops.mesh.primitive_cylinder_add(vertices=vertices, radius=radius, depth=depth, location=loc, rotation=rot)
    return finish(bpy.context.active_object, mat, bevel, smooth=True)


def torus(loc, major, minor, mat):
    bpy.ops.mesh.primitive_torus_add(major_segments=96, minor_segments=20, major_radius=major, minor_radius=minor, location=loc)
    return finish(bpy.context.active_object, mat, 0, smooth=True)


def bolts(positions, z, radius, mat):
    for x, y in positions:
        cyl((x, y, z), radius, .02, mat, bevel=.003, vertices=6)


def setup(ortho):
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.device = "CPU"
    scene.cycles.samples = SAMPLES
    scene.cycles.use_denoising = True
    scene.render.resolution_x = SCENE_SIZE[0]
    scene.render.resolution_y = SCENE_SIZE[1]
    scene.render.film_transparent = True
    scene.render.image_settings.file_format = "PNG"
    scene.render.image_settings.color_mode = "RGBA"
    # Standard keeps the neon glows saturated; photographic tone mapping turned them grey.
    scene.view_settings.view_transform = "Standard"
    scene.view_settings.look = "None"

    camera = bpy.data.cameras.new("Camera")
    camera.type = "ORTHO"
    camera.ortho_scale = ortho
    cam = bpy.data.objects.new("Camera", camera)
    cam.location = (0, 0, 5)
    scene.collection.objects.link(cam)
    scene.camera = cam

    world = bpy.data.worlds.new("World")
    world.use_nodes = True
    world.node_tree.nodes["Background"].inputs[0].default_value = (.62, .72, .82, 1)
    world.node_tree.nodes["Background"].inputs[1].default_value = .62
    scene.world = world

    # Key light from the upper left, the same on every sprite so that all pieces match.
    key = bpy.data.lights.new("Key", "SUN")
    key.energy = 2.5
    key.angle = math.radians(34)
    key.color = (1, .96, .9)
    key_obj = bpy.data.objects.new("Key", key)
    key_obj.rotation_euler = (math.radians(30), math.radians(-22), 0)
    scene.collection.objects.link(key_obj)

    # Cool fill light from the lower right, without shadows, so dark sides keep their detail.
    fill = bpy.data.lights.new("Fill", "SUN")
    fill.energy = .9
    fill.angle = math.radians(40)
    fill.color = (.78, .88, 1)
    fill.use_shadow = False
    fill_obj = bpy.data.objects.new("Fill", fill)
    fill_obj.rotation_euler = (math.radians(-38), math.radians(30), 0)
    scene.collection.objects.link(fill_obj)

    # Invisible floor that only receives the shadow, kept in the sprite's alpha channel.
    bpy.ops.mesh.primitive_plane_add(size=8, location=(0, 0, 0))
    bpy.context.active_object.is_shadow_catcher = True


SCENE_SIZE = (SIZE, SIZE)


def render(name):
    bpy.context.scene.render.filepath = os.path.join(OUT, name + ".png")
    bpy.ops.render.render(write_still=True)


def octagon(loc, rx, ry, height, mat, bevel=.02):
    """Hull with cut corners, which gives a more recognizable silhouette than a box."""
    bpy.ops.mesh.primitive_cylinder_add(vertices=8, radius=1, depth=height, location=loc, rotation=(0, 0, math.radians(22.5)))
    obj = bpy.context.active_object
    bpy.ops.object.transform_apply(rotation=True, location=False, scale=False)
    obj.scale = (rx, ry, 1)
    bpy.ops.object.transform_apply(scale=True, location=False, rotation=False)
    return finish(obj, mat, bevel)


def blade_mesh(angle, z, mat):
    """Wide trapezoid blade, bright and legible at game size."""
    verts, faces = [], []
    inner, outer, a, b = .045, .225, .03, .07
    for h in (-.008, .008):
        verts += [(inner, -a, h), (outer, -b, h), (outer, b, h), (inner, a, h)]
    faces = [(0, 1, 2, 3), (7, 6, 5, 4), (0, 4, 5, 1), (1, 5, 6, 2), (2, 6, 7, 3), (3, 7, 4, 0)]
    mesh = bpy.data.meshes.new("Blade")
    mesh.from_pydata(verts, [], faces)
    obj = bpy.data.objects.new("Blade", mesh)
    bpy.context.scene.collection.objects.link(obj)
    obj.location = (0, 0, z)
    obj.rotation_euler = (math.radians(10), 0, angle)
    finish(obj, mat, bevel=.005)


def build_generator(blade_angle):
    setup(1.0)
    shell = material("Shell", "#6d828d", .85, .4, .3)
    dark = material("Dark", "#26323a", .8, .5, .35)
    brass = material("Brass", "#f0a84f", 1, .3, .3)
    blade = material("Blade", "#ffb35a", .55, .34, .25)
    steel = material("Steel", "#c7d2d7", 1, .26, .2)
    glow = material("Glow", "#ffb765", 0, .4, 0, emission="#ff9d45", strength=2.2)
    handle = material("Handle", "#e7c592", .9, .34, .3)

    # Same octagonal footprint as the reactor, so both read at the same size on the board.
    prism(regular(8, .49, math.pi / 8), .05, .1, shell, .02)
    prism(regular(8, .42, math.pi / 8), .105, .014, dark, .004)
    for side in (-1, 1):  # Battery packs on the flanks.
        box((side * .37, 0, .12), (.07, .3, .05), shell, bevel=.014)
        for i in range(4):
            box((side * .372, -.105 + i * .07, .15), (.04, .04, .012), dark, bevel=.004)
    torus((0, 0, .13), .315, .034, steel)
    torus((0, 0, .145), .262, .015, brass)
    torus((0, 0, .118), .226, .007, glow)
    global LAYER
    LAYER = "fan"
    for i in range(6):
        blade_mesh(math.radians(blade_angle + i * 60), .17, blade)
    cyl((0, 0, .17), .056, .04, brass, bevel=.008)
    cyl((0, 0, .195), .026, .012, steel, bevel=.003)
    LAYER = "base"
    for i in range(6):
        a = i * math.pi / 3 + .5
        cyl((math.cos(a) * .29, math.sin(a) * .29, .155), .012, .012, steel, bevel=.002, vertices=6)
    box((0, -.4, .11), (.2, .05, .04), handle, bevel=.01)  # Power connector at the bottom.
    for x in (-.055, 0, .055):
        cyl((x, -.41, .134), .009, .012, glow, bevel=.002, vertices=16)


def build_tank():
    setup(1.5)  # Enemies use 1.5 cells per sprite, like the current ones.
    hull = material("Hull", "#4b6778", .7, .44, .3)
    plate = material("Plate", "#6b8797", .75, .4, .3)
    tread = material("Tread", "#4a5358", .35, .7, .3)
    link = material("Link", "#6b757a", .6, .55, .3)
    trim = material("Trim", "#ffb88a", .75, .34, .3)
    dark = material("Dark", "#25313a", .8, .5, .3)
    barrel = material("Barrel", "#c5d2d8", .95, .3, .3)
    glow = material("Glow", "#ff8a57", 0, .4, 0, emission="#ff6a32", strength=2)
    bolt = material("Bolt", "#d7dfe3", 1, .3, .15)
    H = .62  # Low height keeps the shadow from spilling over neighboring cells.

    for side in (-1, 1):
        box((side * .4, 0, .05), (.17, .96, .1), tread, bevel=.024)
        for i in range(17):
            box((side * .4, -.44 + (i + PHASE) * .055, .103), (.16, .03, .014), link, bevel=.004)

    octagon((0, 0, .09), .37, .5, .13, hull, bevel=.03)
    octagon((0, -.01, .165), .29, .42, .03, plate, bevel=.012)
    for side in (-1, 1):
        box((side * .325, 0, .16), (.024, .6, .02), trim, bevel=.006)
    for i in range(4):
        box((0, .24 + i * .042, .187), (.26, .02, .012), dark, bevel=.003)
    bolts([(x, y) for x in (-.2, .2) for y in (-.33, .34)], .184, .014, bolt)
    for x in (-.15, .15):
        cyl((x, -.452, .12), .024, .016, glow, bevel=.002, vertices=24, rot=(math.radians(90), 0, 0))

    # Turret: turns toward the module it is about to shell.
    global LAYER
    LAYER = "turret"
    cyl((0, 0, .21), .215, .06, plate, bevel=.016)
    torus((0, 0, .243), .205, .014, trim)
    cyl((0, 0, .26), .14, .035, dark, bevel=.01)
    cyl((0, 0, .28), .085, .018, hull, bevel=.006)
    cyl((0, 0, .293), .042, .012, glow, bevel=.004, vertices=32)
    bolts([(math.cos(a) * .178, math.sin(a) * .178) for a in [i * math.pi / 4 for i in range(8)]], .243, .01, bolt)
    cyl((0, -.36, .23), .044, .5, barrel, bevel=.005, rot=(math.radians(90), 0, 0))
    cyl((0, -.2, .23), .066, .1, plate, bevel=.01, rot=(math.radians(90), 0, 0))
    cyl((0, -.6, .23), .064, .08, trim, bevel=.012, rot=(math.radians(90), 0, 0))
    LAYER = "base"


def prism(points, z, height, mat, bevel=.012):
    """Extrudes a polygon, given counterclockwise, into a flat solid."""
    area = sum(points[i][0] * points[(i + 1) % len(points)][1] - points[(i + 1) % len(points)][0] * points[i][1] for i in range(len(points)))
    if area < 0:  # Faces must wind counterclockwise or the solid renders inside out.
        points = list(reversed(points))
    n = len(points)
    verts = [(x, y, z - height / 2) for x, y in points] + [(x, y, z + height / 2) for x, y in points]
    faces = [tuple(range(n - 1, -1, -1)), tuple(range(n, 2 * n))]
    faces += [(i, (i + 1) % n, n + (i + 1) % n, n + i) for i in range(n)]
    mesh = bpy.data.meshes.new("Prism")
    mesh.from_pydata(verts, [], faces)
    obj = bpy.data.objects.new("Prism", mesh)
    bpy.context.scene.collection.objects.link(obj)
    return finish(obj, mat, bevel)


def ball(loc, radius, mat):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=32, ring_count=16, radius=radius, location=loc)
    return finish(bpy.context.active_object, mat, 0, smooth=True)


def strut(a, b, z, width, mat):
    """Flat bar between two points of the plan view."""
    dx, dy = b[0] - a[0], b[1] - a[1]
    length = math.hypot(dx, dy)
    return box(((a[0] + b[0]) / 2, (a[1] + b[1]) / 2, z), (length, width, width * .7), mat, bevel=width * .2, rot=math.atan2(dy, dx))


def along_y(loc, radius, length, mat, bevel=.008, vertices=48):
    return cyl(loc, radius, length, mat, bevel=bevel, vertices=vertices, rot=(math.radians(90), 0, 0))


def regular(n, radius, start=0, cx=0, cy=0, sx=1, sy=1):
    return [(cx + math.cos(start + i * math.tau / n) * radius * sx, cy + math.sin(start + i * math.tau / n) * radius * sy) for i in range(n)]


def glowing(name, color, strength=2.2):
    return material(name, "#0a0a0a", 0, .4, 0, emission=color, strength=min(1.05, strength * .45))  # Above 1 the color clips to white.


def transparent(name, color, alpha):
    mat = material(name, color, 0, .25, 0)
    next(n for n in mat.node_tree.nodes if n.type == "BSDF_PRINCIPLED").inputs["Alpha"].default_value = alpha
    return mat


def legs(points, z, mat, tip, width=.03, tip_radius=.03):
    for a, b in points:
        strut(a, b, z, width, mat)
        ball((b[0], b[1], z), tip_radius, tip)


# ---- Modules: one cell per sprite; weapons aim up, toward the enemies ------------------

# ---- Enemies: 1.5 cells per sprite; they point down, the direction they advance --------

def enemy_palette(body, trim):
    return {
        "body": material("Body", body, .7, .42, .3),
        "trim": material("Trim", trim, .8, .34, .25),
        "dark": material("Dark", "#232a31", .8, .5, .3),
        "steel": material("Steel", "#d4dce0", 1, .28, .15),
    }


def build_bomber():
    setup(1.5)
    p = enemy_palette("#667683", "#b98a55")
    glow = glowing("Glow", "#ffa45c", 2.8)
    glass = material("Glass", "#25313b", .2, .1, 0)
    H = .34
    prism([(0, -.68), (.13, -.3), (.7, .22), (.68, .34), (.2, .26), (.13, .54), (-.13, .54), (-.2, .26), (-.68, .34), (-.7, .22), (-.13, -.3)], H, .07, p["body"], .018)
    prism([(0, -.6), (.1, -.25), (.1, .46), (-.1, .46), (-.1, -.25)], H + .06, .07, p["trim"], .018)
    prism([(0, -.5), (.055, -.28), (-.055, -.28)], H + .105, .02, glass, .006)
    for side in (-1, 1):
        along_y((side * .3, .12, H - .01), .058, .3, p["dark"], bevel=.012)
        along_y((side * .3, .29, H - .01), .042, .03, glow, bevel=.005)
        points = [(side * .265, .3), (side * .335, .3), (side * .3, .3 + .2 * flick())]
        prism(points if side > 0 else list(reversed(points)), H - .02, .01, glow, .002)
        box((side * .55, .22, H + .04), (.2, .02, .012), p["trim"], bevel=.004, rot=side * -.75)
    for side in (-1, 1):
        prism([(side * .1, .3), (side * .24, .56), (side * .14, .56), (side * .08, .36)] if side < 0 else [(side * .08, .36), (side * .14, .56), (side * .24, .56), (side * .1, .3)], H + .09, .03, p["steel"], .006)
    bolts([(x, -.05) for x in (-.05, .05)], H + .1, .012, p["steel"])


def arc(cx, cy, r_in, r_out, a0, a1, z, height, mat, bevel=.008, n=20):
    """Curved plate between two radii. Angles are in degrees; 270 is the bottom of the screen."""
    outer = [(cx + math.cos(math.radians(a0 + (a1 - a0) * i / n)) * r_out, cy + math.sin(math.radians(a0 + (a1 - a0) * i / n)) * r_out) for i in range(n + 1)]
    inner = [(cx + math.cos(math.radians(a1 - (a1 - a0) * i / n)) * r_in, cy + math.sin(math.radians(a1 - (a1 - a0) * i / n)) * r_in) for i in range(n + 1)]
    return prism(outer + inner, z, height, mat, bevel)


def chevron(cx, cy, angle, size, thick, z, height, mat):
    """Arrowhead pointing along the angle (degrees)."""
    local = [(size, 0), (0, size * .85), (-thick, size * .85), (size - thick, 0), (-thick, -size * .85), (0, -size * .85)]
    c, s = math.cos(math.radians(angle)), math.sin(math.radians(angle))
    return prism([(cx + x * c - y * s, cy + x * s + y * c) for x, y in local], z, height, mat, .003)


def hazard(cx, cy, width, depth, z, mat_a, mat_b, blocks=6):
    for i in range(blocks):
        x = cx - width / 2 + (i + .5) * width / blocks
        box((x, cy, z), (width / blocks * .94, depth, .014), mat_a if i % 2 else mat_b, bevel=.002)


def leg(hip, knee, foot, z, width, upper, lower, joint, tip):
    step = math.sin(PHASE * math.tau + (hip[0] > 0) * math.pi + (hip[1] > 0) * math.pi) * .055
    knee = (knee[0], knee[1] + step * .5)
    foot = (foot[0], foot[1] + step)
    strut(hip, knee, z + .03, width, upper)
    strut(knee, foot, z, width * .8, lower)
    ball((knee[0], knee[1], z + .03), width * .85, joint)
    ball((foot[0], foot[1], z), width * .7, tip)


def flick():
    """Flame and heat intensity for the current frame."""
    return (1, .72, .94, .66)[int(PHASE * 4) % 4]


def glowing_hot(name, color):
    return material(name, "#0a0a0a", 0, .4, 0, emission=color, strength=1.15)


# ---- Redesigned modules ----------------------------------------------------------------

def build_reactor():
    """A contained reactor with power conduits running out of it."""
    setup(1.0)
    hull = material("Hull", "#5d727c", .9, .38, .3)
    dark = material("Dark", "#172228", .8, .5, .3)
    steel = material("Steel", "#c7d2d7", 1, .26, .2)
    brass = material("Brass", "#e8c170", 1, .3, .25)
    conduit = glowing("Conduit", "#55f0a8", 2.4)
    deep = glowing("Deep", "#16a36a", 2.4)
    mid = glowing("Mid", "#46f0a4", 2.4)
    hot = glowing_hot("Hot", "#d8ffec")
    prism(regular(8, .49, math.pi / 8), .05, .1, hull, .02)
    prism(regular(8, .42, math.pi / 8), .105, .014, dark, .004)
    for a, b in ((1, 1), (-1, 1), (1, -1), (-1, -1)):
        box((a * .19, b * .19, .118), (.3, .03, .012), conduit, bevel=.003, rot=math.atan2(b, a))
        cyl((a * .325, b * .325, .13), .062, .05, steel, bevel=.012)
        cyl((a * .325, b * .325, .158), .036, .012, conduit, bevel=.004, vertices=24)
        torus((a * .325, b * .325, .15), .058, .007, brass)
    for side in (-1, 1):
        for i in range(3):
            box((side * .425, -.08 + i * .08, .07), (.05, .05, .07), steel, bevel=.008)
            box((-.08 + i * .08, side * .425, .07), (.05, .05, .07), steel, bevel=.008)
    cyl((0, 0, .15), .29, .08, hull, bevel=.016)
    torus((0, 0, .19), .275, .024, brass)
    cyl((0, 0, .17), .235, .05, deep, bevel=.008)
    cyl((0, 0, .2), .165, .02, mid, bevel=.006)
    cyl((0, 0, .215), .08, .016, hot, bevel=.006)
    for a in (90, 210, 330):
        arc(0, 0, .15, .3, a - 16, a + 16, .225, .04, steel, .008, n=8)
    for i in range(3):
        a = math.radians(90 + i * 120)
        cyl((math.cos(a) * .26, math.sin(a) * .26, .25), .018, .014, dark, bevel=.003, vertices=12)


def build_pulse():
    """An artillery emplacement: footed base, toothed traverse ring, capacitors, shrouded barrel."""
    setup(1.0)
    base = material("Base", "#5f7480", .9, .38, .3)
    gold = material("Gold", "#f2c977", 1, .3, .25)
    dark = material("Dark", "#1d272e", .8, .5, .3)
    steel = material("Steel", "#c7d2d7", 1, .26, .2)
    glow = glowing("Glow", "#ffd46e", 2.6)
    cy = 0
    for a, b in ((1, 1), (-1, 1), (1, -1), (-1, -1)):
        strut((a * .2, cy + b * .2), (a * .36, cy + b * .36), .05, .08, base)
        box((a * .37, cy + b * .37, .03), (.14, .14, .05), dark, bevel=.014)
        cyl((a * .37, cy + b * .37, .062), .024, .015, steel, bevel=.003, vertices=6)
    prism(regular(8, .36, math.pi / 8, cy=cy), .06, .1, base, .02)
    for side in (-1, 1):
        for i in range(3):
            box((side * .28, cy - .09 + i * .09, .12), (.075, .065, .03), glow if i == 1 else dark, bevel=.006)
    for i in range(24):
        a = i * math.tau / 24
        box((math.cos(a) * .225, cy + math.sin(a) * .225, .125), (.032, .02, .024), gold, bevel=.003, rot=a)
    cyl((0, cy, .13), .205, .05, dark, bevel=.01)
    torus((0, cy, .155), .185, .012, steel)
    cyl((0, cy, .175), .15, .05, base, bevel=.016)
    global LAYER
    LAYER = "barrel"
    box((0, -.02, .225), (.2, .24, .05), base, bevel=.02)
    box((0, .2, .2), (.11, .22, .06), dark, bevel=.012)
    for i in range(3):  # Cooling shroud.
        along_y((0, .17 + i * .062, .2), .066, .036, steel, bevel=.006)
    along_y((0, .38, .2), .045, .16, gold, bevel=.005)
    along_y((0, .45, .2), .068, .05, steel, bevel=.012)
    cyl((0, -.02, .26), .03, .012, glow, bevel=.004)
    LAYER = "base"


# ---- Redesigned enemies ----------------------------------------------------------------

def build_scout():
    """Recon walker: its main feature is the radar dish."""
    setup(1.5)
    body = material("Body", "#9a7863", .7, .42, .3)
    trim = material("Trim", "#ffb793", .8, .34, .25)
    dark = material("Dark", "#232a31", .8, .5, .3)
    steel = material("Steel", "#d4dce0", 1, .28, .15)
    glow = glowing("Glow", "#7fe7ff", 2.6)
    for side in (-1, 1):
        for hy, ky, fy in ((-.12, -.22, -.36), (.14, .24, .38)):
            leg((side * .13, hy), (side * .28, ky), (side * .42, fy), .05, .038, trim, steel, dark, steel)
    prism(regular(8, .23, math.pi / 8, sy=1.3), .08, .1, body, .02)
    box((0, -.2, .135), (.1, .1, .03), dark, bevel=.01)
    cyl((0, -.27, .13), .05, .03, glow, bevel=.008, rot=(math.radians(90), 0, 0))
    cyl((0, .06, .17), .205, .025, steel, bevel=.008)
    cyl((0, .06, .185), .17, .012, dark, bevel=.003)
    torus((0, .06, .195), .1, .006, glow)
    torus((0, .06, .195), .16, .006, glow)
    arc(0, .06, .04, .17, 245 + PHASE * 360, 290 + PHASE * 360, .205, .012, glow, .002, n=6)
    cyl((0, .06, .21), .024, .03, trim, bevel=.005, vertices=16)


def build_runner():
    """Light attack craft: long and narrow, with exposed engines and flames."""
    setup(1.5)
    body = material("Body", "#c97e4f", .75, .36, .3)
    trim = material("Trim", "#ffd2a0", .8, .3, .2)
    dark = material("Dark", "#232a31", .8, .5, .3)
    flame = glowing("Flame", "#ff8a3c", 2.4)
    hot = glowing_hot("Hot", "#ffe2a8")
    prism([(0, -.56), (.13, -.1), (.13, .28), (-.13, .28), (-.13, -.1)], .08, .1, body, .018)
    prism([(0, -.42), (.06, -.12), (.06, .2), (-.06, .2), (-.06, -.12)], .14, .03, dark, .006)
    prism([(0, -.36), (.03, -.18), (-.03, -.18)], .16, .012, hot, .002)
    for side in (-1, 1):
        points = [(side * .1, -.06), (side * .42, .24), (side * .36, .3), (side * .1, .18)]
        prism(points if side < 0 else list(reversed(points)), .07, .05, trim, .008)
        along_y((side * .19, .23, .11), .062, .26, dark, bevel=.012)
        along_y((side * .19, .37, .11), .05, .026, flame, bevel=.004)
        points = [(side * .15, .39), (side * .23, .39), (side * .19, .39 + .23 * flick())]
        prism(points if side > 0 else list(reversed(points)), .1, .01, flame, .002)
        points = [(side * .17, .39), (side * .21, .39), (side * .19, .39 + .11 * flick())]
        prism(points if side > 0 else list(reversed(points)), .112, .008, hot, .001)


def build_armor():
    """Heavy crawler: layered slabs and a wedge-shaped ram up front."""
    setup(1.5)
    body = material("Body", "#7a6a5c", .75, .46, .35)
    plate = material("Plate", "#a08a74", .8, .42, .35)
    trim = material("Trim", "#ffb77e", .85, .34, .25)
    dark = material("Dark", "#20262b", .8, .55, .3)
    tread = material("Tread", "#4a5358", .35, .7, .3)
    steel = material("Steel", "#d4dce0", 1, .3, .15)
    glow = glowing("Glow", "#ff8f4d", 2.6)
    for side in (-1, 1):
        box((side * .49, .05, .05), (.2, .86, .1), tread, bevel=.024)
        for i in range(14):
            box((side * .49, -.32 + (i + PHASE) * .057, .103), (.19, .03, .014), steel if i % 7 == 0 else dark, bevel=.004)
    prism([(-.42, -.3), (0, -.74), (.42, -.3), (.42, -.16), (-.42, -.16)], .1, .15, trim, .018)
    prism([(-.3, -.3), (0, -.6), (.3, -.3), (.3, -.22), (-.3, -.22)], .18, .03, dark, .006)
    for x in (-.22, 0, .22):
        y = -.41 if x else -.56
        prism([(x - .06, y + .1), (x, y - .12), (x + .06, y + .1)], .15, .08, steel, .004)
    box((0, .12, .1), (.72, .72, .17), body, bevel=.04)
    for i in range(4):  # Overlapping slabs, like armor scales.
        box((0, -.13 + i * .17, .2 + i * .012), (.66 - i * .04, .2, .05), plate, bevel=.018)
        box((0, -.13 + i * .17 + .085, .228 + i * .012), (.5 - i * .04, .018, .012), dark, bevel=.002)
    box((0, -.16, .25), (.24, .05, .02), glow, bevel=.005)
    bolts([(x, y) for x in (-.29, .29) for y in (-.13, .04, .21, .38)], .24, .016, steel)


def build_jammer():
    """Electronic warfare: a mast array radiating arcs of signal in every direction."""
    setup(1.5)
    body = material("Body", "#594668", .75, .4, .3)
    trim = material("Trim", "#e394ee", .85, .34, .2)
    dark = material("Dark", "#1d1a24", .8, .5, .3)
    steel = material("Steel", "#d4dce0", 1, .28, .15)
    glow = glowing("Glow", "#ff7df2", 2.6)
    soft = transparent("Soft", "#ff8df5", .55)
    for a in (45, 135, 225, 315):
        arc(0, 0, .4 + PHASE * .12, .43 + PHASE * .12, a - 24, a + 24, .06, .012, glow, .002, n=12)
        arc(0, 0, .55 + PHASE * .12, .575 + PHASE * .12, a - 18, a + 18, .05, .01, soft, .002, n=12)
    for x, y in ((-.27, -.27), (.27, -.27), (-.27, .27), (.27, .27)):
        strut((0, 0), (x * .85, y * .85), .07, .05, dark)
        cyl((x, y, .1), .05, .1, body, bevel=.012)
        for z, r in ((.16, .05), (.185, .036), (.205, .022)):
            cyl((x, y, z), r, .014, steel, bevel=.003, vertices=24)
        ball((x, y, .225), .02, glow)
    prism(regular(8, .24, math.pi / 8), .09, .12, body, .022)
    cyl((0, 0, .17), .17, .04, dark, bevel=.01)
    torus((0, 0, .195), .15, .012, trim)
    torus((0, 0, .2), .095, .009, glow)
    cyl((0, 0, .2), .042, .03, glow, bevel=.006)


def build_splitter():
    """Two runners docked side by side in a carrier: it visibly comes apart when destroyed."""
    setup(1.5)
    body = material("Body", "#c97e4f", .75, .36, .3)
    frame = material("Frame", "#725a58", .8, .42, .3)
    trim = material("Trim", "#ffd2a0", .8, .3, .2)
    dark = material("Dark", "#232a31", .8, .5, .3)
    steel = material("Steel", "#d4dce0", 1, .28, .15)
    glow = glowing("Glow", "#ff6f5e", 2.6)
    flame = glowing("Flame", "#ff8a3c", 2.4)
    for side in (-1, 1):
        points = [(side * .25, -.5), (side * .39, -.14), (side * .39, .3), (side * .11, .3), (side * .11, -.14)]
        prism(points if side > 0 else list(reversed(points)), .09, .1, body, .016)
        points = [(side * .25, -.36), (side * .31, -.12), (side * .31, .2), (side * .19, .2), (side * .19, -.12)]
        prism(points if side > 0 else list(reversed(points)), .15, .025, dark, .005)
        along_y((side * .25, .3, .1), .06, .1, dark, bevel=.01)
        along_y((side * .25, .36, .1), .046, .022, flame, bevel=.004)
        points = [(side * .21, .37), (side * .29, .37), (side * .25, .37 + .2 * flick())]
        prism(points if side > 0 else list(reversed(points)), .1, .01, flame, .002)
        points = [(side * .39, -.02), (side * .52, .22), (side * .39, .2)]
        prism(points if side > 0 else list(reversed(points)), .08, .04, trim, .006)
    box((0, -.04, .05), (.12, .78, .05), glow, bevel=.008)  # The seam where it splits.
    for y in (-.32, .02, .28):  # Docking clamps.
        box((0, y, .16), (.36, .07, .05), frame, bevel=.016)
        cyl((0, y, .195), .03, .016, steel, bevel=.004, vertices=16)
        chevron(.12, y, 0, .035, .018, .19, .008, glow)
        chevron(-.12, y, 180, .035, .018, .19, .008, glow)


def build_sapper():
    """Saboteur: pincers that siphon energy up front, an explosive charge on its back."""
    setup(1.5)
    body = material("Body", "#7c5230", .75, .42, .3)
    trim = material("Trim", "#ffa35c", .85, .34, .25)
    dark = material("Dark", "#20252a", .8, .5, .3)
    steel = material("Steel", "#d4dce0", 1, .28, .15)
    yellow = material("Yellow", "#ffc61a", .1, .45, .2)
    black = material("Black", "#17191b", .1, .55, .1)
    glow = glowing("Glow", "#ff7a2e", 2.6)
    hot = glowing_hot("Hot", "#ffe0a8")
    for side in (-1, 1):
        leg((side * .24, .02), (side * .38, -.02), (side * .5, -.14), .05, .032, trim, steel, dark, steel)
        leg((side * .24, .24), (side * .38, .3), (side * .5, .42), .05, .032, trim, steel, dark, steel)
        points = [(side * .08, -.18), (side * .23, -.2), (side * .23, -.45), (side * .06, -.66), (side * .03, -.63), (side * .13, -.42)]
        prism(points if side < 0 else list(reversed(points)), .1, .07, steel, .008)
    for i in range(4):  # Arc between the pincers.
        x, y = (-.04 + i * .027), -.58 + math.sin(PHASE * math.tau * 2 + i * 2) * .025
        ball((x, y, .1), .017, hot)
    prism([(-.26, -.2), (.26, -.2), (.3, .36), (-.3, .36)], .09, .12, body, .022)
    box((0, -.13, .165), (.3, .1, .03), dark, bevel=.008)
    box((0, -.13, .182), (.2, .03, .008), glow, bevel=.002)
    cyl((0, .12, .19), .2, .08, black, bevel=.02)
    hazard(0, .12, .34, .1, .232, yellow, black, blocks=6)
    arc(0, .12, .17, .2, 0, 360, .24, .012, yellow, .002, n=36)
    cyl((0, .12, .248), .045, .018, glow, bevel=.005)
    for x in (-.11, .11):
        strut((x, .32), (x * 1.2, .36), .17, .018, trim)


def build_bulwark():
    """Armored infantry: a huge curved shield up front, the small walker hidden behind it."""
    setup(1.5)
    body = material("Body", "#62584c", .75, .44, .3)
    shield = material("Shield", "#d9a269", .85, .36, .35)
    inner = material("Inner", "#8c6d4a", .85, .42, .35)
    dark = material("Dark", "#20252a", .8, .5, .3)
    steel = material("Steel", "#d4dce0", 1, .28, .15)
    glow = glowing("Glow", "#ffbf6e", 2.6)
    for side in (-1, 1):
        leg((side * .14, .22), (side * .28, .32), (side * .38, .47), .05, .03, inner, steel, dark, steel)
    prism(regular(8, .23, math.pi / 8, cy=.24, sy=1.15), .1, .12, body, .02)
    cyl((0, .24, .17), .1, .04, dark, bevel=.01)
    cyl((0, .24, .19), .05, .012, glow, bevel=.004)
    for side in (-1, 1):
        strut((side * .15, .12), (side * .28, -.12), .14, .05, dark)
    arc(0, .36, .58, .86, 212, 328, .16, .2, shield, .02, n=28)
    arc(0, .36, .63, .81, 220, 320, .275, .025, inner, .006, n=24)
    arc(0, .36, .7, .74, 226, 314, .295, .012, dark, .002, n=24)
    for a in (222, 238, 254, 270, 286, 302, 318):
        r = math.radians(a)
        cyl((math.cos(r) * .82, .36 + math.sin(r) * .82, .265), .018, .014, steel, bevel=.003, vertices=8)
    for a in (246, 270, 294):
        r = math.radians(a)
        chevron(math.cos(r) * .65, .36 + math.sin(r) * .65, a, .045, .02, .3, .012, shield)


def build_thermal():
    """Heat: radial radiator fins around a molten core, glowing red-hot from the inside."""
    setup(1.5)
    fin = material("Fin", "#61473f", .85, .46, .4)
    tip = material("Tip", "#c47050", .85, .4, .35)
    dark = material("Dark", "#1d1a1a", .75, .55, .3)
    steel = material("Steel", "#c3cdd1", 1, .32, .2)
    ember = glowing("Ember", "#ff3e1f", 2.4 * flick())
    fire = glowing("Fire", "#ff8a2a", 2.4 * flick())
    hot = glowing_hot("Hot", "#ffe9a8")
    cyl((0, 0, .05), .44, .05, ember, bevel=.006)
    for i in range(16):
        a = i * math.tau / 16
        box((math.cos(a) * .33, math.sin(a) * .33, .1), (.26, .042, .11), fin, bevel=.008, rot=a)
        box((math.cos(a) * .46, math.sin(a) * .46, .135), (.06, .05, .03), tip, bevel=.006, rot=a)
    torus((0, 0, .15), .22, .022, steel)
    cyl((0, 0, .13), .205, .06, dark, bevel=.01)
    cyl((0, 0, .17), .165, .018, ember, bevel=.005)
    cyl((0, 0, .185), .115, .014, fire, bevel=.004)
    cyl((0, 0, .198), .06, .012, hot, bevel=.004)
    for a in (45, 165, 285):  # Exhaust stacks.
        r = math.radians(a)
        cyl((math.cos(r) * .255, math.sin(r) * .255, .19), .036, .08, steel, bevel=.006, vertices=20)
        cyl((math.cos(r) * .255, math.sin(r) * .255, .232), .022, .006, fire, bevel=.002, vertices=20)


def build_grounder():
    """Electrical insulation: ribbed ceramic insulators and rods driven into the ground."""
    setup(1.5)
    body = material("Body", "#3f6456", .7, .44, .3)
    rubber = material("Rubber", "#1b2422", .05, .78, .2)
    ceramic = material("Ceramic", "#e8f5ec", 0, .18, 0)
    copper = material("Copper", "#e8905a", 1, .28, .2)
    dark = material("Dark", "#1d2624", .8, .5, .3)
    glow = glowing("Glow", "#6dffb8", 2.6)
    for a, b in ((-1, -1), (1, -1), (-1, 1), (1, 1)):
        foot = (a * .47, b * .47)
        strut((a * .2, b * .2), foot, .05, .036, copper)
        cyl((foot[0], foot[1], .05), .07, .012, dark, bevel=.003)   # Ground plate.
        cyl((foot[0], foot[1], .08), .028, .06, copper, bevel=.004, vertices=6)
        torus((foot[0], foot[1], .03), .085 + PHASE * .05, .005, glow)
    prism(regular(8, .36, math.pi / 8), .08, .1, rubber, .025)
    prism(regular(8, .3, math.pi / 8), .135, .012, body, .004)
    for x, y in ((-.15, -.15), (.15, -.15), (-.15, .15), (.15, .15)):  # Stacked insulators.
        for z, r in ((.16, .092), (.19, .076), (.22, .06), (.25, .044)):
            cyl((x, y, z), r, .018, ceramic, bevel=.006)
        cyl((x, y, .268), .018, .014, copper, bevel=.003, vertices=12)
    cyl((0, 0, .17), .05, .05, dark, bevel=.008)
    cyl((0, 0, .198), .028, .01, glow, bevel=.003)


def build_hunter():
    """Sniper spider: long jointed legs, a long rifle with a scope and a red laser sight."""
    setup(1.5)
    body = material("Body", "#3c4a53", .8, .4, .3)
    trim = material("Trim", "#b5593f", .85, .36, .25)
    dark = material("Dark", "#181d21", .8, .5, .3)
    steel = material("Steel", "#c3cdd1", 1, .28, .15)
    red = glowing("Red", "#ff2d24", 2.8)
    hot = glowing_hot("Hot", "#ffc2a8")
    for side in (-1, 1):
        for hy, ky, fy, fx in ((-.08, -.28, -.46, .58), (.0, -.02, -.1, .68), (.08, .22, .24, .66), (.14, .4, .6, .5)):
            leg((side * .13, hy), (side * .36, ky), (side * fx, fy), .05, .036, body, trim, steel, dark)
    prism(regular(6, .23, 0, sx=.86, sy=1.18), .1, .12, body, .02)
    prism(regular(6, .15, 0, cy=.13), .17, .04, dark, .008)
    cyl((0, .13, .195), .06, .02, trim, bevel=.006)
    global LAYER
    LAYER = "turret"
    cyl((0, 0, .2), .07, .03, dark, bevel=.008)
    along_y((0, -.3, .21), .026, .6, steel, bevel=.004)
    along_y((0, -.58, .21), .04, .07, dark, bevel=.008)
    along_y((.06, -.15, .24), .03, .22, dark, bevel=.006)
    along_y((.06, -.265, .24), .022, .012, hot, bevel=.002)
    cyl((-.06, -.11, .24), .028, .03, red, bevel=.005)
    box((0, -.66, .21), (.008, .12, .008), red, bevel=0)
    LAYER = "base"


def build_commander():
    """Boss: a fortress on six legs, with two cannons, a command dome and a protective aura."""
    setup(1.5)
    hull = material("Hull", "#46525c", .85, .4, .3)
    plate = material("Plate", "#67767f", .85, .38, .3)
    gold = material("Gold", "#f2c46e", 1, .28, .2)
    dark = material("Dark", "#181d21", .8, .5, .3)
    steel = material("Steel", "#d4dce0", 1, .28, .15)
    aura = glowing("Aura", "#ffd27a", 2.4)
    soft = transparent("Soft", "#ffd27a", .5)
    glass = material("Glass", "#68c6e8", .3, .08, 0, emission="#3fa8d0", strength=.5)
    for i in range(6):  # Segmented aura: it protects nearby escorts.
        a = 30 + i * 60 + PHASE * 60
        arc(0, 0, .695, .725, a - 22, a + 22, .03, .012, aura, .002, n=14)
        arc(0, 0, .655, .67, a - 14, a + 14, .03, .01, soft, .002, n=10)
    for a in (30, 90, 150, 210, 270, 330):
        r = math.radians(a)
        strut((math.cos(r) * .36, math.sin(r) * .36), (math.cos(r) * .59, math.sin(r) * .59), .06, .07, plate)
        cyl((math.cos(r) * .6, math.sin(r) * .6, .05), .058, .08, dark, bevel=.012)
        cyl((math.cos(r) * .6, math.sin(r) * .6, .095), .03, .014, gold, bevel=.004)
    prism(regular(6, .5, math.pi / 6), .1, .16, hull, .03)
    prism(regular(6, .42, math.pi / 6), .19, .03, plate, .012)
    prism(regular(6, .3, math.pi / 6, cy=.05), .26, .07, hull, .018)
    global LAYER
    LAYER = "turret"
    for side in (-1, 1):
        along_y((side * .17, -.44, .33), .042, .36, steel, bevel=.006)
        along_y((side * .17, -.62, .33), .058, .06, gold, bevel=.01)
        box((side * .17, -.22, .33), (.12, .16, .08), dark, bevel=.02)
    box((0, -.22, .32), (.3, .07, .05), dark, bevel=.015)
    LAYER = "base"
    for a in range(0, 360, 60):  # Crown of spikes around the dome.
        r = math.radians(a + 30)
        x, y = math.cos(r) * .265, .05 + math.sin(r) * .265
        prism([(x + math.cos(r) * .07, y + math.sin(r) * .07), (x + math.cos(r + 1.7) * .045, y + math.sin(r + 1.7) * .045), (x + math.cos(r - 1.7) * .045, y + math.sin(r - 1.7) * .045)], .31, .03, gold, .004)
    torus((0, .05, .3), .185, .016, gold)
    bpy.ops.mesh.primitive_uv_sphere_add(segments=32, ring_count=16, radius=.165, location=(0, .05, .29))
    dome = bpy.context.active_object
    dome.scale = (1, 1, .55)
    bpy.ops.object.transform_apply(scale=True, location=False, rotation=False)
    finish(dome, glass, 0, smooth=True)
    bolts(regular(6, .4, math.pi / 6), .21, .018, steel)


def build_helicopter():
    """A recognizable gunship from above: nose down, tail boom, stub wings, four thin blades."""
    setup(1.5)
    body = material("Body", "#3f7f99", .75, .4, .3)
    light = material("Light", "#a9dcee", .8, .34, .2)
    dark = material("Dark", "#1c2429", .8, .5, .3)
    steel = material("Steel", "#c3cdd1", 1, .28, .15)
    glass = material("Glass", "#16303c", .3, .06, 0)
    red = glowing("Red", "#ff5a3c", 2.8)
    blade = material("Blade", "#20282d", .6, .45, .1)
    tip = material("Tip", "#ffd27a", .3, .4, .1)
    H = .62  # Flying high: the shadow falls clearly away from the aircraft.
    along_y((0, .42, H), .036, .5, body, bevel=.008)
    prism([(-.11, .66), (.11, .66), (.04, .54), (-.04, .54)], H + .02, .03, light, .005)
    cyl((.05, .66, H + .05), .1, .004, transparent("TailBlur", "#d8f3ff", .3), bevel=0, vertices=32)
    box((.05, .66, H + .06), (.2, .014, .006), blade, bevel=.001)
    prism(regular(20, .5, 0, 0, -.1, .3, .62), H, .16, body, .03)
    prism(regular(20, .5, 0, 0, -.32, .2, .34), H + .09, .04, glass, .012)
    prism(regular(16, .5, 0, 0, .1, .19, .34), H + .09, .03, light, .008)
    for side in (-1, 1):
        box((side * .28, -.03, H), (.34, .11, .03), light, bevel=.012)
        along_y((side * .39, -.05, H - .01), .036, .28, dark, bevel=.008)
        for i in range(2):
            along_y((side * (.34 + i * .1), -.21, H - .01), .012, .03, red, bevel=.002, vertices=12)
        box((side * .17, .2, H + .09), (.09, .16, .03), dark, bevel=.008)
    along_y((0, -.53, H - .03), .026, .12, steel, bevel=.004)
    global LAYER
    LAYER = "rotor"
    for a in (35, 125):
        r = math.radians(a)
        box((0, 0, H + .17), (1.02, .032, .008), blade, bevel=.002, rot=r)
        for sign in (-1, 1):
            box((math.cos(r) * .47 * sign, math.sin(r) * .47 * sign, H + .172), (.08, .034, .008), tip, bevel=.001, rot=r)
    cyl((0, 0, H + .15), .07, .05, dark, bevel=.012)
    cyl((0, 0, H + .19), .032, .014, steel, bevel=.004)


def build_cryo():
    """Frost cannon: frozen rim with ice shards, coolant tank and a flared nozzle."""
    setup(1.0)
    base = material("Base", "#4d6573", .9, .4, .4)
    dark = material("Dark", "#172530", .8, .45, .3)
    steel = material("Steel", "#d0dce1", 1, .26, .2)
    frost = material("Frost", "#a9d9ec", .1, .5, .2)
    ice = material("Ice", "#9fe6ff", .1, .08, 0, emission="#5fd2ff", strength=.35)
    coolant = material("Coolant", "#57c8ff", .2, .1, 0, emission="#38b8ff", strength=.5)
    glow = glowing("Glow", "#a8ecff", 2.6)
    cyl((0, 0, .05), .4, .1, base, bevel=.02)
    torus((0, 0, .1), .385, .022, frost)
    random_angles = (8, 47, 83, 121, 160, 197, 233, 276, 312, 348)
    for i, a in enumerate(random_angles):  # Ice shards growing on the rim.
        r = math.radians(a)
        x, y = math.cos(r) * .37, math.sin(r) * .37
        size = .05 + (i % 3) * .015
        prism([(x + math.cos(r) * size * 1.4, y + math.sin(r) * size * 1.4), (x + math.cos(r + 1.9) * size * .6, y + math.sin(r + 1.9) * size * .6),
               (x + math.cos(r - 1.9) * size * .6, y + math.sin(r - 1.9) * size * .6)], .13, .04, ice, .004)
    for side in (-1, 1):  # Coolant tanks on the flanks of the mount.
        bpy.ops.mesh.primitive_cylinder_add(vertices=48, radius=.06, depth=.34, location=(side * .27, 0, .13), rotation=(math.radians(90), 0, 0))
        finish(bpy.context.active_object, coolant, .02, smooth=True)
        for y in (-.13, .13):
            bpy.ops.mesh.primitive_cylinder_add(vertices=48, radius=.068, depth=.025, location=(side * .27, y, .13), rotation=(math.radians(90), 0, 0))
            finish(bpy.context.active_object, steel, .005, smooth=True)
    cyl((0, 0, .12), .215, .04, steel, bevel=.01)
    global LAYER
    LAYER = "turret"
    cyl((0, 0, .16), .2, .08, dark, bevel=.02)
    torus((0, 0, .2), .19, .014, steel)
    bpy.ops.mesh.primitive_uv_sphere_add(segments=32, ring_count=16, radius=.14, location=(0, 0, .2))
    dome = bpy.context.active_object
    dome.scale = (1, 1, .6)
    bpy.ops.object.transform_apply(scale=True, location=False, rotation=False)
    finish(dome, ice, 0, smooth=True)
    for i in range(3):  # Snowflake on the dome.
        box((0, 0, .27), (.22, .022, .02), glow, bevel=.003, rot=i * math.pi / 3)
    along_y((0, .26, .2), .06, .26, steel, bevel=.01)
    cyl((0, .4, .2), .1, .06, steel, bevel=.014, rot=(math.radians(90), 0, 0))
    cyl((0, .433, .2), .072, .012, glow, bevel=.003, rot=(math.radians(90), 0, 0))
    for side in (-1, 1):
        strut((side * .05, .14), (side * .11, .33), .19, .022, frost)
    LAYER = "base"


# ---- HUD icons, rendered with the same lights and materials as the pieces -------------

def build_icon_energy():
    setup(1.0)
    rim = material("Rim", "#8a6a2a", 1, .3, .2)
    gold = glowing("Gold", "#ffd36a", 2.6)
    bolt = [(.12, .46), (-.26, -.04), (-.03, -.04), (-.13, -.46), (.27, .06), (.04, .06)]
    prism(bolt, .06, .1, rim, .02)
    prism([(x * .78, y * .78) for x, y in bolt], .12, .04, gold, .01)


def build_icon_base():
    setup(1.0)
    rim = material("Rim", "#c96a63", .9, .32, .2)
    plate = material("Plate", "#3b2a2e", .8, .45, .2)
    glow = glowing("Glow", "#ff8f8a", 2.4)
    outline = [(-.38, .4), (.38, .4), (.38, -.02), (0, -.46), (-.38, -.02)]
    prism(outline, .05, .1, rim, .02)
    prism([(x * .8, y * .8 + .01) for x, y in outline], .11, .03, plate, .008)
    torus((0, .04, .14), .15, .028, glow)
    cyl((0, .04, .14), .07, .03, glow, bevel=.008)


def build_icon_wave():
    setup(1.0)
    rim = material("Rim", "#3e6f7a", .9, .32, .2)
    glow = glowing("Glow", "#86e6ee", 2.4)
    for i, y in enumerate((.26, 0, -.26)):
        chevron(0, y, 270, .34, .12, .06, .08, rim)
        chevron(0, y + .02, 270, .26, .06, .11, .03, glow if i < 2 else rim)


def build_repair():
    """Field workshop: an articulated robot arm with a welding head over a repair pad."""
    setup(1.0)
    floor = material("Floor", "#5a6a54", .85, .42, .5)
    yellow = material("Yellow", "#e8b52a", .3, .45, .45)
    black = material("Black", "#15171a", .2, .6, .3)
    dark = material("Dark", "#151e22", .8, .5, .3)
    steel = material("Steel", "#c9d3d7", 1, .28, .2)
    arm = material("Arm", "#e8b24a", .6, .34, .35)
    green = glowing("Green", "#6dff9c", 2.4)
    hot = glowing_hot("Hot", "#eaffef")
    crate = material("Crate", "#2f6a72", .6, .48, .5)
    box((0, 0, .04), (.9, .9, .08), yellow, bevel=.035)                      # Industrial safety frame.
    for side in (-1, 1):
        hazard(0, side * .42, .84, .05, .085, yellow, black, blocks=10)
        for k in range(10):
            box((side * .42, -.38 + k * .085, .085), (.05, .042, .012), black if k % 2 else yellow, bevel=.002)
    box((0, 0, .085), (.76, .76, .014), floor, bevel=.006)
    cyl((.07, .06, .1), .25, .03, steel, bevel=.008)                         # Repair pad.
    cyl((.07, .06, .117), .21, .012, dark, bevel=.003)
    box((.07, .06, .126), (.2, .06, .012), green, bevel=.003)
    box((.07, .06, .126), (.06, .2, .012), green, bevel=.003)
    for x in (-.28, -.12):                                                   # Tool rack.
        box((x, -.33, .1), (.12, .1, .08), crate, bevel=.015)
        box((x, -.33, .145), (.08, .02, .01), steel, bevel=.002)
    box((.3, -.33, .1), (.16, .1, .1), crate, bevel=.015)
    cyl((-.3, .3, .12), .13, .1, dark, bevel=.015)                           # Arm base and joints.
    cyl((-.3, .3, .18), .095, .04, arm, bevel=.01)
    strut((-.3, .3), (-.06, .34), .22, .1, arm)
    cyl((-.06, .34, .24), .075, .06, dark, bevel=.01)
    strut((-.06, .34), (.07, .13), .26, .085, arm)
    cyl((.06, .14, .27), .04, .05, dark, bevel=.008)
    cyl((.06, .12, .3), .022, .03, hot, bevel=.004)                           # Welding tip.
    for i in range(4):
        a = i * math.pi / 2 + PHASE
        ball((.06 + math.cos(a) * .045, .12 + math.sin(a) * .045, .3), .012, green)
    for side in (-1, 1):
        box((side * .4, .1, .1), (.04, .3, .06), green if side > 0 else steel, bevel=.008)


# ---- Button icons --------------------------------------------------------------------

def build_icon_upgrade():
    setup(1.0)
    rim = material("Rim", "#1d5a63", .9, .32, .2)
    glow = glowing("Glow", "#9ef6ff", 2.6)
    for i, y in enumerate((-.2, .08)):
        chevron(0, y, 90, .36, .13, .06, .08, rim)
        chevron(0, y - .01, 90, .28, .07, .11, .03, glow)


def build_icon_recycle():
    setup(1.0)
    rim = material("Rim", "#61483a", .9, .32, .2)
    glow = glowing("Glow", "#ffcf9e", 2.4)
    for a in (90, 210, 330):
        arc(0, 0, .26, .38, a + 18, a + 92, .06, .08, rim, .01, n=14)
        arc(0, 0, .29, .35, a + 22, a + 88, .11, .03, glow, .004, n=14)
        r = math.radians(a + 96)
        chevron(math.cos(r) * .32, math.sin(r) * .32, a + 186, .13, .06, .08, .07, glow)


def build_relay():
    """Pylon on an octagonal platform. Four conduits follow the grid, the same way the links run."""
    setup(1.0)
    deck = material("Deck", "#3f5560", .9, .42, .45)
    frame = material("Frame", "#566e7a", .9, .38, .4)
    dark = material("Dark", "#141d22", .8, .5, .3)
    steel = material("Steel", "#c7d2d7", 1, .28, .25)
    rubber = material("Rubber", "#1c2226", .1, .7, .3)
    ceramic = material("Ceramic", "#e6f2f4", 0, .2, .15)
    line = glowing("Line", "#4fe6f5", 2.4)
    hot = glowing_hot("Hot", "#c8fbff")
    prism(regular(8, .46, math.pi / 8), .04, .08, deck, .02)
    prism(regular(8, .4, math.pi / 8), .085, .012, dark, .004)
    for a in (0, 90, 180, 270):
        r = math.radians(a)
        c, s = math.cos(r), math.sin(r)
        box((c * .27, s * .27, .1), (.32, .1, .06), rubber, bevel=.02, rot=r)
        box((c * .27, s * .27, .134), (.28, .022, .01), line, bevel=.003, rot=r)
        box((c * .2, s * .2, .135), (.035, .12, .05), steel, bevel=.008, rot=r)
        box((c * .41, s * .41, .1), (.1, .16, .1), frame, bevel=.02, rot=r)
        cyl((c * .44, s * .44, .155), .03, .012, hot, bevel=.003, vertices=16)
    for a in (45, 135, 225, 315):  # Diagonal studs, for the diagonal links.
        r = math.radians(a)
        cyl((math.cos(r) * .34, math.sin(r) * .34, .1), .045, .04, steel, bevel=.008)
        cyl((math.cos(r) * .34, math.sin(r) * .34, .123), .022, .01, line, bevel=.003, vertices=16)
    cyl((0, 0, .11), .17, .06, frame, bevel=.014)
    for z, radius in ((.15, .135), (.185, .112), (.22, .09), (.255, .07)):
        cyl((0, 0, z), radius, .028, ceramic, bevel=.01)
    torus((0, 0, .275), .085, .014, steel)
    cyl((0, 0, .29), .05, .025, hot, bevel=.008)


def build_tesla():
    """Four copper-wound coil pillars around a spiked crown electrode, with lightning jumping between them."""
    setup(1.0)
    base = material("Base", "#3a3450", .9, .42, .45)
    dark = material("Dark", "#151320", .8, .5, .3)
    chrome = material("Chrome", "#dfe3ea", 1, .16, .1)
    copper = material("Copper", "#d98a4e", 1, .3, .25)
    glow = glowing("Glow", "#b98cff", 3)
    hot = glowing_hot("Hot", "#f3e6ff")
    prism(regular(8, .47, math.pi / 8), .04, .08, base, .025)
    prism(regular(8, .41, math.pi / 8), .085, .012, dark, .004)
    pillars = [(a * .27, b * .27) for a, b in ((1, 1), (-1, 1), (1, -1), (-1, -1))]
    for x, y in pillars:
        cyl((x, y, .12), .1, .07, chrome, bevel=.012)
        for i in range(5):  # Copper windings stacked up the pillar.
            torus((x, y, .16 + i * .022), .075 - i * .004, .012, copper)
        cyl((x, y, .27), .045, .03, chrome, bevel=.01)
        ball((x, y, .3), .035, hot)
    cyl((0, 0, .13), .13, .09, chrome, bevel=.016)
    for i in range(8):  # Spiked crown.
        a = i * math.tau / 8
        prism([(math.cos(a) * .2, math.sin(a) * .2), (math.cos(a + .26) * .1, math.sin(a + .26) * .1), (math.cos(a - .26) * .1, math.sin(a - .26) * .1)],
              .2, .03, chrome, .004)
    ball((0, 0, .23), .07, hot)
    for x, y in pillars:  # Jagged lightning from the crown to each pillar.
        points = [(0, 0), (x * .35 + y * .12, y * .35 - x * .12), (x * .62 - y * .08, y * .62 + x * .08), (x, y)]
        for p0, p1 in zip(points, points[1:]):
            strut(p0, p1, .27, .03, glow)
    for (x0, y0), (x1, y1) in zip(pillars, pillars[1:] + pillars[:1]):
        if abs(x0 - x1) < .01 or abs(y0 - y1) < .01:
            mid = ((x0 + x1) / 2 + (y1 - y0) * .1, (y0 + y1) / 2 + (x0 - x1) * .1)
            strut((x0, y0), mid, .3, .018, glow)
            strut(mid, (x1, y1), .3, .018, glow)


def build_wall():
    """Wedge-shaped bulwark pointing at the enemies: layered armor that deflects them, with a shield edge."""
    setup(1.0)
    plate = material("Plate", "#5c6c75", .85, .42, .5)
    plate_dark = material("PlateDark", "#3f4c54", .85, .46, .5)
    frame = material("Frame", "#e0a27f", .8, .38, .4)
    dark = material("Dark", "#1c252c", .8, .5, .3)
    steel = material("Steel", "#d6dde0", 1, .28, .2)
    yellow = material("Yellow", "#e0ac1a", .2, .5, .5)
    black = material("Black", "#15171a", .2, .6, .3)
    field = material("Field", "#7fe4ff", 0, .2, 0, emission="#5fd8ff", strength=.9)
    wedge = [(-.46, -.36), (-.46, .02), (0, .42), (.46, .02), (.46, -.36)]
    prism(wedge, .07, .14, frame, .03)
    for k, mat in ((.82, plate), (.62, plate_dark), (.42, plate)):  # Layered plates, thinner toward the back.
        prism([(x * k, y * k + .04) for x, y in wedge], .14 + (1 - k) * .1, .04, mat, .012)
    edge = [(-.44, .05), (0, .45), (.44, .05), (.4, .02), (0, .4), (-.4, .02)]
    prism(edge, .16, .02, field, .004)                                     # Shield edge facing the enemies.
    for side in (-1, 1):
        for t in (.25, .55, .8):
            bolts([(side * .46 * t, .42 - .4 * t + .01)], .2, .016, steel)
        box((side * .4, -.3, .17), (.06, .1, .04), dark, bevel=.01)      # Lifting lugs on the back corners.
    hazard(0, -.33, .84, .05, .15, yellow, black, blocks=12)
    cyl((0, .0, .25), .07, .02, steel, bevel=.005)
    cyl((0, .0, .265), .035, .012, field, bevel=.003, vertices=16)


def build_tank_wrapper():
    build_tank()


# ---- Colony buildings and resources (campaign) ----------------------------------------

def colony_base(radius=.46):
    deck = material("ColonyDeck", "#3a4850", .85, .48, .5)
    rim = material("ColonyRim", "#26323a", .8, .55, .4)
    prism(regular(8, radius, math.pi / 8), .03, .06, deck, .02)
    prism(regular(8, radius * .9, math.pi / 8), .065, .01, rim, .004)


def build_icon_extractor():
    """Drilling rig over a borehole, with an ore hopper."""
    setup(1.0)
    colony_base()
    steel = material("Steel", "#c7d2d7", 1, .28, .25)
    rust = material("Rust", "#a85a34", .5, .55, .5)
    dark = material("Dark", "#151c21", .8, .5, .3)
    hot = glowing("Hot", "#ffa040", 2.6)
    ore = material("Ore", "#7a6a5a", .2, .85, .3)
    cyl((0, .02, .08), .17, .03, dark, bevel=.008)
    cyl((0, .02, .1), .1, .02, hot, bevel=.004)
    corners = [(-.17, -.15), (.17, -.15), (.17, .19), (-.17, .19)]
    for x, y in corners:  # Derrick legs leaning in to the crown.
        strut((x, y), (x * .35, .02 + (y - .02) * .35), .22, .035, rust)
    for (x0, y0), (x1, y1) in zip(corners, corners[1:] + corners[:1]):
        strut((x0, y0), (x1, y1), .16, .028, steel)
    box((0, .02, .34), (.14, .14, .05), dark, bevel=.015)
    cyl((0, .02, .37), .035, .02, hot, bevel=.004)
    box((.3, -.25, .1), (.22, .18, .1), steel, bevel=.02)
    for i in range(5):
        ball((.26 + (i % 3) * .05, -.27 + (i // 3) * .06, .17), .03, ore)


def build_icon_laboratory():
    """Research dome with a glowing analysis ring and a sensor dish."""
    setup(1.0)
    colony_base()
    white = material("White", "#dde5e8", .3, .3, .35)
    steel = material("Steel", "#c7d2d7", 1, .28, .25)
    glow = glowing("Glow", "#5ee8ff", 2.6)
    dark = material("Dark", "#151c21", .8, .5, .3)
    bpy.ops.mesh.primitive_uv_sphere_add(segments=48, ring_count=24, radius=.3, location=(-.04, -.03, .06))
    dome = bpy.context.active_object
    dome.scale = (1, 1, .75)
    finish(dome, white, 0, smooth=True)
    torus((-.04, -.03, .1), .3, .025, glow)
    for i in range(6):
        a = i * math.tau / 6
        box((-.04 + math.cos(a) * .2, -.03 + math.sin(a) * .2, .25), (.08, .03, .01), glow, bevel=.003, rot=a)
    cyl((.3, .3, .15), .02, .22, steel, bevel=.004)
    cyl((.3, .3, .28), .11, .02, steel, bevel=.008)
    cyl((.3, .3, .3), .03, .03, dark, bevel=.006)
    ball((.3, .3, .33), .02, glow)


def build_icon_refinery():
    """Fuel tanks, a cracking column and a flare stack."""
    setup(1.0)
    colony_base()
    white = material("White", "#d4dcde", .4, .35, .4)
    steel = material("Steel", "#c7d2d7", 1, .28, .25)
    rust = material("Rust", "#8a4b2f", .5, .6, .6)
    flame = glowing("Flame", "#ff8a2a", 2.8)
    for x, y in ((-.2, .15), (-.2, -.17)):
        cyl((x, y, .14), .15, .2, white, bevel=.03)
        torus((x, y, .24), .12, .015, steel)
        cyl((x, y, .25), .04, .02, rust, bevel=.006)
    cyl((.15, .05, .22), .08, .36, steel, bevel=.015)
    for z in (.1, .2, .3):
        torus((.15, .05, z), .085, .012, rust)
    strut((-.05, .15), (.08, .07), .2, .035, rust)
    strut((-.05, -.17), (.08, .02), .2, .035, rust)
    cyl((.32, -.25, .2), .025, .32, steel, bevel=.005)
    ball((.32, -.25, .38), .05, flame)


def build_icon_fortification():
    """Armored bunker ring with crenels around a central gun turret."""
    setup(1.0)
    concrete = material("Concrete", "#6c7378", .3, .7, .6)
    plate = material("Plate", "#4f5b62", .85, .45, .5)
    dark = material("Dark", "#151c21", .8, .5, .3)
    steel = material("Steel", "#d0d8dc", 1, .28, .2)
    red = glowing("Red", "#ff6a52", 2.4)
    prism(regular(6, .47, math.pi / 6), .08, .16, concrete, .03)
    prism(regular(6, .33, math.pi / 6), .12, .16, dark, .02)
    for i in range(6):
        a = i * math.tau / 6
        box((math.cos(a) * .42, math.sin(a) * .42, .2), (.12, .16, .08), plate, bevel=.02, rot=a)
    cyl((0, 0, .18), .17, .1, plate, bevel=.02)
    along_y((0, .2, .24), .03, .26, steel, bevel=.005)
    cyl((0, 0, .245), .05, .02, red, bevel=.005)


def build_icon_housing():
    """Three habitat modules joined by corridors, with warm windows."""
    setup(1.0)
    colony_base()
    white = material("White", "#dfe6e8", .3, .35, .35)
    teal = material("Teal", "#2f6a72", .6, .48, .5)
    warm = glowing("Warm", "#ffc77a", 2.2)
    spots = [(-.2, .16), (.2, .16), (0, -.2)]
    for (x0, y0), (x1, y1) in zip(spots, spots[1:] + spots[:1]):
        strut((x0, y0), (x1, y1), .1, .07, teal)
    for x, y in spots:
        cyl((x, y, .14), .16, .16, white, bevel=.05)
        cyl((x, y, .225), .09, .02, teal, bevel=.006)
        for i in range(4):
            a = i * math.tau / 4 + .4
            box((x + math.cos(a) * .155, y + math.sin(a) * .155, .15), (.02, .05, .04), warm, bevel=.004, rot=a)


def build_icon_materials():
    """A stack of refined metal ingots."""
    setup(1.0)
    steel = material("Steel", "#c3ccd1", 1, .25, .3)
    copper = material("Copper", "#d98a4e", 1, .3, .3)
    for x, y, z, mat in ((-.14, -.12, .07, steel), (.16, -.12, .07, copper), (0, .14, .07, steel), (.02, -.01, .2, copper)):
        bpy.ops.mesh.primitive_cube_add(size=1, location=(x, y, z))
        ingot = bpy.context.active_object
        ingot.scale = (.36, .2, .12)
        bpy.ops.object.transform_apply(scale=True)
        taper = ingot.modifiers.new("Taper", "SIMPLE_DEFORM")
        taper.deform_method = "TAPER"
        taper.factor = -.35
        taper.deform_axis = "Z"
        finish(ingot, mat, .02)


def build_icon_water():
    setup(1.0)
    glass = material("Water", "#3fb4ea", .1, .08, 0, emission="#2a9fd8", strength=.6)
    rim = material("Rim", "#1b5f80", .6, .3, .3)
    pts = [(0, .46)] + [(math.cos(math.radians(a)) * .3, -.1 + math.sin(math.radians(a)) * .3) for a in range(-200, 21, 10)]
    prism(pts, .06, .1, rim, .02)
    prism([(x * .8, y * .8 - .01) for x, y in pts], .13, .06, glass, .02)
    ball((-.1, -.05, .18), .05, glowing_hot("Shine", "#e8fbff"))


def build_icon_food():
    setup(1.0)
    leaf = material("Leaf", "#5fcf63", .2, .35, .2, emission="#3fa548", strength=.3)
    vein = material("Vein", "#2c7a33", .2, .4, .2)
    pts = [(math.cos(math.radians(a)) * .42 * (1 if a < 180 else 1), math.sin(math.radians(a)) * .24) for a in range(0, 360, 12)]
    pts = [(x * .9 + y * .55, y * .9 - x * .55) for x, y in pts]  # Leaf tilted 30 degrees.
    prism(pts, .06, .08, leaf, .02)
    strut((-.33, -.2), (.3, .2), .11, .03, vein)
    for k in (-.12, .05, .2):
        strut((k, k * .65), (k - .1, k * .65 + .15), .11, .02, vein)
        strut((k, k * .65), (k + .1, k * .65 - .13), .11, .02, vein)


def build_icon_population():
    setup(1.0)
    body = material("Body", "#86e6ee", .3, .35, .2, emission="#4fc6d2", strength=.3)
    back = material("Back", "#3f8f99", .4, .4, .2)
    for x, y, s, mat in ((-.2, .06, .8, back), (.2, .06, .8, back), (0, -.04, 1, body)):
        ball((x, y + .2 * s, .2), .11 * s, mat)
        bpy.ops.mesh.primitive_uv_sphere_add(segments=32, ring_count=16, radius=.2 * s, location=(x, y - .1 * s, .1))
        torso = bpy.context.active_object
        torso.scale = (1, .75, .5)
        finish(torso, mat, 0, smooth=True)


def build_icon_anchor():
    setup(1.0)
    steel = material("Steel", "#c3ced3", 1, .28, .25)
    torus((0, .3, .08), .08, .03, steel)
    strut((0, .22), (0, -.32), .08, .06, steel)
    strut((-.16, .12), (.16, .12), .08, .05, steel)
    for side in (-1, 1):
        strut((0, -.34), (side * .26, -.12), .08, .05, steel)
        prism([(side * .26, -.04), (side * .32, -.16), (side * .2, -.15)], .08, .05, steel, .01)


def build_icon_gear():
    setup(1.0)
    steel = material("Steel", "#c3ced3", 1, .28, .25)
    dark = material("Dark", "#1d2a33", .6, .4, .3)
    pts = []
    for i in range(16):
        a = i * math.tau / 16
        r = .4 if i % 2 == 0 else .3
        pts += [(math.cos(a - .12) * r, math.sin(a - .12) * r), (math.cos(a + .12) * r, math.sin(a + .12) * r)]
    prism(pts, .06, .1, steel, .015)
    cyl((0, 0, .12), .13, .04, dark, bevel=.01)


def build_icon_ship():
    setup(1.0)
    white = material("White", "#dde5e8", .3, .3, .35)
    glow = glowing("Glow", "#ffb86a", 2.4)
    prism([(0, .46), (.14, .05), (.38, -.3), (.1, -.24), (0, -.34), (-.1, -.24), (-.38, -.3), (-.14, .05)], .08, .12, white, .02)
    for side in (-1, 1):
        cyl((side * .1, -.34, .08), .05, .04, glow, bevel=.01)


def build_icon_link():
    setup(1.0)
    glow = glowing("Glow", "#5ee8ff", 2.6)
    steel = material("Steel", "#c3ced3", 1, .28, .25)
    for x, y in ((-.18, -.12), (.18, .12)):
        bpy.ops.mesh.primitive_torus_add(major_radius=.18, minor_radius=.05, location=(x, y, .1))
        ring = bpy.context.active_object
        ring.scale = (1.3, .8, 1)
        ring.rotation_euler = (0, 0, math.radians(35))
        finish(ring, steel, 0, smooth=True)
    ball((0, 0, .14), .07, glow)


def build_icon_crane():
    setup(1.0)
    yellow = material("Yellow", "#e0ac1a", .3, .45, .45)
    dark = material("Dark", "#1d2a33", .6, .4, .3)
    box((-.15, -.3, .05), (.3, .18, .1), dark, bevel=.02)
    strut((-.15, -.25), (-.15, .3), .12, .06, yellow)
    strut((-.32, .3), (.38, .3), .14, .05, yellow)
    strut((.3, .3), (.3, .02), .14, .015, dark)
    box((.3, -.02, .14), (.1, .08, .06), dark, bevel=.01)


def build_icon_board():
    setup(1.0)
    frame = material("Frame", "#1d2a33", .6, .4, .3)
    cell = material("Cell", "#2e4a5c", .6, .4, .3)
    glow = glowing("Glow", "#7dff9e", 2.4)
    box((0, 0, .04), (.86, .86, .08), frame, bevel=.03)
    for r in range(3):
        for c in range(3):
            box((-.27 + c * .27, -.27 + r * .27, .1), (.23, .23, .03), cell, bevel=.01)
    ball((0, 0, .14), .08, glow)


def build_icon_solar():
    setup(1.0)
    panel = material("Panel", "#1a2c4a", .4, .12, .2)
    steel = material("Steel", "#c3ced3", 1, .28, .25)
    sun = glowing("Sun", "#ffd36a", 2.6)
    ball((.22, .24, .1), .12, sun)
    box((-.06, -.08, .06), (.62, .46, .06), steel, bevel=.02, rot=.15)
    box((-.06, -.08, .1), (.56, .4, .02), panel, bevel=.01, rot=.15)
    for k in range(3):
        box((-.06 + (k - 1) * .18, -.08, .115), (.012, .4, .004), steel, bevel=0, rot=.15)


def build_icon_data():
    """A data chip with gold pins and a glowing core."""
    setup(1.0)
    board = material("Board", "#1d2b36", .5, .4, .3)
    gold = material("Gold", "#f2c46e", 1, .25, .2)
    glow = glowing("Glow", "#5ee8ff", 2.8)
    box((0, 0, .05), (.56, .56, .06), board, bevel=.04)
    for i in range(5):
        for side in (-1, 1):
            box((side * .34, -.2 + i * .1, .04), (.1, .04, .02), gold, bevel=.005)
            box((-.2 + i * .1, side * .34, .04), (.04, .1, .02), gold, bevel=.005)
    box((0, 0, .09), (.3, .3, .03), gold, bevel=.01)
    box((0, 0, .11), (.2, .2, .02), glow, bevel=.006)


BUILDERS = {
    "generator": lambda: build_generator(20 + PHASE * 60), "reactor": build_reactor, "relay": build_relay, "pulse": build_pulse,
    "cryo": build_cryo, "tesla": build_tesla, "wall": build_wall, "repair": build_repair,
    "scout": build_scout, "runner": build_runner, "armor": build_armor, "jammer": build_jammer, "splitter": build_splitter,
    "sapper": build_sapper, "bulwark": build_bulwark, "thermal": build_thermal, "grounder": build_grounder, "hunter": build_hunter,
    "commander": build_commander, "tank": build_tank_wrapper, "helicopter": build_helicopter, "bomber": build_bomber,
    "icon_energy": build_icon_energy, "icon_base": build_icon_base, "icon_wave": build_icon_wave,
    "icon_upgrade": build_icon_upgrade, "icon_recycle": build_icon_recycle,
    "icon_extractor": build_icon_extractor, "icon_laboratory": build_icon_laboratory, "icon_refinery": build_icon_refinery,
    "icon_fortification": build_icon_fortification, "icon_housing": build_icon_housing,
    "icon_materials": build_icon_materials, "icon_data": build_icon_data,
    "icon_solar": build_icon_solar, "icon_water": build_icon_water, "icon_food": build_icon_food, "icon_population": build_icon_population, "icon_anchor": build_icon_anchor,
    "icon_gear": build_icon_gear, "icon_ship": build_icon_ship, "icon_link": build_icon_link, "icon_crane": build_icon_crane, "icon_board": build_icon_board,
}
ONLY = sys.argv[sys.argv.index("--") + 2:]
MODULES = ("generator", "reactor", "relay", "pulse", "cryo", "tesla", "wall", "repair", "icon_energy", "icon_base", "icon_wave", "icon_upgrade", "icon_recycle",
           "icon_extractor", "icon_laboratory", "icon_refinery", "icon_fortification", "icon_housing", "icon_materials", "icon_data",
           "icon_solar", "icon_water", "icon_food", "icon_population", "icon_anchor", "icon_gear", "icon_ship", "icon_link", "icon_crane", "icon_board")
# Size on the board, in cells. Everything reads at about the same size; a few are slightly larger.
ENEMY_SIZE = {"commander": 1.2, "tank": 1.1, "bomber": 1.1, "armor": 1.05, "helicopter": 1.05}
FRAMES = {"helicopter": 1}
LAYERS = {"generator": "fan", "pulse": "barrel", "cryo": "turret", "helicopter": "rotor", "tank": "turret", "hunter": "turret", "commander": "turret"}


# Modules share one fixed scale so their bodies match; a few models are scaled to line up.
MODULE_SCALE = {"generator": .93, "reactor": .93, "relay": .84, "cryo": .96}


def fit(name):
    """Camera framing. Modules use a fixed one-cell frame; enemies and icons fit their size target."""
    if name in MODULES and not name.startswith("icon_"):
        return 0, 0, 1.0
    points = []
    for obj in bpy.context.scene.objects:
        if obj.type == "MESH" and not obj.is_shadow_catcher:
            points += [obj.matrix_world @ Vector(corner) for corner in obj.bound_box]
    min_x, max_x = min(p.x for p in points), max(p.x for p in points)
    min_y, max_y = min(p.y for p in points), max(p.y for p in points)
    largest = max(max_x - min_x, max_y - min_y)
    if name in LAYERS:  # Turning parts pivot on the sprite center, so the model must stay centered on its origin.
        largest = 2 * max(abs(min_x), abs(max_x), abs(min_y), abs(max_y))
        min_x = max_x = min_y = max_y = 0
    ortho = largest / .96 if name in MODULES else largest * 1.5 / ENEMY_SIZE.get(name, .96)
    return (min_x + max_x) / 2, (min_y + max_y) / 2, ortho


def scale_scene(factor):
    for obj in bpy.context.scene.objects:
        if obj.type == "MESH" and not obj.is_shadow_catcher:
            obj.location *= factor
            obj.scale *= factor

def show_only(layer):
    for obj in bpy.context.scene.objects:
        if obj.type == "MESH" and not obj.is_shadow_catcher:
            obj.hide_render = layer is not None and obj.get("layer", "base") != layer


if __name__ == "__main__":
    for sprite_name, builder in BUILDERS.items():
        if ONLY and sprite_name not in ONLY:
            continue
        count = FRAMES.get(sprite_name, 1 if sprite_name in MODULES else 4)
        framing = None
        for frame in range(count):
            PHASE = frame / count
            LAYER = "base"
            clear()
            builder()
            if sprite_name in MODULE_SCALE:
                scale_scene(MODULE_SCALE[sprite_name])
            framing = framing or fit(sprite_name)  # Same framing for every frame, so nothing jitters.
            camera = bpy.context.scene.camera
            camera.location.x, camera.location.y = framing[0], framing[1]
            camera.data.ortho_scale = framing[2]
            moving = LAYERS.get(sprite_name)
            if moving and sprite_name in MODULES:
                # Full picture for the build cards, then the static part and the moving part.
                show_only(None)
                render(sprite_name)
                show_only("base")
                render(f"{sprite_name}_base")
                show_only(moving)
                render(f"{sprite_name}_{moving}")
            elif moving:
                # Enemies: every animation frame of the body, and the turning part once.
                show_only("base")
                render(f"{sprite_name}_{frame}")
                if frame == 0:
                    show_only(moving)
                    render(f"{sprite_name}_{moving}")
            else:
                render(f"{sprite_name}_{frame}" if count > 1 else sprite_name)
    print("RENDER_DONE")
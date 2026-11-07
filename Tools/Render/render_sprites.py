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

    octagon((0, -.01, .045), .36, .36, .09, shell, bevel=.018)
    octagon((0, -.01, .095), .325, .325, .012, dark, bevel=.004)
    for side in (-1, 1):  # Side battery packs break up the outline.
        box((side * .35, -.01, .055), (.07, .34, .09), shell, bevel=.016)
        for i in range(4):
            box((side * .355, -.12 + i * .073, .102), (.04, .04, .012), dark, bevel=.004)
    for side in (-1, 1):  # Carry handle over the top edge, like the original sprite.
        box((side * .12, .385, .055), (.03, .13, .03), handle, bevel=.01)
    box((0, .44, .055), (.27, .032, .03), handle, bevel=.012)

    torus((0, -.01, .125), .288, .03, steel)
    torus((0, -.01, .138), .238, .014, brass)
    torus((0, -.01, .112), .205, .006, glow)
    global LAYER
    LAYER = "fan"
    for i in range(6):
        blade_mesh(math.radians(blade_angle + i * 60), .15, blade)
    cyl((0, -.01, .165), .05, .04, brass, bevel=.008)
    cyl((0, -.01, .19), .024, .012, steel, bevel=.003)
    LAYER = "base"
    for i in range(6):
        a = i * math.pi / 3 + .5
        cyl((math.cos(a) * .265, -.01 + math.sin(a) * .265, .15), .012, .012, steel, bevel=.002, vertices=6)

    box((0, -.365, .045), (.22, .08, .05), handle, bevel=.012)  # Connector at the bottom, like the original.
    for x in (-.055, 0, .055):
        cyl((x, -.38, .074), .009, .012, glow, bevel=.002, vertices=16)


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

    # Turret: bright rings and a central sensor instead of a dark hatch.
    cyl((0, .03, .21), .215, .06, plate, bevel=.016)
    torus((0, .03, .243), .205, .014, trim)
    cyl((0, .03, .26), .14, .035, dark, bevel=.01)
    cyl((0, .03, .28), .085, .018, hull, bevel=.006)
    cyl((0, .03, .293), .042, .012, glow, bevel=.004, vertices=32)
    bolts([(math.cos(a) * .178, .03 + math.sin(a) * .178) for a in [i * math.pi / 4 for i in range(8)]], .243, .01, bolt)

    # Cannon toward the bottom of the screen: the direction enemies advance.
    cyl((0, -.36, .23), .044, .6, barrel, bevel=.005, rot=(math.radians(90), 0, 0))
    cyl((0, -.17, .23), .066, .1, plate, bevel=.01, rot=(math.radians(90), 0, 0))
    cyl((0, -.66, .23), .064, .09, trim, bevel=.012, rot=(math.radians(90), 0, 0))


def prism(points, z, height, mat, bevel=.012):
    """Extrudes a polygon, given counterclockwise, into a flat solid."""
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

def build_cryo():
    setup(1.0)
    base = material("Base", "#60757f", .9, .4, .3)
    tank = material("Tank", "#7fd6ff", .25, .16, .05)
    dark = material("Dark", "#1d2b33", .8, .45, .3)
    steel = material("Steel", "#d0dce1", 1, .24, .15)
    glow = glowing("Glow", "#a8ecff", 2.6)
    box((0, -.06, .045), (.8, .66, .09), base, bevel=.03)
    for side in (-1, 1):
        along_y((side * .2, -.08, .16), .16, .52, tank, bevel=.03)
        for y in (-.26, .1):
            along_y((side * .2, y, .16), .17, .03, steel, bevel=.006)
    box((0, -.34, .11), (.34, .05, .04), steel, bevel=.012)
    cyl((0, .2, .24), .2, .05, dark, bevel=.012)
    torus((0, .2, .27), .19, .018, steel)
    for i in range(3):
        a = i * math.pi / 3
        box((0, .2, .275), (.3, .018, .012), glow, bevel=.003, rot=a)
    cyl((0, .2, .285), .035, .015, glow, bevel=.004)


def build_tesla():
    setup(1.0)
    base = material("Base", "#57536f", .9, .4, .3)
    dark = material("Dark", "#1f1d2c", .8, .5, .3)
    coil = material("Coil", "#c0a8ff", .9, .28, .2)
    trim = material("Trim", "#8f78d8", 1, .3, .15)
    glow = glowing("Glow", "#b98cff", 3)
    box((0, -.3, .05), (.62, .24, .1), base, bevel=.025)
    box((0, -.3, .105), (.42, .11, .012), glow, bevel=.003)
    box((0, -.06, .05), (.4, .34, .08), dark, bevel=.02)
    for side in (-1, 1):
        box((side * .335, -.02, .05), (.14, .62, .08), base, bevel=.02)
        for i in range(4):
            cyl((side * .335, -.24 + i * .15, .11), .062, .05, coil, bevel=.012)
            cyl((side * .335, -.24 + i * .15, .14), .026, .012, dark, bevel=.003)
    prism([(0, .48), (-.11, .08), (0, -.2), (.11, .08)], .15, .06, trim, .012)
    prism([(0, .4), (-.06, .08), (0, -.12), (.06, .08)], .19, .02, glow, .005)
    for side in (-1, 1):
        strut((side * .09, .05), (side * .3, .22), .1, .018, trim)


def build_repair():
    setup(1.0)
    frame = material("Frame", "#86999f", .9, .36, .25)
    dark = material("Dark", "#17242a", .75, .5, .3)
    hood = material("Hood", "#5d7178", .85, .4, .3)
    arm = material("Arm", "#9de6a4", .7, .36, .2)
    glow = glowing("Glow", "#7dff9c", 2.4)
    box((0, -.03, .05), (.82, .8, .1), frame, bevel=.03)
    box((0, -.03, .105), (.7, .68, .012), dark, bevel=.004)
    box((0, .33, .15), (.56, .2, .09), hood, bevel=.03)
    box((0, .33, .198), (.4, .05, .012), frame, bevel=.004)
    box((0, -.1, .14), (.13, .36, .05), glow, bevel=.012)
    box((0, -.1, .14), (.36, .13, .05), glow, bevel=.012)
    for side in (-1, 1):
        strut((side * .3, .14), (side * .13, -.02), .13, .03, arm)
        ball((side * .3, .14, .13), .032, frame)
    box((0, -.36, .13), (.34, .06, .03), frame, bevel=.01)


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


def build_relay():
    """Arrows and power lines running outward from the center: this piece extends the network."""
    setup(1.0)
    arm = material("Arm", "#566e7a", .9, .38, .3)
    dark = material("Dark", "#172228", .8, .5, .3)
    steel = material("Steel", "#c7d2d7", 1, .26, .2)
    line = glowing("Line", "#4fe6f5", 2.4)
    hot = glowing_hot("Hot", "#c8fbff")
    for angle in (0, 90, 180, 270):
        c, s = math.cos(math.radians(angle)), math.sin(math.radians(angle))
        rot = math.radians(angle)
        box((c * .27, s * .27, .05), (.38, .2, .09), arm, bevel=.025, rot=rot)
        box((c * .28, s * .28, .1), (.36, .13, .012), dark, bevel=.003, rot=rot)
        box((c * .27, s * .27, .112), (.34, .022, .01), line, bevel=.002, rot=rot)
        chevron(c * .2, s * .2, angle, .06, .026, .118, .01, line)
        chevron(c * .31, s * .31, angle, .06, .026, .118, .01, line)
        cyl((c * .41, s * .41, .08), .068, .08, steel, bevel=.012)
        torus((c * .41, s * .41, .122), .05, .009, line)
        cyl((c * .41, s * .41, .124), .024, .012, hot, bevel=.003, vertices=24)
    cyl((0, 0, .11), .2, .1, arm, bevel=.016)
    torus((0, 0, .16), .165, .02, steel)
    torus((0, 0, .165), .115, .01, line)
    cyl((0, 0, .17), .06, .025, hot, bevel=.006)


def build_pulse():
    """An artillery emplacement: footed base, toothed traverse ring, capacitors, shrouded barrel."""
    setup(1.0)
    base = material("Base", "#5f7480", .9, .38, .3)
    gold = material("Gold", "#f2c977", 1, .3, .25)
    dark = material("Dark", "#1d272e", .8, .5, .3)
    steel = material("Steel", "#c7d2d7", 1, .26, .2)
    glow = glowing("Glow", "#ffd46e", 2.6)
    cy = -.14
    for a, b in ((1, 1), (-1, 1), (1, -1), (-1, -1)):
        strut((a * .16, cy + b * .16), (a * .35, cy + b * .29), .05, .07, base)
        box((a * .37, cy + b * .3, .03), (.13, .13, .05), dark, bevel=.014)
        cyl((a * .37, cy + b * .3, .062), .022, .015, steel, bevel=.003, vertices=6)
    prism(regular(8, .32, math.pi / 8, cy=cy), .06, .1, base, .02)
    for side in (-1, 1):
        for i in range(3):
            box((side * .24, cy - .09 + i * .09, .12), (.075, .065, .03), glow if i == 1 else dark, bevel=.006)
    for i in range(24):
        a = i * math.tau / 24
        box((math.cos(a) * .225, cy + math.sin(a) * .225, .125), (.032, .02, .024), gold, bevel=.003, rot=a)
    cyl((0, cy, .13), .205, .05, dark, bevel=.01)
    torus((0, cy, .155), .185, .012, steel)
    cyl((0, cy, .175), .15, .05, base, bevel=.016)
    box((0, cy + .02, .225), (.2, .24, .05), base, bevel=.02)
    global LAYER
    LAYER = "barrel"
    box((0, .09, .2), (.11, .3, .06), dark, bevel=.012)
    for i in range(4):  # Cooling shroud.
        along_y((0, .1 + i * .062, .2), .066, .036, steel, bevel=.006)
    along_y((0, .34, .2), .045, .24, gold, bevel=.005)
    along_y((0, .46, .2), .068, .06, steel, bevel=.012)
    LAYER = "base"
    cyl((0, cy + .02, .26), .03, .012, glow, bevel=.004)


def build_wall():
    """A shield with a force field along its top edge, the side facing the enemies."""
    setup(1.0)
    frame = material("Frame", "#e8a57c", .8, .36, .25)
    plate = material("Plate", "#55666f", .85, .42, .3)
    dark = material("Dark", "#1c252c", .8, .5, .3)
    steel = material("Steel", "#d6dde0", 1, .28, .15)
    field = glowing("Field", "#8fe9ff", 2.4)
    outline = [(-.47, .36), (-.47, -.06), (0, -.5), (.47, -.06), (.47, .36)]
    prism(outline, .055, .11, frame, .018)
    prism([(x * .86, y * .86 + .01) for x, y in outline], .118, .016, dark, .004)
    prism([(x * .76, y * .76 + .01) for x, y in outline], .14, .03, plate, .01)
    chevron(0, -.18, 270, .15, .06, .165, .016, frame)
    chevron(0, -.04, 270, .15, .06, .165, .016, frame)
    chevron(0, .1, 270, .15, .06, .165, .016, frame)
    bolts([(-.33, .24), (.33, .24), (-.33, -.03), (.33, -.03), (0, -.34)], .16, .014, steel)
    for side in (-1, 1):  # Field emitters.
        box((side * .42, .36, .12), (.1, .12, .12), steel, bevel=.02)
        cyl((side * .42, .37, .185), .034, .014, field, bevel=.004, vertices=24)
    box((0, .435, .08), (.72, .034, .06), field, bevel=.008)
    box((0, .468, .06), (.66, .012, .02), transparent("FieldEdge", "#bff3ff", .45), bevel=.002)


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
    along_y((0, -.32, .17), .026, .68, steel, bevel=.004)
    along_y((0, -.62, .17), .04, .07, dark, bevel=.008)
    along_y((.06, -.17, .2), .03, .22, dark, bevel=.006)
    along_y((.06, -.285, .2), .022, .012, hot, bevel=.002)
    cyl((-.06, -.13, .2), .028, .03, red, bevel=.005)
    box((0, -.7, .17), (.008, .13, .008), red, bevel=0)


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
    for side in (-1, 1):
        along_y((side * .17, -.42, .25), .042, .46, steel, bevel=.006)
        along_y((side * .17, -.66, .25), .058, .06, gold, bevel=.01)
        box((side * .17, -.12, .25), (.13, .24, .09), dark, bevel=.02)
    prism(regular(6, .3, math.pi / 6, cy=.05), .26, .07, hull, .018)
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


def build_tank_wrapper():
    build_tank()


BUILDERS = {
    "generator": lambda: build_generator(20 + PHASE * 60), "reactor": build_reactor, "relay": build_relay, "pulse": build_pulse,
    "cryo": build_cryo, "tesla": build_tesla, "wall": build_wall, "repair": build_repair,
    "scout": build_scout, "runner": build_runner, "armor": build_armor, "jammer": build_jammer, "splitter": build_splitter,
    "sapper": build_sapper, "bulwark": build_bulwark, "thermal": build_thermal, "grounder": build_grounder, "hunter": build_hunter,
    "commander": build_commander, "tank": build_tank_wrapper, "helicopter": build_helicopter, "bomber": build_bomber,
}
ONLY = sys.argv[sys.argv.index("--") + 2:]
MODULES = ("generator", "reactor", "relay", "pulse", "cryo", "tesla", "wall", "repair")
# Size on the board, in cells. Everything reads at about the same size; a few are slightly larger.
ENEMY_SIZE = {"commander": 1.2, "tank": 1.1, "bomber": 1.1, "armor": 1.05, "helicopter": 1.05}
FRAMES = {"helicopter": 1}
LAYERS = {"generator": "fan", "pulse": "barrel", "helicopter": "rotor"}


def fit(name):
    """Frames the model so its largest dimension takes the target size, centered on the cell."""
    points = []
    for obj in bpy.context.scene.objects:
        if obj.type == "MESH" and not obj.is_shadow_catcher:
            points += [obj.matrix_world @ Vector(corner) for corner in obj.bound_box]
    min_x, max_x = min(p.x for p in points), max(p.x for p in points)
    min_y, max_y = min(p.y for p in points), max(p.y for p in points)
    largest = max(max_x - min_x, max_y - min_y)
    ortho = largest / .96 if name in MODULES else largest * 1.5 / ENEMY_SIZE.get(name, .96)
    return (min_x + max_x) / 2, (min_y + max_y) / 2, ortho


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
            framing = framing or fit(sprite_name)  # Same framing for every frame, so nothing jitters.
            camera = bpy.context.scene.camera
            camera.location.x, camera.location.y = framing[0], framing[1]
            camera.data.ortho_scale = framing[2]
            moving = LAYERS.get(sprite_name)
            if moving:
                # Full picture for the build cards, then the static part and the moving part.
                show_only(None)
                render(sprite_name)
                show_only("base")
                render(f"{sprite_name}_0" if sprite_name not in MODULES else f"{sprite_name}_base")
                show_only(moving)
                render(f"{sprite_name}_{moving}")
            else:
                render(f"{sprite_name}_{frame}" if count > 1 else sprite_name)
    print("RENDER_DONE")
"""Renders test sprites for Colonia 09 from procedural 3D models, seen from straight above.

Usage:
  blender --background --factory-startup --python Tools/Render/render_test.py -- OUTPUT_DIR
"""
import math
import os
import sys

import bpy

OUT = sys.argv[sys.argv.index("--") + 1]
SIZE = 512
SAMPLES = 96
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
    return mat


def finish(obj, mat, bevel=0, smooth=False):
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
    key_obj.rotation_euler = (math.radians(20), math.radians(-14), 0)
    scene.collection.objects.link(key_obj)

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
    clear()
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
    for i in range(6):
        blade_mesh(math.radians(blade_angle + i * 60), .15, blade)
    cyl((0, -.01, .165), .05, .04, brass, bevel=.008)
    cyl((0, -.01, .19), .024, .012, steel, bevel=.003)
    for i in range(6):
        a = i * math.pi / 3 + .5
        cyl((math.cos(a) * .265, -.01 + math.sin(a) * .265, .15), .012, .012, steel, bevel=.002, vertices=6)

    box((0, -.365, .045), (.22, .08, .05), handle, bevel=.012)  # Connector at the bottom, like the original.
    for x in (-.055, 0, .055):
        cyl((x, -.38, .074), .009, .012, glow, bevel=.002, vertices=16)


def build_tank():
    clear()
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
            box((side * .4, -.44 + i * .055, .103), (.16, .03, .014), link, bevel=.004)

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


build_generator(20)
render("generator")
build_generator(50)
render("generator_b")
build_tank()
render("tank")
print("RENDER_DONE")
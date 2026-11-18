"""Tilted ("almost 3D") view shared by the board and unit renders.

The camera leans back TILT degrees from straight down, looking north. The whole scene is stretched
north-south by 1 / cos(TILT) first, so every ground point lands exactly where the straight-down view
put it: the grid, taps and rules stay the same, and anything with height rises up the screen by
height * LIFT. Flat parts that turn about the vertical axis (fans, rotors) still turn as plain 2D
rotations, around a point raised by their height * LIFT.
"""
import math

import bpy

TILT = math.radians(55)
ZSCALE = 1.5  # Taller than the straight-down models, so fronts read clearly.
LIFT = math.sin(TILT) * ZSCALE  # Screen rise per unit of model height.
STRETCH = 1 / math.cos(TILT)


def apply(target=(0.0, 0.0), skip=()):
    """Stretches every mesh under one empty and aims the camera at the ground point target."""
    scene = bpy.context.scene
    root = bpy.data.objects.new("Stretch", None)
    scene.collection.objects.link(root)
    for obj in list(scene.objects):
        if obj.type != "MESH" or obj.parent is not None or obj in skip:
            continue
        if getattr(obj, "is_shadow_catcher", False):
            continue
        obj.parent = root
    root.scale = (1, STRETCH, ZSCALE)
    cam = scene.camera
    cam.rotation_euler = (TILT, 0, 0)
    gx, gy = target[0], target[1] * STRETCH
    cam.location = (gx, gy - 20 * math.sin(TILT), 20 * math.cos(TILT))
    cam.data.clip_end = 200
    return root


def pivot(objects, angle_degrees):
    """Turns objects about the vertical axis through the origin, before the stretch."""
    root = bpy.data.objects.get("Stretch")
    spin = bpy.data.objects.new("Pivot", None)
    bpy.context.scene.collection.objects.link(spin)
    spin.parent = root
    for obj in objects:
        obj.parent = spin
    spin.rotation_euler = (0, 0, math.radians(angle_degrees))
    return spin

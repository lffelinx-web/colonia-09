"""Test: renders the colony building FBX models (made outside this project) as colony building sprites.

Usage:
  blender --background --factory-startup --python Tools/Render/render_fbx_models.py -- OUTPUT_DIR SOURCE_DIR [night] [names...]

SOURCE_DIR is the Validacion project's Assets/Colonia09 folder. Each family has its own texture sheet; glass
meshes get a see-through tinted material like the Unity one. Every model gets the houses' scale, so N4's
platform fills a tile and the other buildings keep their relative sizes.
"""
import os
import sys

import bpy
from mathutils import Vector

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import render_colony as rc  # noqa: E402

OUT = rc.OUT
SRC = rc.EXTRA[0]
ONLY = [a for a in rc.EXTRA[1:] if a != "night"]
TILE, WIDEST = .94, 8.53  # N4's platform, in model meters, fills .94 of a tile.

# Model name: (folder under SOURCE_DIR, texture prefix, glass tint and alpha from the Unity material).
HOUSES = ("", "Colonia09", (.36, .67, .75, .16))
FAMILIES = {
    "N": HOUSES, "AS": HOUSES,
    "EX": ("Extractores", "Extractor", None),
    "LB": ("Laboratorios", "Laboratorio", (.23, .63, .76, .14)),
    "PS": ("Solares", "Solar", None),
    "GH": ("Granjas", "Granja", (.34, .64, .59, .095)),
}
NAMES = ["N1_RefugioBasico", "N2_ViviendaModular", "N3_ViviendaFamiliar", "N4_ResidencialColectivo",
         "AS01_ViviendaAutosuficiente",
         "EX01_ExtractorBasico", "EX02_ExtractorProcesador", "EX03_ExtractorIndustrial",
         "LB01_LaboratorioBasico", "LB02_LaboratorioDeAnalisis", "LB03_LaboratorioAvanzado",
         "PS01_PlantaSolarBasica", "PS02_PlantaSolarModular", "PS03_PlantaSolarAvanzada",
         "GH01_GranjaHidroponicaBasica", "GH02_GranjaHidroponicaModular", "GH03_GranjaHidroponicaAvanzada"]


def family(name):
    return FAMILIES[name[:2] if name[:2] in FAMILIES else name[:1]]


def structure(folder, prefix):
    mat = bpy.data.materials.new(prefix)
    mat.use_nodes = True
    nt = mat.node_tree
    bsdf = nt.nodes["Principled BSDF"]
    color = nt.nodes.new("ShaderNodeTexImage")
    color.image = bpy.data.images.load(os.path.join(SRC, folder, "Texturas", prefix + "_Color.png"))
    glow = nt.nodes.new("ShaderNodeTexImage")
    glow.image = bpy.data.images.load(os.path.join(SRC, folder, "Texturas", prefix + "_Emision.png"))
    nt.links.new(color.outputs[0], bsdf.inputs["Base Color"])
    nt.links.new(glow.outputs[0], bsdf.inputs["Emission Color"])
    bsdf.inputs["Emission Strength"].default_value = 2.4 if rc.NIGHT else 1.6
    bsdf.inputs["Metallic"].default_value = .21
    bsdf.inputs["Roughness"].default_value = .55
    return mat


def glass(tint):
    mat = bpy.data.materials.new("Glass")
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes["Principled BSDF"]
    bsdf.inputs["Base Color"].default_value = (*tint[:3], 1)
    # Sprites are small: a little more body than the Unity glass so the panes still read.
    bsdf.inputs["Alpha"].default_value = min(1, tint[3] * 2.5)
    bsdf.inputs["Roughness"].default_value = .08
    bsdf.inputs["Metallic"].default_value = .02
    return mat


def place(name):
    folder, prefix, tint = family(name)
    before = set(bpy.context.scene.objects)
    bpy.ops.import_scene.fbx(filepath=os.path.join(SRC, folder, "Modelos", name + ".fbx"))
    new = [o for o in bpy.context.scene.objects if o not in before]
    body = structure(folder, prefix)
    pane = glass(tint) if tint else None
    for o in new:
        if o.type == "MESH":
            is_glass = any("vidrio" in m.name.lower() for m in o.data.materials if m)
            o.data.materials.clear()
            o.data.materials.append(pane if is_glass and pane else body)
    root = bpy.data.objects.new("Model", None)
    bpy.context.scene.collection.objects.link(root)
    for o in new:
        if o.parent is None:
            o.parent = root
    bpy.context.view_layer.update()
    pts = [o.matrix_world @ Vector(c) for o in new if o.type == "MESH" for c in o.bound_box]
    lo = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts)))
    hi = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
    s = TILE / WIDEST
    root.scale = (s, s, s)
    root.location = (-(lo.x + hi.x) / 2 * s, -(lo.y + hi.y) / 2 * s, -lo.z * s)


if __name__ == "__main__":
    os.makedirs(OUT, exist_ok=True)
    for name in NAMES:
        if ONLY and not any(name.startswith(o) for o in ONLY):
            continue
        rc.sprite_scene()
        if rc.NIGHT:
            rc.apply_night()
        place(name)
        bpy.context.scene.render.filepath = os.path.join(OUT, name + rc.suffix() + ".png")
        bpy.ops.render.render(write_still=True)
    print("RENDER_DONE")

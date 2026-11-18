"""Renders the board pieces in the tilted view: modules at levels 1-3 and the enemies.

Usage:
  blender --background --factory-startup --python Tools/Render/render_units.py -- OUTPUT_DIR [names] [preview]

Every sprite is 384 x 640 pixels. Its pivot is the ground point under the piece, 30 % of the way up
the image (Unity imports them with pivot 0.5, 0.3). Horizontally it covers the same width as the
straight-down sprites, so pieces keep their size on the board.

Outputs, by piece:
  modules  NAME_L (whole piece), NAME_L_base and NAME_L_PART_DD for the parts that aim (DD = direction)
           and NAME_L_fan for the generator fan, which Unity spins.
  enemies  NAME_F (walk frames), NAME_turret_DD, helicopter_rotor.
Directions go counterclockwise from the piece's forward direction in steps of 360 / DIRS degrees.
units_meta.txt lists, for the spinning parts, how high their hub sits as a fraction of sprite width.
"""
import math
import os
import sys

import bpy

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import render_sprites as rs  # noqa: E402
import tilt  # noqa: E402

OUT = sys.argv[sys.argv.index("--") + 1]
ARGS = sys.argv[sys.argv.index("--") + 2:]
PREVIEW = "preview" in ARGS
ONLY = [a for a in ARGS if a != "preview"]
os.makedirs(OUT, exist_ok=True)

PIVOT = .3  # Height of the ground point in the image, as a fraction of its height.
DIRS = 24 if "preview" not in sys.argv else 4
SIZE = (192, 320) if PREVIEW else (384, 640)
MODULES = ["generator", "reactor", "relay", "pulse", "cryo", "tesla", "wall", "repair"]
ENEMIES = ["scout", "runner", "armor", "jammer", "splitter", "sapper", "bulwark", "thermal", "grounder", "hunter",
           "commander", "tank", "helicopter", "bomber"]
AIMED = {"pulse": "barrel", "cryo": "turret", "tank": "turret", "hunter": "turret", "commander": "turret"}
SPUN = {"generator": "fan", "helicopter": "rotor"}
meta = []


# ---- Evolution: what each module gains at levels 2 and 3 ------------------------------

def mats():
    return {
        "steel": rs.material("EvoSteel", "#c7d2d7", 1, .26, .2),
        "dark": rs.material("EvoDark", "#1b252c", .8, .5, .3),
        "brass": rs.material("EvoBrass", "#f0c46a", 1, .28, .25),
        "armor": rs.material("EvoArmor", "#6f8591", .9, .38, .3),
    }


def tier_posts(level, accent, angles, r, z0, m):
    """Corner posts with status lamps: short and steel at level 2, tall and brass at level 3."""
    lamp = rs.glowing("TierLamp", accent, 2.6)
    h = .09 if level == 2 else .19
    for a in angles:
        x, y = math.cos(math.radians(a)) * r, math.sin(math.radians(a)) * r
        rs.cyl((x, y, z0 + h / 2), .034, h, m["steel"] if level == 2 else m["brass"], bevel=.006, vertices=24)
        rs.cyl((x, y, z0 + h + .008), .026, .016, lamp, bevel=.003, vertices=16)


def evolve_pulse(level, m):
    glow = rs.glowing("EvoGlow", "#ffd46e", 2.6)
    tier_posts(level, "#ffd46e", (45, 135, 225, 315), .37, .055, m)
    for a in (0, 180):  # Armor skirt on the flanks.
        rs.arc(0, 0, .3, .36, a - 35, a + 35, .135, .05, m["armor"], .008, n=10)
    rs.LAYER = "barrel"
    for side in (-1, 1):  # Twin side barrels.
        rs.along_y((side * .085, .31, .2), .028, .22, m["steel"], bevel=.005)
        rs.along_y((side * .085, .43, .2), .038, .03, m["dark"], bevel=.006)
    if level >= 3:
        rs.box((0, -.06, .275), (.16, .14, .05), m["dark"], bevel=.012)  # Capacitor bank on top.
        for i in range(3):
            rs.box((0, -.11 + i * .05, .303), (.12, .02, .008), glow, bevel=.002)
        rs.along_y((0, .52, .2), .078, .06, m["brass"], bevel=.012)  # Muzzle brake.
        for side in (-1, 1):
            rs.box((side * .12, .02, .24), (.03, .2, .05), m["brass"], bevel=.006)
    rs.LAYER = "base"


def evolve_cryo(level, m):
    ice = rs.material("EvoIce", "#9fe6ff", .1, .08, 0, emission="#5fd2ff", strength=.35)
    coolant = rs.material("EvoCoolant", "#57c8ff", .2, .1, 0, emission="#38b8ff", strength=.5)
    glow = rs.glowing("EvoGlow", "#a8ecff", 2.6)
    for i, a in enumerate((25, 100, 170, 250, 325)):  # Larger frost crystals on the rim.
        r = math.radians(a)
        x, y = math.cos(r) * .33, math.sin(r) * .33
        h = .1 if level == 2 else .2
        rs.prism([(x + math.cos(r) * .06, y + math.sin(r) * .06), (x + math.cos(r + 2.1) * .045, y + math.sin(r + 2.1) * .045),
                  (x + math.cos(r - 2.1) * .045, y + math.sin(r - 2.1) * .045)], .1 + h / 2, h, ice, .006)
    rs.LAYER = "turret"
    rs.along_y((0, -.2, .23), .062, .15, coolant, bevel=.02)  # Coolant tank on the back of the turret.
    for y in (-.26, -.14):
        rs.along_y((0, y, .23), .068, .018, m["steel"], bevel=.004)
    if level >= 3:
        for side in (-1, 1):  # Side nozzles.
            rs.along_y((side * .12, .27, .19), .034, .2, m["steel"], bevel=.006)
            rs.cyl((side * .12, .375, .19), .03, .012, glow, bevel=.003, rot=(math.radians(90), 0, 0))
        rs.torus((0, 0, .26), .1, .012, m["brass"])
    rs.LAYER = "base"


def evolve_generator(level, m):
    glow = rs.glowing("EvoGlow", "#ffb765", 2.4)
    tier_posts(level, "#ffb765", (45, 135, 225, 315), .4, .1, m)
    for side in (-1, 1):  # Extra battery packs.
        rs.box((side * .23, .34, .13), (.16, .07, .06), m["armor"], bevel=.014)
        rs.box((side * .23, .34, .165), (.1, .03, .01), glow, bevel=.003)
    if level >= 3:
        rs.torus((0, 0, .2), .33, .02, m["brass"])  # Raised fan guard.
        for i in range(6):
            a = i * math.pi / 3 + .26
            rs.cyl((math.cos(a) * .33, math.sin(a) * .33, .165), .012, .07, m["steel"], bevel=.002, vertices=8)


def evolve_reactor(level, m):
    hot = rs.glowing_hot("EvoHot", "#d8ffec")
    for a, b in ((1, 1), (-1, 1), (1, -1), (-1, -1)):  # Cooling stacks over the corner pods.
        h = .1 if level == 2 else .2
        rs.cyl((a * .325, b * .325, .155 + h / 2), .04, h, m["steel"], bevel=.006)
        rs.torus((a * .325, b * .325, .155 + h), .04, .008, m["brass"])
    if level >= 3:
        rs.cyl((0, 0, .29), .06, .14, rs.transparent("EvoBeam", "#9dffd2", .55), bevel=0, vertices=32)
        rs.cyl((0, 0, .36), .03, .02, hot, bevel=.004)
        rs.torus((0, 0, .33), .19, .016, m["brass"])
        for a in (30, 150, 270):
            r = math.radians(a)
            rs.strut((math.cos(r) * .12, math.sin(r) * .12), (math.cos(r) * .21, math.sin(r) * .21), .32, .025, m["steel"])


def evolve_relay(level, m):
    hot = rs.glowing_hot("EvoHot", "#c8fbff")
    ceramic = rs.material("EvoCeramic", "#e6f2f4", 0, .2, .15)
    for z, radius in ((.31, .062), (.34, .05)):  # Taller insulator stack.
        rs.cyl((0, 0, z), radius, .026, ceramic, bevel=.009)
    rs.cyl((0, 0, .368), .04, .022, hot, bevel=.007)
    if level >= 3:
        for a in (45, 135, 225, 315):  # Antenna rods.
            r = math.radians(a)
            x, y = math.cos(r) * .21, math.sin(r) * .21
            rs.cyl((x, y, .2), .014, .2, m["steel"], bevel=.003, vertices=12)
            rs.ball((x, y, .31), .022, hot)
        rs.torus((0, 0, .3), .21, .01, m["brass"])


def evolve_tesla(level, m):
    copper = rs.material("EvoCopper", "#d98a4e", 1, .3, .25)
    hot = rs.glowing_hot("EvoHot", "#f3e6ff")
    glow = rs.glowing("EvoGlow", "#b98cff", 3)
    tier_posts(level, "#b98cff", (0, 90, 180, 270), .4, .08, m)
    rs.cyl((0, 0, .32), .045, .14, m["steel"], bevel=.01)  # Central coil column.
    for i in range(4):
        rs.torus((0, 0, .28 + i * .025), .06 - i * .004, .011, copper)
    if level >= 3:
        rs.ball((0, 0, .44), .085, hot)
        rs.torus((0, 0, .44), .14, .014, m["steel"])
        for a in (45, 135, 225, 315):
            r = math.radians(a)
            rs.strut((math.cos(r) * .06, math.sin(r) * .06), (math.cos(r) * .27, math.sin(r) * .27), .38, .018, glow)
    else:
        rs.ball((0, 0, .41), .05, hot)


def evolve_wall(level, m):
    field = rs.material("EvoField", "#7fe4ff", 0, .2, 0, emission="#5fd8ff", strength=.9)
    for side in (-1, 1):  # Spikes along the front edges.
        for t in (.2, .45, .7):
            x, y = side * .46 * (1 - t), .02 + .4 * (1 - t)
            nx, ny = side * .7, .7
            rs.prism([(x + nx * .1, y + ny * .1), (x - ny * .04 * side, y + nx * .04 * side), (x + ny * .04 * side, y - nx * .04 * side)],
                     .15, .05, m["steel"], .004)
    if level >= 3:
        rs.prism([(-.36, .02), (0, .36), (.36, .02), (.33, -.01), (0, .32), (-.33, -.01)], .3, .02, field, .004)
        for side in (-1, 1):  # Shield pylons.
            rs.cyl((side * .34, -.2, .21), .04, .24, m["armor"], bevel=.008)
            rs.cyl((side * .34, -.2, .34), .028, .03, field, bevel=.004)


def evolve_repair(level, m):
    arm = rs.material("EvoArm", "#e8b24a", .6, .34, .35)
    hot = rs.glowing_hot("EvoHot", "#eaffef")
    green = rs.glowing("EvoGreen", "#6dff9c", 2.4)
    rs.cyl((.33, .33, .12), .09, .08, m["dark"], bevel=.012)  # Second arm.
    rs.strut((.33, .33), (.2, .2), .2, .07, arm)
    rs.cyl((.2, .2, .22), .03, .04, m["dark"], bevel=.006)
    rs.cyl((.2, .2, .25), .016, .02, hot, bevel=.003)
    if level >= 3:
        rs.torus((.07, .06, .13), .24, .014, green)  # Glowing pad rim and a hovering drone.
        rs.box((-.15, -.1, .42), (.12, .12, .04), arm, bevel=.012)
        for dx, dy in ((-1, -1), (1, -1), (-1, 1), (1, 1)):
            rs.cyl((-.15 + dx * .08, -.1 + dy * .08, .44), .04, .006, rs.transparent("EvoBlur", "#d8f3ff", .35), bevel=0, vertices=24)
        rs.cyl((-.15, -.1, .4), .02, .02, green, bevel=.003)


EVOLVE = {"pulse": evolve_pulse, "cryo": evolve_cryo, "generator": evolve_generator, "reactor": evolve_reactor,
          "relay": evolve_relay, "tesla": evolve_tesla, "wall": evolve_wall, "repair": evolve_repair}


# ---- Rendering ------------------------------------------------------------------------

def stage(name, level=1, phase=0.0):
    """Builds the piece and returns the straight-down framing (center x, center y, width)."""
    rs.PHASE = phase
    rs.LAYER = "base"
    rs.SCENE_SIZE = SIZE
    rs.clear()
    rs.BUILDERS[name]()
    if level > 1:
        EVOLVE[name](level, mats())
    rs.LAYER = "base"
    if name in rs.MODULE_SCALE:
        rs.scale_scene(rs.MODULE_SCALE[name])
    framing = rs.fit(name)
    if PREVIEW:
        bpy.context.scene.cycles.samples = 24
    width = framing[2]
    bpy.context.scene.camera.data.ortho_scale = width * SIZE[1] / SIZE[0]
    # The pivot sits a third of the way up, so the image center is a quarter width above the ground point.
    tilt.apply((framing[0], framing[1] + width * SIZE[1] / SIZE[0] * (1 / 2 - PIVOT)))
    return framing


def parts(layer):
    return [o for o in bpy.context.scene.objects if o.type == "MESH" and o.get("layer", "base") == layer]


def hub_lift(layer, width):
    objs = parts(layer)
    z = max(o.location.z for o in objs)  # Model height, before the tilt's stretch.
    return z * tilt.LIFT / width


def render(name):
    rs.render(name)


def module(name):
    for level in (1, 2, 3):
        framing = stage(name, level)
        rs.show_only(None)
        render(f"{name}_{level}")
        part = AIMED.get(name) or SPUN.get(name)
        if not part:
            continue
        rs.show_only("base")
        render(f"{name}_{level}_base")
        rs.show_only(part)
        if name in AIMED:
            spin = tilt.pivot(parts(part), 0)
            for d in range(DIRS):
                spin.rotation_euler = (0, 0, math.radians(d * 360 / DIRS))
                render(f"{name}_{level}_{part}_{d:02d}")
        else:
            render(f"{name}_{level}_{part}")
            meta.append(f"{name}_{level}_{part} {hub_lift(part, framing[2]):.4f}")


def enemy(name):
    count = rs.FRAMES.get(name, 4)
    part = AIMED.get(name) or SPUN.get(name)
    for frame in range(count):
        framing = stage(name, 1, frame / count)
        rs.show_only("base")
        render(f"{name}_{frame}")
        if frame or not part:
            continue
        rs.show_only(part)
        if name in AIMED:
            spin = tilt.pivot(parts(part), 0)
            for d in range(DIRS):
                spin.rotation_euler = (0, 0, math.radians(d * 360 / DIRS))
                render(f"{name}_{part}_{d:02d}")
        else:
            render(f"{name}_{part}")
            meta.append(f"{name}_{part} {hub_lift(part, framing[2]):.4f}")


for piece in MODULES + ENEMIES:
    if ONLY and piece not in ONLY:
        continue
    (module if piece in MODULES else enemy)(piece)
    with open(os.path.join(OUT, f"meta_{piece}.txt"), "w") as f:
        f.write("\n".join(meta))
    meta.clear()
print("UNITS_DONE")

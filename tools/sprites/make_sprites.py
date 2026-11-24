# /// script
# requires-python = ">=3.12"
# dependencies = ["pillow>=13", "numpy>=2.5", "scipy>=1.18"]
# ///
"""
Draws the own layers of the Hot Cargo parts (spec 1057): Computer, targeted reactor, Funnel, Tech Vault, Prototype Core.
Run: uv run tools/sprites/make_sprites.py   (rewrites mod/<area>/sprites/<part>/*.png)

Each part shows the vanilla deck and walls by reference from its rules (never copied). This script draws only what makes
the part itself: the machine on the deck, animated doodads, lights, the roof with its normals and glows, the blueprint
and the editor icon. Workflow and limits: docs/part-art.md. Shapes and shading: kit.py.
"""
import math
import pathlib
import random

import numpy as np
from PIL import Image
from scipy import ndimage

from kit import (BLACK, COPPER, DARK, GUNMETAL, HAZARD, STEEL, TILE, Out, Sprite, blueprint, damaged_normals, deck,
                 frame, glow, lights)

MOD = pathlib.Path(__file__).resolve().parents[2] / "mod"
ROOT = MOD / "autonomy" / "sprites"
POWER = (180, 255, 77)  # the vanilla reactor glow (reactor_small roof light VertexColor)
SCREEN = (70, 160, 255)
INTAKE = (255, 170, 60)


def roof_layers(out, roof, seed):
    """roof, roof_normals and their damage levels; the normals follow the damage holes."""
    colors = out.save_damaged("roof", roof.roof(seed), seed)
    nrm = roof.normals()
    for suffix, c in zip(("", "_33", "_66"), colors):
        out.save("roof_normals" + suffix, damaged_normals(nrm, c))


def icon(out, w, h, machine, doodad):
    """The editor icon: own deck and frame (no vanilla pixels), the machine, and its light added, as vanilla icons look lit."""
    img = deck(w, h)
    for layer in (machine, doodad, frame(w, h)):
        img.alpha_composite(layer)
    lit = np.asarray(img).astype(np.int16)
    lit[..., :3] += np.asarray(Image.open(out.dir / "lights.png"))[..., :3] * 3 // 2
    out.save("icon", Image.fromarray(np.clip(lit, 0, 255).astype(np.uint8), "RGBA"))


SIDES = ((0, -1), (1, 0), (0, 1), (-1, 0))


def along(c, side, u0, u1, v0, v1):
    """A box from u0 to u1 out from the centre c toward side, and from v0 to v1 across it: x0, y0, x1, y1."""
    dx, dy = side
    xs = [c + dx * u - dy * v for u in (u0, u1) for v in (v0, v1)]
    ys = [c + dy * u + dx * v for u in (u0, u1) for v in (v0, v1)]
    return min(xs), min(ys), max(xs), max(ys)


def side_points(cx, cy, dist):
    """The four directions: (dx, dy) and the point at dist from the centre."""
    return [((dx, dy), (cx + dx * dist, cy + dy * dist)) for dx, dy in SIDES]


# ---- Targeted reactor, 2x2: a sealed core with a power coupler on each side. It feeds only the parts that touch it.

def targeted_reactor():
    out = Out(ROOT, "targeted_reactor")
    m = Sprite(2, 2)
    c = 64
    # Housing: an octagonal plate with seams and greebles in its corners.
    m.put(m.d_rect(12, 12, 116, 116, chamfer=22), 6, GUNMETAL, bevel=2)
    m.cut(np.maximum(m.d_rect(16, 16, 112, 112, chamfer=20), -m.d_rect(17, 17, 111, 111, chamfer=19)), 2)
    # Conduits from the core to each side, ending in a coupler block against the wall.
    for side in SIDES:
        m.put(m.d_rect(*along(c, side, 20, 50, -8, 8)), 9, DARK, bevel=2)
        m.slots(*along(c, side, 32, 48, -6, 6), 3, 2, vertical=bool(side[0]))
        m.put(m.d_rect(*along(c, side, 44, 56, -13, 13), round_=2), 12, STEEL, bevel=2)
        m.put(m.d_rect(*along(c, side, 47, 53, -13, 13)), 12, COPPER, bevel=1)
    # Capacitor banks in the corners, cabled to the core.
    for sx, sy in ((-1, -1), (1, -1), (1, 1), (-1, 1)):
        tx, ty = c + sx * 33, c + sy * 33
        m.pipe(c + sx * 20, c + sy * 20, tx, ty, 2, 8, COPPER)
        m.put(m.d_rect(tx - 9, ty - 9, tx + 9, ty + 9, chamfer=4), 9, DARK, bevel=2)
        for k in (-4, 0, 4):
            m.cylinder(tx + k - 1.5, ty - 7, tx + k + 1.5, ty + 7, 10, 2, STEEL, vertical=True)
    m.greebles(14, 14, 114, 114, 24, 6, density=0.8, avoid=(m.height != 6))
    # The core: a bolted ring around a vessel with a window.
    m.put(m.d_circle(c, c, 30), 12, STEEL, bevel=3)
    m.cut(m.d_ring(c, c, 22, 24), 3)
    m.rivets([(c + 27 * math.cos(a), c + 27 * math.sin(a)) for a in np.linspace(0, 2 * math.pi, 16, endpoint=False)], 12)
    m.put(m.d_circle(c, c, 21), 10, DARK, bevel=2)
    m.dome(c, c, 14, 10, 5, ((40, 58, 30), 0.9))
    machine = out.save_damaged("machine", m.color(21), 21, holes="burnt")[0]

    # Doodads: the pulsing core and the coupler lights, which chase around the four sides.
    frames = []
    for f in range(4):
        d = Sprite(2, 2)
        pulse = 0.75 + 0.25 * math.sin(f * math.pi / 2)
        core = tuple(int(v * pulse) for v in POWER)
        d.put(d.d_circle(c, c, 9), 0, core, emit=True)
        d.put(d.d_circle(c - 3, c - 3, 3), 0, (240, 255, 210), emit=True)
        for k, ((dx, dy), (x, y)) in enumerate(side_points(c, c, 50)):
            lit = (f + k) % 4 != 0
            col = POWER if lit else (60, 80, 30)
            px, py = (x - dx * 9, y - dy * 9)
            d.put(d.d_circle(px, py, 2.2), 0, col, emit=True)
        frames.append(out.save(f"doodads_on{f + 1}", d.color()))
    off = Sprite(2, 2)
    off.put(off.d_circle(c, c, 9), 0, (40, 52, 30), emit=True)
    out.save("doodads_off", off.color())

    # Vanilla power parts glow yellow-green inside; this one too, with the light pooled at the core and the couplers.
    spots = [(m.d_circle(c, c, 26) < 0, (120, 170, 50))]
    spots += [(m.d_circle(x, y, 8) < 0, (90, 130, 36)) for _, (x, y) in side_points(c, c, 48)]
    out.save("lights", lights(spots, (m.h, m.w), blur=12))

    # Roof: a raised ring over the core with a round window, and an arrow hole toward each side.
    r = Sprite(2, 2)
    r.put(r.d_rect(0, 0, 128, 128), 8, (0, 0, 0), paint=True)
    r.put(r.d_circle(c, c, 36), 13, (0, 0, 0), bevel=3, paint=True)
    r.cut(r.d_ring(c, c, 27, 29), 3)
    r.put(r.d_circle(c, c, 22), 10, (0, 0, 0), bevel=2, paint=True)
    holes = r.d_circle(c, c, 12) < 0
    for (dx, dy), (x, y) in side_points(c, c, 50):
        # Arrow pointing out: tip at the side, tail toward the core.
        def at(u, v):
            return (x + dx * u - dy * v, y + dy * u + dx * v)
        arrow = [at(6, 0), at(-2, 8), at(-2, 4), at(-9, 4), at(-9, -4), at(-2, -4), at(-2, -8)]
        plate = r.d_poly([at(9, 0), at(-4, 12), at(-12, 12), at(-12, -12), at(-4, -12)])
        r.put(plate, 11, (0, 0, 0), bevel=2, paint=True)
        holes |= r.d_poly(arrow) < 0
    for sx, sy in ((0, 0), (1, 0), (0, 1), (1, 1)):
        x0, y0 = 6 + sx * 80, 6 + sy * 80
        r.put(r.d_rect(x0, y0, x0 + 36, y0 + 36, chamfer=10), 10, (0, 0, 0), bevel=2, paint=True)
    avoid = (r.d_circle(c, c, 40) < 0) | ndimage.binary_dilation(holes, iterations=14)
    for qx, qy in ((0, 0), (64, 0), (0, 64), (64, 64)):
        r.greebles(qx + 8, qy + 8, qx + 56, qy + 56, 23 + qx + qy, 8, density=1.2, avoid=avoid)
    r.rivets([(8, 8), (120, 8), (8, 120), (120, 120)], 8)
    r.hole(np.where(holes, -1.0, 1.0))
    roof_layers(out, r, 23)
    out.save("roof_glows", glow(holes, POWER, radius=5, strength=1.3))

    out.save("blueprints", blueprint(m.mask() & ~(m.d_circle(c, c, 21) < 0) | (m.d_circle(c, c, 14) < 0)))
    icon(out, 2, 2, machine, frames[0])


# ---- Funnel, 2x2: a stepped intake that narrows to a throat with turning vanes; a chute on each side. No crew.

def funnel():
    out = Out(ROOT, "funnel")
    m = Sprite(2, 2)
    c = 64
    # Output chutes toward each side, under the rim.
    for (dx, dy), (x, y) in side_points(c, c, 52):
        m.put(m.d_rect(x - 9, y - 9, x + 9, y + 9), 7, GUNMETAL, bevel=2)
        m.slots(x - 7, y - 7, x + 7, y + 7, 3, 2, vertical=bool(dy))
    # Corner brackets.
    for sx, sy in ((0, 0), (1, 0), (0, 1), (1, 1)):
        x0, y0 = 8 + sx * 92, 8 + sy * 92
        m.put(m.d_rect(x0, y0, x0 + 20, y0 + 20, chamfer=8), 6, DARK, bevel=2)
        m.rivets([(x0 + 5, y0 + 5), (x0 + 15, y0 + 15)], 6)
    # The rim: a raised ring with hazard stripes.
    rim = m.d_ring(c, c, 46, 56)
    m.put(rim, 12, STEEL, bevel=2)
    ang = np.degrees(np.arctan2(m.Y - c, m.X - c)) % 20 < 10
    band = m.d_ring(c, c, 50, 53) < 0
    m.base[band & ang] = HAZARD[0]
    m.base[band & ~ang] = (34, 32, 30)
    m.cut(m.d_ring(c, c, 54, 55), 2)
    # Steps down into the throat.
    for r0, r1, z, mat in ((36, 46, 8, GUNMETAL), (26, 36, 4, DARK), (16, 26, 0, DARK)):
        m.put(m.d_ring(c, c, r0, r1), z, mat, bevel=2)
        m.rivets([(c + (r1 - 3) * math.cos(a), c + (r1 - 3) * math.sin(a))
                  for a in np.linspace(0, 2 * math.pi, 12, endpoint=False)], z)
    m.put(m.d_circle(c, c, 16), -4, BLACK)
    machine = out.save_damaged("machine", m.color(31), 31, holes="burnt")[0]

    # Vanes: a separate sprite that the rules turn while the Funnel works.
    v = Sprite(60 / TILE, 60 / TILE)
    vc = 30
    for k in range(6):
        a = k * math.pi / 3
        pts = [(vc + math.cos(a) * 8, vc + math.sin(a) * 8), (vc + math.cos(a + 0.55) * 28, vc + math.sin(a + 0.55) * 28),
               (vc + math.cos(a + 0.85) * 26, vc + math.sin(a + 0.85) * 26), (vc + math.cos(a + 0.4) * 8, vc + math.sin(a + 0.4) * 8)]
        v.put(v.d_poly(pts), 6, STEEL, bevel=2)
    v.dome(vc, vc, 8, 6, 4, GUNMETAL)
    v.put(v.d_circle(vc, vc, 2), 12, BLACK)
    out.save("vanes", v.color(32))

    out.save("lights", lights([(m.d_ring(c, c, 16, 30) < 0, (60, 36, 10))], (m.h, m.w), blur=10))

    # Roof: paint with the round opening of the intake, a hazard collar around it and a hatch over each chute.
    r = Sprite(2, 2)
    r.put(r.d_rect(0, 0, 128, 128), 8, (0, 0, 0), paint=True)
    r.put(r.d_circle(c, c, 54), 12, (0, 0, 0), bevel=3, paint=True)
    collar = r.d_ring(c, c, 44, 47)
    r.put(collar, 13, HAZARD, bevel=1)
    r.base[(collar < 0) & ~ang] = (34, 32, 30)
    for (dx, dy), (x, y) in side_points(c, c, 56):
        r.put(r.d_rect(x - 8, y - 8, x + 8, y + 8, round_=2), 10, (0, 0, 0), bevel=2, paint=True)
        r.slots(x - 6, y - 6, x + 6, y + 6, 3, 1.5, vertical=bool(dy))
    avoid = r.d_circle(c, c, 58) < 0
    for qx, qy in ((0, 0), (64, 0), (0, 64), (64, 64)):
        r.greebles(qx + 4, qy + 4, qx + 60, qy + 60, 33 + qx + qy, 8, density=1.4, avoid=avoid)
    r.rivets([(8, 8), (120, 8), (8, 120), (120, 120)], 8)
    holes = r.d_circle(c, c, 44) < 0
    r.hole(np.where(holes, -1.0, 1.0))
    roof_layers(out, r, 33)
    out.save("roof_glows", glow(holes, INTAKE, radius=3, strength=0.35))

    out.save("blueprints", blueprint(m.mask()))
    vanes = Image.new("RGBA", (128, 128))
    vanes.alpha_composite(Image.open(out.dir / "vanes.png"), (c - 30, c - 30))
    icon(out, 2, 2, machine, vanes)


# ---- Computer, 2x1: two server racks and a screen strip at the front (top edge at rotation 0), the side it serves.

def computer():
    out = Out(ROOT, "computer")
    m = Sprite(2, 1)
    # Cabinet with two rack bays, a cable trunk between them.
    m.put(m.d_rect(8, 20, 120, 58, round_=2), 8, GUNMETAL, bevel=2)
    for x0 in (12, 70):
        m.put(m.d_rect(x0, 23, x0 + 46, 55), 4, DARK, bevel=1)
        for i in range(6):
            bx = x0 + 2 + i * 7.3
            m.put(m.d_rect(bx, 25, bx + 6, 53, round_=1), 9, STEEL, bevel=1.5)
            m.slots(bx + 1, 28, bx + 5, 44, 2, 1.2)
    for x in (60, 63.5, 67):
        m.pipe(x, 22, x, 56, 1.6, 9, ((120, 80, 56), 0.4))
    # Console at the front: bezel and a dark screen.
    m.put(m.d_rect(10, 6, 118, 19, round_=2), 7, DARK, bevel=2)
    m.put(m.d_rect(14, 9, 114, 16), 5, ((16, 24, 38), 0.8))
    machine = out.save_damaged("machine", m.color(11), 11, holes="burnt")[0]

    frames = []
    for f in range(2):
        d = Sprite(2, 1)
        rng = random.Random(100 + f)
        d.put(d.d_rect(15, 10, 113, 15), 0, (30, 80, 140), emit=True)
        x = 17
        while x < 108:
            w = rng.randint(3, 12)
            d.put(d.d_rect(x, 11, min(x + w, 111), 14 if rng.random() < 0.6 else 12), 0, (150, 215, 255), emit=True)
            x += w + rng.randint(2, 5)
        for x0 in (12, 70):
            for i in range(6):
                bx = x0 + 2 + i * 7.3
                for k in range(2):
                    col = (90, 255, 140) if rng.random() < 0.65 else (255, 190, 60) if rng.random() < 0.3 else (30, 50, 36)
                    d.put(d.d_rect(bx + 1 + k * 3, 48, bx + 2 + k * 3, 50), 0, col, emit=True)
        frames.append(out.save(f"doodads_on{f + 1}", d.color()))
    off = Sprite(2, 1)
    off.put(off.d_rect(15, 10, 113, 15), 0, (22, 30, 42), emit=True)
    out.save("doodads_off", off.color())

    out.save("lights", lights([(m.d_rect(14, 9, 114, 16) < 0, (30, 70, 130))], (m.h, m.w), blur=6))

    # Roof: a window strip at the front shows the screen glow; vents over the racks; a sensor dome in the middle.
    r = Sprite(2, 1)
    r.put(r.d_rect(0, 0, 128, 64), 8, (0, 0, 0), paint=True)
    r.put(r.d_rect(8, 4, 120, 21, round_=3), 11, (0, 0, 0), bevel=2, paint=True)
    for x0 in (12, 76):
        r.put(r.d_rect(x0, 26, x0 + 40, 56, round_=2), 11, (0, 0, 0), bevel=2, paint=True)
        r.slots(x0 + 4, 30, x0 + 36, 52, 4, 2)
    r.put(r.d_ring(64, 40, 8, 10), 10, (0, 0, 0), bevel=1, paint=True)
    r.dome(64, 40, 8, 9, 5, (0, 0, 0), paint=True)
    r.rivets([(5, 5), (123, 5), (5, 59), (123, 59)], 8)
    holes = r.d_rect(16, 9, 112, 16, round_=1) < 0
    r.hole(np.where(holes, -1.0, 1.0))
    roof_layers(out, r, 13)
    out.save("roof_glows", glow(holes, SCREEN, radius=3, strength=0.6))

    out.save("blueprints", blueprint(m.mask()))
    icon(out, 2, 1, machine, frames[0])


# ---- Tech Vault, 1x1: an armoured bed for 4 units of tech, a heavy frame with locking bolts and mint status lights.
# No roof: the tech shows (spec 1540). The units draw on doodads_low, inset 8 px, so the 4 pads sit under the stack slots.

MINT = (140, 255, 200)  # the Stolen Tech glyph colour (make_items.py)
VAULT_WALL = ((84, 80, 76), 0.25)


def tech_vault():
    out = Out(MOD / "tech" / "sprites", "tech_vault")
    f = Sprite(1, 1)
    f.put(f.d_rect(0, 0, 64, 64), 4, DARK)  # under the walls: a hole in a wall shows a dark breach
    f.put(f.d_rect(5, 5, 59, 59, chamfer=6), 6, GUNMETAL, bevel=2)
    f.put(f.d_rect(8, 8, 56, 56, chamfer=4), 2, DARK, bevel=1)
    for x0, y0 in ((10, 10), (32.5, 10), (10, 32.5), (32.5, 32.5)):  # under the stack slots (8 + 0.75 x vanilla slot)
        f.put(f.d_rect(x0, y0, x0 + 21.5, y0 + 21.5, round_=1.5), 3.5, ((112, 118, 122), 0.35), bevel=1)
        f.slots(x0 + 3, y0 + 3, x0 + 18.5, y0 + 18.5, 3, 1)
    floor = out.save_damaged("floor", f.color(41), 41, holes="burnt")[0]

    w = Sprite(1, 1)
    w.put(np.maximum(w.d_rect(0, 0, 64, 64), -w.d_rect(5, 5, 59, 59, chamfer=6)), 9, VAULT_WALL, bevel=2)
    w.cut(np.maximum(w.d_rect(1.5, 1.5, 62.5, 62.5), -w.d_rect(2.5, 2.5, 61.5, 61.5)), 2)
    for (dx, dy), (x, y) in side_points(32, 32, 30):  # locking bolts, one in each wall
        w.put(w.d_rect(*along(32, (dx, dy), 26, 31.5, -5, 5), round_=1), 11, STEEL, bevel=1)
        w.cut(w.d_rect(*along(32, (dx, dy), 27.5, 30, -0.6, 0.6)), 2)
    for sx, sy in ((0, 0), (1, 0), (0, 1), (1, 1)):  # mint status lights in the corners
        x, y = 4.5 + sx * 55, 4.5 + sy * 55
        w.put(w.d_circle(x, y, 2.6), 10, BLACK)
        w.put(w.d_circle(x, y, 1.6), 10, MINT, emit=True)
    walls = out.save_damaged("walls", w.color(42), 42)[0]
    corners = np.zeros((64, 64), bool)
    for sx, sy in ((0, 0), (1, 0), (0, 1), (1, 1)):
        corners |= w.d_circle(4.5 + sx * 55, 4.5 + sy * 55, 2) < 0
    out.save("lights", lights([(corners, (50, 110, 80))], (64, 64), blur=3))

    out.save("blueprints", blueprint((f.d_rect(5, 5, 59, 59, chamfer=6) < 0)))
    # The editor icon shows the vault full: the four tech crates of make_items.py on the bed.
    img = floor.copy()
    crates = Image.open(MOD / "tech" / "sprites" / "stolen_tech" / "stack_4.png").resize((48, 48), Image.LANCZOS)
    img.alpha_composite(crates, (8, 8))
    img.alpha_composite(walls)
    out.save("icon", img)


# ---- Prototype Core, 2x2: the containment unit that a Prototype Retrieval carrier holds. The same blue core and hazard
# corners as the cargo item (make_items.py), so a player who sees the part knows the item. No roof: the core shows.

CORE = (70, 170, 255)


def prototype_core():
    out = Out(MOD / "missions" / "mechanics" / "sprites", "prototype_core")
    m = Sprite(2, 2)
    c = 64
    m.put(m.d_rect(10, 10, 118, 118, chamfer=26), 6, GUNMETAL, bevel=2)
    m.cut(np.maximum(m.d_rect(14, 14, 114, 114, chamfer=24), -m.d_rect(15, 15, 113, 113, chamfer=23)), 2)
    for sx, sy in ((-1, -1), (1, -1), (1, 1), (-1, 1)):
        # Hazard pad in each corner, with black stripes across it.
        pad = m.d_poly([(c + sx * 54, c + sy * 30), (c + sx * 54, c + sy * 38), (c + sx * 38, c + sy * 54),
                        (c + sx * 30, c + sy * 54)])
        inside = m.put(pad, 9, HAZARD, bevel=1)
        stripe = np.mod(sx * m.X + sy * m.Y, 8) < 4
        m.base[inside & stripe] = (34, 32, 30)
        # Coolant line from the ring to the pad.
        m.pipe(c + sx * 24, c + sy * 24, c + sx * 36, c + sy * 36, 2.4, 8, ((70, 110, 160), 0.5))
    m.greebles(14, 14, 114, 114, 51, 6, density=0.7, avoid=(m.height != 6))
    for side in SIDES:  # clamps that hold the core
        m.put(m.d_rect(*along(c, side, 26, 50, -7, 7), round_=1.5), 11, STEEL, bevel=2)
        m.slots(*along(c, side, 36, 48, -5, 5), 3, 1.5, vertical=bool(side[0]))
        m.put(m.d_rect(*along(c, side, 50, 54, -9, 9)), 9, DARK, bevel=1)
    m.put(m.d_circle(c, c, 30), 12, STEEL, bevel=3)
    m.cut(m.d_ring(c, c, 25, 26.5), 3)
    m.rivets([(c + 28 * math.cos(a), c + 28 * math.sin(a)) for a in np.linspace(0, 2 * math.pi, 16, endpoint=False)], 12)
    m.put(m.d_circle(c, c, 23), 3, BLACK)
    m.dome(c, c, 18, 3, 6, ((24, 40, 60), 0.9))  # the dark core glass; the doodads light it
    machine = out.save_damaged("machine", m.color(51), 51, holes="burnt")[0]

    frames = []
    for f in range(4):  # a slow pulse, as the cargo item
        pulse = (1.0, 0.86, 0.72, 0.86)[f]
        d = Sprite(2, 2)
        d.dome(c, c, 18, 0, 6, tuple(int(v * pulse) for v in CORE), emit=True)
        d.dome(c - 2, c - 2, 11, 1, 6, tuple(int(v * (0.6 + 0.4 * pulse)) for v in (170, 228, 255)), emit=True)
        d.dome(c - 4, c - 4, 4.5, 2, 3, (250, 254, 255), emit=True)
        for k, ((dx, dy), (x, y)) in enumerate(side_points(c, c, 44)):
            d.put(d.d_circle(x, y, 1.8), 0, CORE if (f + k) % 2 == 0 else (30, 60, 90), emit=True)
        frames.append(out.save(f"doodads_on{f + 1}", d.color()))

    out.save("lights", lights([(m.d_circle(c, c, 22) < 0, (40, 100, 170))], (128, 128), blur=14))
    out.save("blueprints", blueprint(m.mask()))
    icon(out, 2, 2, machine, frames[0])


if __name__ == "__main__":
    targeted_reactor()
    funnel()
    computer()
    tech_vault()
    prototype_core()
    print(f"sprites written to {MOD}/<area>/sprites")

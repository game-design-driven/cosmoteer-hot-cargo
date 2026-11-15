# /// script
# requires-python = ">=3.12"
# dependencies = ["pillow>=13", "numpy>=2.5", "scipy>=1.18"]
# ///
"""
Draws the own layers of the Hot Cargo parts (spec 1057): Computer, targeted reactor, Funnel.
Run: uv run tools/sprites/make_sprites.py   (rewrites mod/autonomy/sprites/<part>/*.png)

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

ROOT = pathlib.Path(__file__).resolve().parents[2] / "mod" / "autonomy" / "sprites"
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
    img = deck(w, h)
    for layer in (machine, doodad, frame(w, h)):
        img.alpha_composite(layer)
    out.save("icon", img)


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

    spots = [(m.d_circle(c, c, 14) < 0, (70, 100, 30))]
    spots += [(m.d_circle(x, y, 6) < 0, (50, 72, 22)) for _, (x, y) in side_points(c, c, 46)]
    out.save("lights", lights(spots, (m.h, m.w), blur=8))

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
    out.save("roof_glows", glow(holes, POWER, radius=4, strength=0.8))

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


if __name__ == "__main__":
    targeted_reactor()
    funnel()
    computer()
    print(f"sprites written to {ROOT}")

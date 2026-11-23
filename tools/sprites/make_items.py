# /// script
# requires-python = ">=3.12"
# dependencies = ["pillow>=13", "numpy>=2.5", "scipy>=1.18"]
# ///
"""
Draws the Hot Cargo items (resources): icon, overlay, nugget with normals, and stack sprites, plus a sprites.rules that
the item's rules reference. Spec 1057. Run: uv run tools/sprites/make_items.py

Each item is one design function in a 28 x 28 unit box (x and y from -14 to 14), drawn at three scales:
the nugget and stacks (1 unit = 1 px, like a vanilla processor), the icon (1.45 px) and the overlay (3 px).
Output goes next to the item's rules: <rules folder>/sprites/<item>/. The rules file is found by its ID line.

Vanilla look, measured from the vanilla resources (no art copied): the object is 28 px in a 64 px tile, with a dark rim,
light from the top left, and a soft black shadow of about 4 px on all sides. Normals: R right, G up, flat = 128,128,255.
Stacks put one object in each 28 px quarter (4,4), (32,4), (4,32), (32,32), then one in the middle. Overlays are white
line art with a soft glow on a 128 px canvas. Glowing resources (uranium) add a coloured halo.
"""
import math
import pathlib
import random

import numpy as np
from PIL import Image
from scipy import ndimage

from kit import BLACK, DARK, GUNMETAL, HAZARD, STEEL, Sprite

REPO = pathlib.Path(__file__).resolve().parents[2]
MOD = REPO / "mod"
ID_PREFIX = "yarden_zamir."

NUGGET, ICON, OVERLAY = 1.0, 1.45, 3.0
STACK_SLOTS = ((4, 4), (32, 4), (4, 32), (32, 32), (18, 18))  # top-left corner of each 28 px object, in fill order

ORANGE = ((236, 108, 32), 0.35)
WHITE = ((214, 216, 220), 0.45)
ALU = ((172, 176, 184), 0.6)
RED = ((178, 36, 36), 0.25)
PURPLE = ((112, 64, 168), 0.5)
GOLD = ((218, 172, 72), 0.8)
CRATE = ((86, 98, 82), 0.3)
BASALT = ((60, 56, 60), 0.25)


class Pen:
    """A kit Sprite with design units: (0, 0) is the centre of the canvas, 1 unit = u px. Heights scale with u."""

    def __init__(self, size, u):
        self.s = Sprite(size / 64, size / 64)
        self.c, self.u = size / 2, u
        self.glow = np.zeros((self.s.h, self.s.w, 3), np.float32)

    def p(self, v):
        return self.c + v * self.u

    def rect(self, x0, y0, x1, y1, round_=0.0, chamfer=0.0):
        return self.s.d_rect(self.p(x0), self.p(y0), self.p(x1), self.p(y1), chamfer * self.u, round_ * self.u)

    def circle(self, x, y, r):
        return self.s.d_circle(self.p(x), self.p(y), r * self.u)

    def ring(self, x, y, r0, r1):
        return np.maximum(self.circle(x, y, r1), -self.circle(x, y, r0))

    def segment(self, x0, y0, x1, y1, r):
        return self.s.d_segment(self.p(x0), self.p(y0), self.p(x1), self.p(y1), r * self.u)

    def poly(self, points):
        return self.s.d_poly([(self.p(x), self.p(y)) for x, y in points])

    def ngon(self, x, y, r, n, turn=0.0):
        return self.poly([(x + r * math.cos(turn + 2 * math.pi * k / n), y + r * math.sin(turn + 2 * math.pi * k / n))
                          for k in range(n)])

    def put(self, d, z, mat, bevel=0.0, glow=0.0, **kw):
        """Like Sprite.put. glow > 0 makes the shape emissive and adds a halo of its colour with that strength."""
        inside = self.s.put(d, z * self.u, mat, bevel=bevel * self.u, emit=glow > 0 or kw.pop("emit", False), **kw)
        if glow:
            self.glow[inside] = np.array(mat if isinstance(mat[0], int) else mat[0], np.float32) * glow
        return inside

    def cut(self, d, depth, bevel=0.0):
        self.s.cut(d, depth * self.u, bevel * self.u)

    def dome(self, x, y, r, z, h, mat, glow=0.0):
        rr = np.clip(1 - (np.hypot(self.s.X - self.p(x), self.s.Y - self.p(y)) / (r * self.u)) ** 2, 0, 1)
        return self.put(self.circle(x, y, r), z, mat, glow=glow, profile=h * self.u * np.sqrt(rr))

    def pipe(self, x0, y0, x1, y1, r, z, mat):
        d = self.segment(x0, y0, x1, y1, r)
        ru = r * self.u
        return self.put(d, z, mat, profile=ru * np.sqrt(np.clip(1 - ((d + ru) / ru) ** 2, 0, 1)))

    def grid(self, d_area, pitch, depth, dot=0.5):
        """Recessed dots in a grid inside the area (foam, contacts)."""
        px = pitch * self.u
        gx = np.abs(np.mod(self.s.X - self.c, px) - px / 2)
        gy = np.abs(np.mod(self.s.Y - self.c, px) - px / 2)
        self.s.height[(np.maximum(gx, gy) < px * dot / 2) & (d_area < 0)] -= depth * self.u

    # ---- output

    def sprite(self):
        """Shaded RGBA with a dark rim, a soft black shadow and the glow halo, like the vanilla resources."""
        img = np.asarray(self.s.color(7)).astype(np.float32) / 255
        mask = self.s.cover > 0.5
        rim = mask & ~ndimage.binary_erosion(mask)
        img[rim & ~self.s.emit, :3] *= 0.42
        shadow = np.clip(ndimage.gaussian_filter(ndimage.binary_dilation(mask).astype(np.float32), 1.6) * 1.25, 0, 0.85)
        a = img[..., 3] + shadow * (1 - img[..., 3])
        rgb = img[..., :3] * img[..., 3:]  # premultiplied, over a black shadow
        if self.glow.any():
            sigma = 2.2 * self.u
            halo = np.dstack([ndimage.gaussian_filter(self.glow[..., k], sigma) for k in range(3)]) / 255 * 1.6
            rgb += halo
            a = np.maximum(a, np.clip(halo.max(axis=2), 0, 1))
        out = np.dstack([np.clip(rgb / np.maximum(a, 1e-4)[..., None], 0, 1), np.clip(a, 0, 1)]) * 255
        return Image.fromarray(out.astype(np.uint8), "RGBA")

    def normals(self):
        return self.s.normals()

    def overlay(self):
        """White line art of the shape: its outline and its deep grooves, with a soft glow."""
        h, mask = self.s.height, self.s.cover > 0.5
        outline = mask & ~ndimage.binary_erosion(mask, iterations=3)
        grooves = (ndimage.maximum_filter(h, size=3) - h > 1.6 * self.u) & mask
        lit = self.glow.max(axis=2) > 0
        lit_edge = lit & ~ndimage.binary_erosion(lit, iterations=2)
        lines = (outline | ndimage.binary_dilation(grooves) | lit_edge).astype(np.float32)
        lines = ndimage.gaussian_filter(lines, 0.6)
        a = np.clip(np.maximum(lines * 1.3, ndimage.gaussian_filter(lines, 5) * 1.4), 0, 1)
        return Image.fromarray(np.dstack([np.full(a.shape + (3,), 255.0), a[..., None] * 255]).astype(np.uint8), "RGBA")


# ---- designs. Each takes a Pen and a frame index; frame 0 is the lit frame (icon and normals).

def flight_recorder(g, f):
    """International-orange black box: rubber end caps, reflective bands, carry handle, a red lamp that blinks."""
    g.put(g.rect(-10, -12, 10, 12, round_=2.5), 6, ORANGE, bevel=2)
    for y0, y1 in ((-13.5, -8), (8, 13.5)):
        g.put(g.rect(-11, y0, 11, y1, round_=3), 7.5, BLACK, bevel=2)
        g.cut(g.rect(-8, (y0 + y1) / 2 - 0.5, 8, (y0 + y1) / 2 + 0.5), 1.5)
    for y in (-6.5, 5):
        g.put(g.rect(-10, y, 10, y + 1.5), 6, WHITE)
    g.pipe(-5, -11, 5, -11, 1.2, 9, STEEL)
    for y in (-3.5, 2.5):  # stencil text
        g.cut(g.rect(-8, y, -5.5, y + 1), 1.2)
        g.cut(g.rect(5.5, y, 8, y + 1), 1.2)
    g.put(g.circle(0, -0.5, 4.2), 8, DARK, bevel=1)
    lit = f == 0
    g.dome(0, -0.5, 2.8, 8, 2, (255, 80, 60) if lit else (96, 24, 20), glow=1.0 if lit else 0.0)


def case_body(g, y0, y1, z):
    g.put(g.rect(-13, y0, 13, y1, round_=2.5), z, ALU, bevel=2)
    g.cut(np.maximum(g.rect(-11.5, y0 + 1.5, 11.5, y1 - 1.5, round_=1.5), -g.rect(-11, y0 + 2, 11, y1 - 2, round_=1)), 1)


def sealed_case(g, f):
    """Aluminium courier case, shut: handle, latches, a red seal band and a lock with a red light."""
    case_body(g, -8, 10, 6)
    for y in (-3.5, 5.5):
        g.cut(g.rect(-11, y, 11, y + 0.8), 1.3)
    g.put(g.rect(-2.5, -8, 2.5, 10), 6.5, RED)
    for x in (-9, 9):
        g.put(g.rect(x - 1.5, -9, x + 1.5, -5, round_=0.5), 8, DARK, bevel=1)
    for x in (-6, 4):
        g.put(g.rect(x, -10.5, x + 2, -7.5), 8, GUNMETAL, bevel=0.5)
    g.pipe(-4.5, -11, 4.5, -11, 1.3, 9, BLACK)
    g.put(g.rect(-3.5, -1, 3.5, 5, round_=1), 8.5, GUNMETAL, bevel=1)
    g.cut(g.rect(-0.4, 2.5, 0.4, 4.2), 1.5)
    g.dome(0, 0.9, 1.2, 8.5, 0.8, (255, 60, 50), glow=0.6)


def opened_case(g, f):
    """The same case, open flat: lid lining above, empty foam below with the cut-out its contents left. Seal torn."""
    case_body(g, -14, -1.5, 4.5)  # lid, lying open
    g.put(g.rect(-11.5, -12.5, 11.5, -3, round_=1), 3, ((76, 44, 48), 0.15))
    g.grid(g.rect(-11.5, -12.5, 11.5, -3), 2.2, 0.8)
    case_body(g, -0.5, 13, 6)  # base
    g.put(g.rect(-11.5, 1, 11.5, 11.5, round_=1), 3.5, ((62, 62, 70), 0.15))
    g.grid(g.rect(-11.5, 1, 11.5, 11.5), 2.2, 0.8)
    g.put(g.rect(-6, 3.5, 6, 9, round_=1), 0.8, BLACK)
    g.put(g.rect(-10, -1.8, 10, -0.2), 7, GUNMETAL, bevel=0.5)
    g.put(g.rect(-2.5, -14, 2.5, -12.5), 5, RED)
    g.put(g.poly([(-2.5, 11.5), (2.5, 11.5), (2.5, 13), (0.5, 12.2), (-1, 13.3), (-2.5, 12.6)]), 6.5, RED)


def prototype_core_cargo(g, f):
    """A glowing core held in a heavy frame by four clamps, hazard pads on the corners. The core pulses."""
    g.put(np.maximum(g.rect(-13, -13, 13, 13, chamfer=4), -g.circle(0, 0, 8.6)), 7, GUNMETAL, bevel=2)
    for sx in (-1, 1):
        for sy in (-1, 1):  # hazard pad: a triangle in each corner
            g.put(g.poly([(sx * 12.2, sy * 6.5), (sx * 12.2, sy * 8.6), (sx * 8.6, sy * 12.2), (sx * 6.5, sy * 12.2),
                          (sx * 9.5, sy * 9.5)]), 8, HAZARD, bevel=0.8)
    g.put(g.circle(0, 0, 8.6), 2.5, BLACK)
    g.put(g.ring(0, 0, 6.6, 8), 6, STEEL, bevel=1)
    for dx, dy in ((1, 0), (0, 1), (-1, 0), (0, -1)):  # clamps from the ring to the frame
        g.put(g.rect(min(dx * 6.5, dx * 10.5) - 1.3 * (dx == 0), min(dy * 6.5, dy * 10.5) - 1.3 * (dy == 0),
                     max(dx * 6.5, dx * 10.5) + 1.3 * (dx == 0), max(dy * 6.5, dy * 10.5) + 1.3 * (dy == 0)),
              8.5, STEEL, bevel=1)
    pulse = (1.0, 0.85, 0.7, 0.85)[f]
    g.dome(0, 0, 5.6, 4, 3, tuple(int(c * pulse) for c in (70, 170, 255)), glow=1.0 * pulse)
    g.dome(-0.6, -0.6, 3.6, 5, 3, tuple(int(c * (0.6 + 0.4 * pulse)) for c in (170, 228, 255)), glow=0.5 * pulse)
    g.dome(-1, -1, 1.6, 6, 2, (250, 254, 255), glow=0.3)


def rescue_beacon(g, f):
    """White distress puck with an orange livery ring, a whip antenna and a green lamp that blinks slowly."""
    g.put(g.circle(0, 1, 11), 5, WHITE, bevel=3)
    ang = np.mod(np.arctan2(g.s.Y - g.p(1), g.s.X - g.p(0)), math.pi / 2) < math.pi / 4
    band = np.where(ang, g.ring(0, 1, 7.5, 10.4), 1.0)
    g.put(band, 5, ORANGE, bevel=2.5)
    g.cut(g.ring(0, 1, 6.8, 7.4), 1.5)
    g.put(g.circle(0, 1, 5), 7, DARK, bevel=1)
    g.put(g.circle(6.5, -5.5, 1.8), 8, GUNMETAL)
    g.pipe(6.5, -5.5, 12.5, -12.5, 0.7, 9, STEEL)
    g.dome(12.5, -12.5, 1.2, 9, 1, BLACK)
    lit = f == 0
    g.dome(0, 1, 3.4, 7, 2.5, (110, 255, 120) if lit else (30, 80, 40), glow=0.9 if lit else 0.0)


def tracer_beacon(g, f):
    """Small black hex bug with ribbed cap, a wire antenna and a red light that double-blinks."""
    g.put(g.ngon(0, 1, 9.5, 6, math.pi / 6), 5, ((44, 46, 54), 0.5), bevel=2.5)
    g.put(g.circle(0, 1, 5.8), 6.5, DARK, bevel=1.5)
    for k in range(6):
        t = math.pi / 6 + k * math.pi / 3
        g.cut(g.segment(6.6 * math.cos(t), 1 + 6.6 * math.sin(t), 8.6 * math.cos(t), 1 + 8.6 * math.sin(t), 0.4), 1.5)
    g.pipe(3.5, -2.5, 9, -12.5, 0.5, 8, STEEL)
    g.pipe(9, -12.5, 12.5, -12.5, 0.5, 8, STEEL)
    lit = f in (0, 2)
    g.dome(0, 1, 2, 6.5, 1.5, (255, 50, 50) if lit else (90, 16, 16), glow=1.0 if lit else 0.0)


def access_codes(g, f):
    """A purple data card with gold contacts, a mag stripe and lines of code on a small screen."""
    g.put(g.poly([(-13, -8.5), (9, -8.5), (13, -4.5), (13, 8.5), (-13, 8.5)]), 4, PURPLE, bevel=1.5)
    g.put(g.rect(-10.5, -6, -3, 1, round_=1), 5, GOLD, bevel=0.5)
    g.cut(g.rect(-10.5, -2.8, -3, -2.2), 1)
    for x in (-8.3, -5.3):
        g.cut(g.rect(x - 0.3, -6, x + 0.3, 1), 1)
    g.put(g.rect(-13, 4, 13, 6.5), 4.5, ((30, 26, 40), 0.6))
    g.put(g.rect(-0.5, -6, 10, 1.2, round_=0.5), 4.5, ((14, 20, 30), 0.8))
    for y, x1 in ((-4.8, 8.5), (-2.6, 6), (-0.4, 7.5)):
        g.put(g.rect(0.7, y, x1, y + 0.9), 4.6, (110, 230, 255), glow=0.35)


def stolen_tech(g, f):
    """A military crate with steel corners and a glowing tech glyph on the lid."""
    g.put(g.rect(-13, -13, 13, 13, round_=1.5), 6, CRATE, bevel=2)
    g.put(np.maximum(g.rect(-13, -13, 13, 13, round_=1.5), -g.rect(-10.5, -10.5, 10.5, 10.5)), 8, GUNMETAL, bevel=1.5)
    for sx in (-1, 1):
        for sy in (-1, 1):
            g.put(g.rect(min(sx * 13.5, sx * 8), min(sy * 13.5, sy * 8), max(sx * 13.5, sx * 8), max(sy * 13.5, sy * 8),
                         round_=1), 9, STEEL, bevel=1)
    for y in (-4, 4):
        g.cut(g.rect(-10.5, y - 0.3, 10.5, y + 0.3), 1)
    mint = (140, 255, 200)
    g.put(g.ngon(0, 0, 6.2, 6), 5, ((20, 34, 30), 0.6))
    g.put(np.maximum(g.ngon(0, 0, 5.6, 6), -g.ngon(0, 0, 4.4, 6)), 5.5, mint, glow=0.45)
    g.put(g.circle(0, 0, 1.8), 5.5, (220, 255, 240), glow=0.45)
    for t in (-math.pi / 6, math.pi / 2, math.pi * 7 / 6):  # traces from three corners of the hex to pads
        x0, y0 = 5.6 * math.cos(t), 5.6 * math.sin(t)
        x1, y1 = 8.2 * math.cos(t), 8.2 * math.sin(t)
        g.put(g.segment(x0, y0, x1, y1, 0.5), 5.5, mint, glow=0.45)
        g.put(g.circle(x1, y1, 1.1), 5.5, mint, glow=0.45)


def helion_ore(g, f, seed=0):
    """A dark rock split by glowing orange seams: a mineral cooked in a sun's heat zone."""
    rng = random.Random(3 + seed * 7)
    pts = [(math.cos(t) * r * 1.05, math.sin(t) * r * 0.9 + 0.5)
           for t, r in ((2 * math.pi * k / 9 + rng.uniform(-0.2, 0.2), rng.uniform(10, 12.8)) for k in range(9))]
    d = g.poly(pts)
    g.put(d, 4, BASALT, profile=np.clip(-d, 0, 8 * g.u) * 0.7)
    for k in range(6):  # facets
        a = rng.uniform(0, 2 * math.pi)
        x, y = 6 * math.cos(a), 6 * math.sin(a)
        g.cut(np.maximum(d, g.segment(x, y, x + rng.uniform(-6, 6), y + rng.uniform(-6, 6), 2.2)), 1.2, bevel=1.5)
    seams = [((-8, -3), (-2, 0), (3, -5), (8, -3)), ((-2, 0), (0, 6), (5, 8)), ((3, -5), (4, -9))]
    turn = seed * 2.1  # each variant turns the seams
    seams = [[(x * math.cos(turn) - y * math.sin(turn), x * math.sin(turn) + y * math.cos(turn)) for x, y in path]
             for path in seams]
    for path in seams:
        for (x0, y0), (x1, y1) in zip(path, path[1:]):
            g.put(np.maximum(g.segment(x0, y0, x1, y1, 0.9), d + 1.5 * g.u), 2.5, (255, 150, 50), glow=0.8)
    for (x, y) in (seams[0][1], seams[0][2]):
        g.put(g.circle(x, y, 1.4), 2.6, (255, 220, 140), glow=0.5)


# Frames per design and the time between them (seconds). One frame means no animation.
ANIM = {flight_recorder: (4, 0.25), rescue_beacon: (4, 0.3), tracer_beacon: (8, 0.1), prototype_core_cargo: (4, 0.15)}
# Shapes per stack: ore pieces differ, as in vanilla ore stacks. The design takes the variant as `seed`.
VARIANTS = {helion_ore: 3}
NUGGET_OFFSET = ".25+6/64"  # how far ahead of the crew the nugget sits, as vanilla processors


def rules_dir(item):
    """The folder of the rules file that defines the item, found by its ID line."""
    hits = [p for p in MOD.rglob("*.rules") if f"\nID = {ID_PREFIX}{item}\n" in "\n" + p.read_text()]
    if len(hits) != 1:
        raise SystemExit(f"{item}: expected one rules file with 'ID = {ID_PREFIX}{item}', found {hits}")
    return hits[0].parent


def max_stack(item):
    text = (rules_dir(item) / f"{item}.rules").read_text()
    return int(text.split("\nMaxStackSize = ")[1].split()[0])


def stack(nuggets, n):
    """n objects in the vanilla stack slots, taking the nugget variants in turn. A nugget's object sits at (18, 18)."""
    img = Image.new("RGBA", (64, 64))
    for i, (x, y) in enumerate(STACK_SLOTS[:n]):
        layer = Image.new("RGBA", (64, 64))
        layer.paste(nuggets[i % len(nuggets)], (x - 18, y - 18))
        img.alpha_composite(layer)
    return img


def files_block(names, interval):
    if len(names) == 1:
        return f'File = "{names[0]}"'
    files = "\n".join(f'\t\t"{n}"' for n in names)
    return f"AnimationFiles\n\t[\n{files}\n\t]\n\tAnimationInterval = {interval}\n\tAnimationStartTime = Random"


def build(design):
    item = design.__name__
    frames, interval = ANIM.get(design, (1, 0))
    out = rules_dir(item) / "sprites" / item
    out.mkdir(parents=True, exist_ok=True)
    for old in out.glob("*.png"):
        old.unlink()

    def pen(size, u, f=0, variant=0):
        g = Pen(size, u)
        design(g, f, variant) if variant else design(g, f)
        return g

    pen(64, ICON).sprite().save(out / "icon.png", optimize=True)
    pen(128, OVERLAY).overlay().save(out / "overlay.png", optimize=True)
    pen(64, NUGGET).normals().save(out / "nugget_normals.png", optimize=True)
    # Equal frames share one file (a blink is one lit frame and the same dark frame several times).
    nuggets = [pen(64, NUGGET, f).sprite() for f in range(frames)]
    unique = []
    for img in nuggets:
        if not any(img.tobytes() == u.tobytes() for u in unique):
            unique.append(img)
    index = [next(i for i, u in enumerate(unique) if img.tobytes() == u.tobytes()) for img in nuggets]

    def save_frames(stem, imgs):
        names = [f"{stem}.png" if len(imgs) == 1 else f"{stem}_{i + 1}.png" for i in range(len(imgs))]
        for name, img in zip(names, imgs):
            img.save(out / name, optimize=True)
        return [names[i] for i in index]

    nugget_names = save_frames("nugget", unique)
    others = [pen(64, NUGGET, 0, v).sprite() for v in range(1, VARIANTS.get(design, 1))]
    stacks = [save_frames(f"stack_{n}", [stack([img, *others], n) for img in unique]) for n in range(1, max_stack(item) + 1)]

    normals = (f'NormalsFile = "nugget_normals.png"' if frames == 1 else
               "NormalsAnimationFiles\n\t[\n" + "\n".join('\t\t"nugget_normals.png"' for _ in range(frames)) + "\n\t]")
    stack_rules = "\n".join(f"\t{{\n\t\t{files_block(s, interval).replace(chr(10), chr(10) + chr(9))}\n\t\tSize = [1, 1]\n\t}}"
                            for s in stacks)
    (out / "sprites.rules").write_text(f"""// Written by tools/sprites/make_items.py. Do not edit: change the script and run it again.
Icon
{{
	Texture
	{{
		File = "icon.png"
		MipLevels = max
	}}
	Size = [64, 64]
}}

Overlay
{{
	Texture
	{{
		File = "overlay.png"
		SampleMode = Linear
		MipLevels = max
	}}
	Size = [1, 1]
}}

NuggetSprite
{{
	{files_block(nugget_names, interval)}
	{normals}
	Size = [1, 1]
	Offset = [{NUGGET_OFFSET}, 0]
	UVRotation = 1
}}

StackSprites
[
{stack_rules}
]
""")
    return out


ITEMS = (flight_recorder, sealed_case, opened_case, prototype_core_cargo, rescue_beacon, tracer_beacon, access_codes,
         stolen_tech, helion_ore)

if __name__ == "__main__":
    for design in ITEMS:
        print(f"{design.__name__}: {build(design).relative_to(REPO)}")

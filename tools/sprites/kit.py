"""
Sprite kit for Hot Cargo parts: draw shapes into a height map, then shade it like the vanilla terran art.
docs/part-art.md explains the workflow. make_sprites.py holds the part designs.

The vanilla look, measured from the game's part sprites (no art copied):
- 64 px per tile, hard pixel edges, light from the top left, near-black crevices, bright top-left rims on metal.
- Floors and machines: shaded metal. Damage levels cut transparent holes with a soft dark scorch around them.
- Roofs: flat paint (green 0,230,0 is the paint mask) with darker panel lines, holes for glows, and most of the
  detail in the normal map. Normal maps point green up (high G = faces the top of the image).
"""
import math
import pathlib
import random

import numpy as np
from PIL import Image, ImageDraw
from scipy import ndimage

TILE = 64
PAINT = (0, 230, 0)
# Light from the top left and a little above, in image coordinates (y points down).
LIGHT = np.array([-0.55, -0.75, 0.9]) / np.linalg.norm([-0.55, -0.75, 0.9])

# Materials: base colour and specular strength. Colours sampled by eye from the vanilla terran machines.
STEEL = ((150, 152, 158), 0.55)
GUNMETAL = ((96, 99, 108), 0.45)
DARK = ((54, 56, 62), 0.3)
BLACK = ((20, 20, 24), 0.1)
COPPER = ((178, 106, 62), 0.5)
HAZARD = ((214, 168, 38), 0.2)
OLIVE = ((124, 124, 92), 0.3)


class Sprite:
    """A height map with a material per pixel. Shapes write into it; color(), normals() and roof() read it."""

    def __init__(self, w_tiles, h_tiles):
        self.w, self.h = int(w_tiles * TILE), int(h_tiles * TILE)
        self.Y, self.X = np.mgrid[0:self.h, 0:self.w].astype(np.float32) + 0.5
        self.height = np.zeros((self.h, self.w), np.float32)
        self.cover = np.zeros((self.h, self.w), np.float32)
        self.base = np.zeros((self.h, self.w, 3), np.float32)
        self.spec = np.zeros((self.h, self.w), np.float32)
        self.emit = np.zeros((self.h, self.w), bool)
        self.paint = np.zeros((self.h, self.w), bool)
        self._surface = np.zeros((self.h, self.w), np.float32)

    # ---- distance fields (negative inside)

    def d_rect(self, x0, y0, x1, y1, chamfer=0.0, round_=0.0):
        cx, cy, hx, hy = (x0 + x1) / 2, (y0 + y1) / 2, (x1 - x0) / 2 - round_, (y1 - y0) / 2 - round_
        qx, qy = np.abs(self.X - cx) - hx, np.abs(self.Y - cy) - hy
        d = np.hypot(np.maximum(qx, 0), np.maximum(qy, 0)) + np.minimum(np.maximum(qx, qy), 0) - round_
        if chamfer:
            d = np.maximum(d, (qx + qy + chamfer) / math.sqrt(2))
        return d

    def d_circle(self, cx, cy, r):
        return np.hypot(self.X - cx, self.Y - cy) - r

    def d_ring(self, cx, cy, r0, r1):
        return np.maximum(self.d_circle(cx, cy, r1), -self.d_circle(cx, cy, r0))

    def d_segment(self, x0, y0, x1, y1, r):
        px, py, vx, vy = self.X - x0, self.Y - y0, x1 - x0, y1 - y0
        t = np.clip((px * vx + py * vy) / max(vx * vx + vy * vy, 1e-6), 0, 1)
        return np.hypot(px - vx * t, py - vy * t) - r

    def d_poly(self, points):
        """Distance to a polygon, from a 4x mask and a distance transform."""
        k = 4
        m = Image.new("L", (self.w * k, self.h * k), 0)
        ImageDraw.Draw(m).polygon([(x * k, y * k) for x, y in points], fill=255)
        inside = np.asarray(m.resize((self.w, self.h), Image.BOX)).astype(np.float32) / 255
        din = ndimage.distance_transform_edt(inside > 0.5)
        dout = ndimage.distance_transform_edt(inside <= 0.5)
        return np.where(inside > 0.5, -din + 0.5, dout - 0.5)

    # ---- writing

    def put(self, d, z, mat, bevel=0.0, profile=None, emit=False, paint=False):
        """Fill where d < 0 at height z. bevel ramps the height up over that many pixels from the edge.
        profile (array or None) adds a shape on top, for domes and cylinders."""
        a = np.clip(0.5 - d, 0, 1)
        inside = a > 0.5
        hgt = np.full(d.shape, float(z), np.float32)
        if bevel:
            hgt = z * np.clip(-d / bevel, 0.25, 1)
        if profile is not None:
            hgt = hgt + profile
        self.height[inside] = hgt[inside]
        self._surface[inside] = hgt[inside]
        color, spec = mat if isinstance(mat[0], tuple) else (mat, 0.0)
        self.base[inside] = color
        self.spec[inside] = spec
        self.emit[inside] = emit
        self.paint[inside] = paint
        self.cover = np.maximum(self.cover, a)
        return inside

    def cut(self, d, depth, bevel=0.0):
        """Lower the height where d < 0 (grooves, slots); keeps the material."""
        inside = d < 0
        amt = depth * (np.clip(-d / bevel, 0, 1) if bevel else 1)
        self.height[inside] -= amt[inside] if np.ndim(amt) else amt
        return inside

    def hole(self, d):
        """Transparent where d < 0 (roof openings)."""
        self.cover *= np.clip(0.5 + d, 0, 1)

    def dome(self, cx, cy, r, z, h, mat, **kw):
        rr = np.clip(1 - (np.hypot(self.X - cx, self.Y - cy) / r) ** 2, 0, 1)
        return self.put(self.d_circle(cx, cy, r), z, mat, profile=h * np.sqrt(rr), **kw)

    def cylinder(self, x0, y0, x1, y1, z, h, mat, vertical=False, **kw):
        """A cylinder lying along x (or y when vertical) inside the box."""
        if vertical:
            t = (self.X - (x0 + x1) / 2) / ((x1 - x0) / 2)
        else:
            t = (self.Y - (y0 + y1) / 2) / ((y1 - y0) / 2)
        return self.put(self.d_rect(x0, y0, x1, y1), z, mat, profile=h * np.sqrt(np.clip(1 - t * t, 0, 1)), **kw)

    def pipe(self, x0, y0, x1, y1, r, z, mat):
        d = self.d_segment(x0, y0, x1, y1, r)
        return self.put(d, z, mat, profile=r * np.sqrt(np.clip(1 - ((d + r) / r) ** 2, 0, 1)))

    def slots(self, x0, y0, x1, y1, pitch, depth, vertical=False, width=None):
        """Parallel grooves across the box (a grille)."""
        width = width or pitch / 2
        coord = self.X if vertical else self.Y
        start = x0 if vertical else y0
        stripe = (np.mod(coord - start, pitch) < width)
        box = self.d_rect(x0, y0, x1, y1) < 0
        self.height[stripe & box] -= depth

    def rivets(self, points, z, r=1.2, mat=STEEL):
        for x, y in points:
            self.dome(x, y, r, z, 1.2, mat)

    def greebles(self, x0, y0, x1, y1, seed, z, density=1.0, avoid=None):
        """Small recessed panels, vents and rivets in a free area, like the busy vanilla roofs and decks."""
        rng = random.Random(seed)
        free = np.ones((self.h, self.w), bool) if avoid is None else ~avoid
        tries = int(40 * density * (x1 - x0) * (y1 - y0) / (64 * 64))
        for _ in range(tries):
            w, h = rng.choice([(6, 4), (8, 5), (5, 8), (10, 3), (3, 10), (4, 4), (12, 6), (22, 14), (14, 22), (18, 10)])
            x, y = rng.randint(int(x0), int(x1 - w)), rng.randint(int(y0), int(y1 - h))
            box = self.d_rect(x, y, x + w, y + h)
            if not free[(box < 2)].all():
                continue
            kind = rng.random()
            if kind < 0.45:
                self.cut(np.maximum(box, -box - 1), 1.5)  # recessed outline
            elif kind < 0.75:
                self.slots(x, y, x + w, y + h, 2, 1.5, vertical=w > h)
            else:
                self.cut(box, 1.2, bevel=1)
            free &= box >= 2

    # ---- reading

    def _normals(self, strength):
        gy, gx = np.gradient(self.height)
        n = np.dstack([-gx * strength, -gy * strength, np.ones_like(gx)])
        return n / np.linalg.norm(n, axis=2, keepdims=True)

    def color(self, noise_seed=0):
        """Shaded RGBA, lit from the top left, with dark crevices: for floors and machines."""
        n = self._normals(0.9)
        flat = LIGHT[2]
        diff = np.clip(n @ LIGHT, 0, 1) / flat
        half = (LIGHT + [0, 0, 1]) / np.linalg.norm(LIGHT + [0, 0, 1])
        spec = np.clip(n @ half, 0, 1) ** 40 * self.spec * 150
        ao = 1 - np.clip((ndimage.gaussian_filter(self.height, 2.0) - self.height) * 0.09, 0, 0.55)
        crevice = (ndimage.maximum_filter(self.height, size=3) - self.height) > 2.5
        shade = (0.42 + 0.58 * diff) * ao * np.where(crevice, 0.45, 1.0)
        rgb = self.base * shade[..., None] + spec[..., None]
        rgb = np.where(self.emit[..., None], self.base, rgb)
        rgb += np.random.default_rng(noise_seed).integers(-3, 4, size=(self.h, self.w))[..., None]
        return _rgba(rgb, self.cover)

    def roof(self, noise_seed=0):
        """Roof colour: flat paint mask with darker panel lines; the normal map carries the relief."""
        # Only grooves cut into a surface darken (not the foot of a raised plate): vanilla roof lines are faint.
        lines = (ndimage.maximum_filter(self.height, size=3) - self.height > 0.9) & (self.height < self._surface)
        ao = 1 - np.clip((ndimage.gaussian_filter(self.height, 1.5) - self.height) * 0.05, 0, 0.3)
        paint = np.array(PAINT, np.float32) * (ao * np.where(lines, 0.88, 1.0))[..., None]
        metal = np.asarray(self.color(noise_seed))[..., :3].astype(np.float32)
        rgb = np.where(self.paint[..., None], paint, metal)
        return _rgba(rgb, self.cover)

    def normals(self):
        """Normal map: R right, G up, B out; flat = 128,128,255."""
        n = self._normals(0.7)
        rgb = np.dstack([n[..., 0] * 127 + 128, -n[..., 1] * 127 + 128, n[..., 2] * 127 + 128])
        return _rgba(rgb, self.cover)

    def mask(self):
        return self.cover > 0.5


def _rgba(rgb, alpha):
    a = np.dstack([np.clip(rgb, 0, 255), np.clip(alpha, 0, 1)[..., None] * 255]).astype(np.uint8)
    return Image.fromarray(a, "RGBA")


# ---- layers built from a finished sprite

def glow(holes, color, radius=5, strength=1.0):
    """roof_glows: a soft coloured halo on the roof around its holes (a bool mask); the holes stay clear."""
    halo = ndimage.gaussian_filter(holes.astype(np.float32), radius) * 2.2 * strength
    a = np.clip(halo, 0, 1) * (~holes)
    rgb = np.broadcast_to(np.array(color, np.float32), holes.shape + (3,))
    return _rgba(rgb, a)


def lights(spots, size, blur=10):
    """lights_add: dim coloured light on black. spots: (d_mask bool array, colour) pairs. Vanilla keeps these dark."""
    h, w = size
    rgb = np.zeros((h, w, 3), np.float32)
    for m, c in spots:
        rgb += ndimage.gaussian_filter(m.astype(np.float32), blur)[..., None] * np.array(c, np.float32)
    return _rgba(rgb, np.ones((h, w)))


def damaged(img, level, seed, holes=True):
    """Damage level 1 (_33) or 2 (_66): jagged holes with a soft dark scorch, like vanilla.
    holes=True cuts transparent holes (roofs). holes="burnt" fills them near black instead: a machine sits on the
    vanilla deck, and a clear hole would show the light deck where vanilla shows a dark breach."""
    rng = random.Random(seed * 10 + level)
    w, h = img.size
    burn = Image.new("L", (w, h), 0)
    hole = Image.new("L", (w, h), 0)
    bd, hd = ImageDraw.Draw(burn), ImageDraw.Draw(hole)
    for _ in range(3 * level + 1):
        cx, cy, r = rng.uniform(0, w), rng.uniform(0, h), rng.uniform(5, 8 + 5 * level)
        pts = [(cx + r * rng.uniform(0.5, 1.15) * math.cos(t), cy + r * rng.uniform(0.5, 1.15) * math.sin(t))
               for t in np.linspace(0, 2 * math.pi, 11, endpoint=False)]
        bd.polygon([(cx + (x - cx) * 1.6, cy + (y - cy) * 1.6) for x, y in pts], fill=255)
        if holes and rng.random() < 0.75:
            hd.polygon(pts, fill=255)
    burn_a = ndimage.gaussian_filter(np.asarray(burn, np.float32) / 255, 3)
    hole_a = np.asarray(hole, np.float32) / 255
    a = np.asarray(img, np.float32)
    a[..., :3] *= (1 - 0.8 * np.clip(burn_a, 0, 1))[..., None]
    if holes == "burnt":
        hit = (hole_a > 0.5) & (a[..., 3] > 0)
        a[hit, :3] = (14, 13, 13)
    else:
        a[..., 3] *= 1 - hole_a
    return Image.fromarray(np.clip(a, 0, 255).astype(np.uint8), "RGBA")


def damaged_normals(img, damaged_color):
    """Normals that follow the holes of a damaged colour layer."""
    a = np.asarray(img).copy()
    a[..., 3] = np.asarray(damaged_color)[..., 3]
    return Image.fromarray(a, "RGBA")


def blueprint(mask_img):
    """Blueprint layer from a shape mask (bool): blue fill, bright outlines of the shapes, frame at the edge."""
    m = mask_img.astype(np.float32)
    edge = (ndimage.maximum_filter(m, 3) - ndimage.minimum_filter(m, 3)) > 0
    h, w = m.shape
    rgb = np.zeros((h, w, 3), np.float32) + [0, 108, 204]
    a = np.full((h, w), 0.3, np.float32)
    rgb[edge] = [150, 210, 255]
    a[edge] = 1.0
    img = _rgba(rgb, a)
    ImageDraw.Draw(img).rectangle((0, 0, w - 1, h - 1), outline=(0, 0, 175, 255), width=4)
    return img


def deck(w_tiles, h_tiles, seed=0):
    """A plain deck in the vanilla corridor style, for the editor icon only (the part itself shows the vanilla deck)."""
    s = Sprite(w_tiles, h_tiles)
    s.put(s.d_rect(0, 0, s.w, s.h), 4, ((168, 168, 166), 0.15))
    for ty in range(int(h_tiles)):
        for tx in range(int(w_tiles)):
            x, y = tx * TILE, ty * TILE
            s.cut(np.maximum(s.d_rect(x + 1, y + 1, x + TILE - 1, y + TILE - 1),
                             -s.d_rect(x + 2, y + 2, x + TILE - 2, y + TILE - 2)), 2)
    return s.color(seed)


def frame(w_tiles, h_tiles, seed=0):
    """A wall frame in the vanilla style, for the editor icon only (the part itself shows vanilla walls)."""
    s = Sprite(w_tiles, h_tiles)
    outer = s.d_rect(0, 0, s.w, s.h)
    inner = s.d_rect(6, 6, s.w - 6, s.h - 6, chamfer=6)
    s.put(np.maximum(outer, -inner), 6, ((84, 80, 76), 0.25), bevel=3)
    s.cut(np.maximum(s.d_rect(2, 2, s.w - 2, s.h - 2), -s.d_rect(3, 3, s.w - 3, s.h - 3)), 2)
    return s.color(seed)


class Out:
    """Writes the PNG files of one part."""

    def __init__(self, root, part):
        self.dir = pathlib.Path(root) / part
        self.dir.mkdir(parents=True, exist_ok=True)
        for old in self.dir.glob("*.png"):
            old.unlink()

    def save(self, name, img):
        img.save(self.dir / f"{name}.png", optimize=True)
        return img

    def save_damaged(self, name, img, seed, holes=True):
        """The layer and its _33 and _66 damage levels. Returns the three images."""
        out = [img, damaged(img, 1, seed, holes), damaged(img, 2, seed, holes)]
        for suffix, im in zip(("", "_33", "_66"), out):
            self.save(name + suffix, im)
        return out

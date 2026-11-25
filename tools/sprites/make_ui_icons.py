# /// script
# requires-python = ">=3.12"
# dependencies = ["pillow>=13", "numpy>=2.5", "scipy>=1.18"]
# ///
"""
Draws the HUD icons of Hot Cargo buttons in the vanilla HUD icon style. Run: uv run tools/sprites/make_ui_icons.py
Writes mod/tactics/sprites/share_on.png and share_off.png (spec 1318), mod/shipai/sprites/ai_on.png, ai_off.png and
ai_mixed.png (spec 410), mod/cut/sprites/tool_cut.png (spec 2460), and mod/weld/sprites/tool_weld.png (spec 2480).

The vanilla HUD icons (gui/game/*/hud_*.png and the *_on/*_off tool icons), measured, no art copied: 96 x 96 px, a flat glyph
about 55 px wide, blue 0,183,255 when off or neutral, green 10,212,97 when on, and a soft glow of the same hue around the glyph.
"""
import math
import pathlib

import numpy as np
from PIL import Image, ImageDraw
from scipy import ndimage

MOD = pathlib.Path(__file__).resolve().parents[2] / "mod"
SIZE, K = 96, 4  # drawn at K times the size, then reduced, for smooth edges
OFF, ON = (0, 183, 255), (10, 212, 97)


def share_glyph():
    """A resource cube inside two arrows that circle it: resources that go around between players."""
    s = SIZE * K
    m = Image.new("L", (s, s), 0)
    d = ImageDraw.Draw(m)
    c = s / 2

    def p(x, y):
        return (c + x * K, c + y * K)

    # The cube: three faces with gaps, as the vanilla resources icon.
    r, gap = 15, 1.8
    top = [p(0, -r), p(r * 0.87, -r / 2), p(0, 0), p(-r * 0.87, -r / 2)]
    left = [p(-r * 0.87, -r / 2 + gap), p(-gap * 0.87, gap / 2), p(-gap * 0.87, r), p(-r * 0.87, r / 2)]
    right = [p(r * 0.87, -r / 2 + gap), p(gap * 0.87, gap / 2), p(gap * 0.87, r), p(r * 0.87, r / 2)]
    for face in (top, left, right):
        d.polygon(face, fill=255)
    # Two arrows on a ring around the cube, each over 125 degrees, with a head at its end.
    ring, width = 26, 5.2
    for start in (200, 20):
        end = start + 125
        box = [p(-ring, -ring), p(ring, ring)]
        d.arc(box, start, end, fill=255, width=int(width * K))
        a = math.radians(end)
        tip = (math.cos(a) * ring, math.sin(a) * ring)
        tangent = (-math.sin(a), math.cos(a))
        normal = (math.cos(a), math.sin(a))
        head = 8.5
        d.polygon([p(tip[0] + tangent[0] * head, tip[1] + tangent[1] * head),
                   p(tip[0] + normal[0] * head * 0.85, tip[1] + normal[1] * head * 0.85),
                   p(tip[0] - normal[0] * head * 0.85, tip[1] - normal[1] * head * 0.85)], fill=255)
    return np.asarray(m.resize((SIZE, SIZE), Image.LANCZOS), np.float32) / 255


def ai_glyph():
    """A chip with three pins on each side and a small network of nodes on it: the ship AI."""
    s = SIZE * K
    m = Image.new("L", (s, s), 0)
    d = ImageDraw.Draw(m)
    c = s / 2

    def p(x, y):
        return (c + x * K, c + y * K)

    body, stroke = 22, 6
    d.rounded_rectangle([p(-body, -body), p(body, body)], radius=5 * K, outline=255, width=int(stroke * K))
    for k in (-11, 0, 11):  # pins
        for x0, y0, x1, y1 in ((k - 2.6, -body - 8, k + 2.6, -body), (k - 2.6, body, k + 2.6, body + 8),
                               (-body - 8, k - 2.6, -body, k + 2.6), (body, k - 2.6, body + 8, k + 2.6)):
            d.rectangle([p(x0, y0), p(x1, y1)], fill=255)
    nodes = [(-8.5, -7.5), (8.5, -8.5), (0, 8.5)]
    for (x0, y0), (x1, y1) in zip(nodes, nodes[1:] + nodes[:1]):
        d.line([p(x0, y0), p(x1, y1)], fill=255, width=int(3.4 * K))
    for x, y in nodes:
        d.ellipse([p(x - 4.8, y - 4.8), p(x + 4.8, y + 4.8)], fill=255)
    return np.asarray(m.resize((SIZE, SIZE), Image.LANCZOS), np.float32) / 255


def cut_glyph():
    """A cursor arrow at the top left, as the vanilla build tool icons (tool_delete.png), and a dashed line that steps along tile
    edges at the bottom right: the cut line."""
    s = SIZE * K
    m = Image.new("L", (s, s), 0)
    d = ImageDraw.Draw(m)

    def p(x, y):
        return (x * K, y * K)

    d.polygon([p(14, 10), p(46, 30), p(30, 34), p(22, 48)], fill=255)
    path = [(40, 88), (40, 66), (62, 66), (62, 44), (86, 44)]
    dash, gap, width = 7.0, 4.5, 5.5
    for (x0, y0), (x1, y1) in zip(path, path[1:]):
        length = math.hypot(x1 - x0, y1 - y0)
        t = 0.0
        while t < length:
            t1 = min(t + dash, length)
            d.line([p(x0 + (x1 - x0) * t / length, y0 + (y1 - y0) * t / length),
                    p(x0 + (x1 - x0) * t1 / length, y0 + (y1 - y0) * t1 / length)], fill=255, width=int(width * K))
            t = t1 + gap
    return np.asarray(m.resize((SIZE, SIZE), Image.LANCZOS), np.float32) / 255


def weld_glyph():
    """A cursor arrow at the top left, as the vanilla build tool icons, and two blocks that meet along a seam with a spark: the weld."""
    s = SIZE * K
    m = Image.new("L", (s, s), 0)
    d = ImageDraw.Draw(m)

    def p(x, y):
        return (x * K, y * K)

    d.polygon([p(14, 10), p(46, 30), p(30, 34), p(22, 48)], fill=255)
    d.rectangle([p(36, 54), p(58, 82)], outline=255, width=int(5 * K))
    d.rectangle([p(62, 54), p(84, 82)], fill=255)
    # A four-point spark on the seam between the blocks.
    cx, cy = 60, 50
    for dx, dy in ((0, -11), (0, 11), (-11, 0), (11, 0)):
        d.polygon([p(cx + dx, cy + dy), p(cx + dy * 0.25, cy - dx * 0.25), p(cx - dy * 0.25, cy + dx * 0.25)], fill=255)
    return np.asarray(m.resize((SIZE, SIZE), Image.LANCZOS), np.float32) / 255


def icon(glyph, color):
    """The glyph in its colour with a soft glow of a darker shade of the same hue, as vanilla HUD icons."""
    glow = np.clip(ndimage.gaussian_filter(glyph, 3.5) * 1.6, 0, 1) * 0.75
    a = np.maximum(glyph, glow)
    shade = np.array(color, np.float32) * 0.82
    rgb = (np.array(color, np.float32) * glyph[..., None] + shade * (glow * (1 - glyph))[..., None]) / np.maximum(a, 1e-4)[..., None]
    return Image.fromarray(np.dstack([np.clip(rgb, 0, 255), a[..., None] * 255]).astype(np.uint8), "RGBA")


def save(folder, name, img):
    out = MOD / folder / "sprites"
    out.mkdir(parents=True, exist_ok=True)
    img.save(out / f"{name}.png", optimize=True)


if __name__ == "__main__":
    g = share_glyph()
    save("tactics", "share_on", icon(g, ON))
    save("tactics", "share_off", icon(g, OFF))
    g = ai_glyph()
    on, off = icon(g, ON), icon(g, OFF)
    save("shipai", "ai_on", on)
    save("shipai", "ai_off", off)
    # Some of the selected ships have their AI on: the left half on, the right half off.
    mixed = np.where((np.arange(SIZE) < SIZE // 2)[None, :, None], np.asarray(on), np.asarray(off))
    save("shipai", "ai_mixed", Image.fromarray(mixed.astype(np.uint8), "RGBA"))
    save("cut", "tool_cut", icon(cut_glyph(), OFF))
    save("weld", "tool_weld", icon(weld_glyph(), OFF))
    print(f"icons written under {MOD}")

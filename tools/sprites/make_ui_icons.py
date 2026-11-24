# /// script
# requires-python = ">=3.12"
# dependencies = ["pillow>=13", "numpy>=2.5", "scipy>=1.18"]
# ///
"""
Draws the HUD icons of Hot Cargo buttons in the vanilla HUD icon style. Run: uv run tools/sprites/make_ui_icons.py
Writes mod/tactics/sprites/share_on.png and share_off.png (spec 1318).

The vanilla HUD icons (gui/game/*/hud_*.png and the *_on/*_off tool icons), measured, no art copied: 96 x 96 px, a flat glyph
about 55 px wide, blue 0,183,255 when off or neutral, green 10,212,97 when on, and a soft glow of the same hue around the glyph.
"""
import math
import pathlib

import numpy as np
from PIL import Image, ImageDraw
from scipy import ndimage

OUT = pathlib.Path(__file__).resolve().parents[2] / "mod" / "tactics" / "sprites"
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


def icon(glyph, color):
    """The glyph in its colour with a soft glow of a darker shade of the same hue, as vanilla HUD icons."""
    glow = np.clip(ndimage.gaussian_filter(glyph, 3.5) * 1.6, 0, 1) * 0.75
    a = np.maximum(glyph, glow)
    shade = np.array(color, np.float32) * 0.82
    rgb = (np.array(color, np.float32) * glyph[..., None] + shade * (glow * (1 - glyph))[..., None]) / np.maximum(a, 1e-4)[..., None]
    return Image.fromarray(np.dstack([np.clip(rgb, 0, 255), a[..., None] * 255]).astype(np.uint8), "RGBA")


if __name__ == "__main__":
    OUT.mkdir(parents=True, exist_ok=True)
    g = share_glyph()
    icon(g, ON).save(OUT / "share_on.png", optimize=True)
    icon(g, OFF).save(OUT / "share_off.png", optimize=True)
    print(f"icons written to {OUT}")

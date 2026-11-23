# /// script
# requires-python = ">=3.12"
# dependencies = ["pillow>=13", "numpy>=2.5"]
# ///
"""
Contact sheet of the Hot Cargo item sprites next to vanilla resources, without the game. One row per item:
icon, overlay, nugget, nugget normals, the nugget lit through its normals, every stack, and the animation frames.
The last row shows each item's single stack at game size (1x and 2x) on a deck grey.
It reads vanilla images from a local game folder and writes one PNG outside the repo. The PNG holds vanilla pixels:
never commit it.

Usage: uv run tools/sprites/item_sheet.py <out.png> [--game <Data folder>] [--scale N]
"""
import argparse
import pathlib

import numpy as np
from PIL import Image, ImageDraw

REPO = pathlib.Path(__file__).resolve().parents[2]
VANILLA = ("processor", "gold", "uranium", "missile_part_nuke")
BG, DECK = (52, 56, 64, 255), (120, 122, 126, 255)
LIGHT = np.array([-0.45, 0.6, 0.66]) / np.linalg.norm([-0.45, 0.6, 0.66])  # normal-map space: G up


def lit(color, normals):
    c = np.asarray(color).astype(np.float32)
    n = (np.asarray(normals).astype(np.float32)[..., :3] - 128) / 127
    k = 0.45 + 0.55 * np.clip(n @ LIGHT, 0, 1)[..., None] / LIGHT[2]
    k = np.where(np.asarray(normals)[..., 3:4] > 0, k, 1.0)
    return Image.fromarray(np.dstack([np.clip(c[..., :3] * k, 0, 255), c[..., 3:]]).astype(np.uint8), "RGBA")


def rows_vanilla(data):
    for name in VANILLA:
        d = data / "resources" / name
        nugget = d / f"{name}.png" if (d / f"{name}.png").exists() else sorted(d.glob(f"{name}*[0-9].png"))[0]
        normals = nugget.with_name(nugget.stem + "_normals.png")
        stacks = sorted(d.glob(f"{name}_stack_*.png"), key=lambda p: int(p.stem.rsplit("_", 1)[1]))[:5]
        yield f"vanilla {name}", [d / "icon.png", d / "overlay.png", nugget, normals, (nugget, normals), *stacks], []


def rows_mod():
    for rules in sorted(REPO.glob("mod/**/sprites/*/sprites.rules")):
        d = rules.parent
        frames = sorted(d.glob("nugget_[0-9]*.png"), key=lambda p: int(p.stem.split("_")[1])) or [d / "nugget.png"]
        normals = d / "nugget_normals.png"
        stacks = sorted({p.stem.split("_")[1] for p in d.glob("stack_*.png")}, key=int)
        stack_imgs = [d / (f"stack_{n}.png" if (d / f"stack_{n}.png").exists() else f"stack_{n}_1.png") for n in stacks]
        yield d.name, [d / "icon.png", d / "overlay.png", frames[0], normals, (frames[0], normals), *stack_imgs], frames[1:]


def tile(src, size, scale):
    if isinstance(src, tuple):
        img = lit(Image.open(src[0]).convert("RGBA"), Image.open(src[1]).convert("RGBA"))
    else:
        img = Image.open(src).convert("RGBA")
    if img.size != (64, 64):
        img = img.resize((64, 64), Image.LANCZOS)
    cell = Image.new("RGBA", (64, 64), DECK if "overlay" not in str(src) else (30, 32, 38, 255))
    cell.alpha_composite(img)
    return cell.resize((size, size), Image.NEAREST)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("out", type=pathlib.Path)
    ap.add_argument("--game", type=pathlib.Path, default=REPO.parents[1] / "cosmoteer-macos" / ".game" / "Data")
    ap.add_argument("--scale", type=int, default=3)
    a = ap.parse_args()
    if a.out.resolve().is_relative_to(REPO.parent):
        raise SystemExit(f"refusing to write vanilla pixels inside the repo: {a.out}")
    rows = [*rows_vanilla(a.game), *rows_mod()]
    s, gap, label = 64 * a.scale, 6, 150
    cols = max(len(r[1]) + len(r[2]) for r in rows)
    width = label + cols * (s + gap)
    per_line = (width - label) // (64 + 128 + 3 * gap)
    zoom_h = -(-len(rows) // per_line) * (128 + 2 * gap)
    sheet = Image.new("RGBA", (width, len(rows) * (s + gap) + zoom_h + gap), BG)
    draw = ImageDraw.Draw(sheet)
    for i, (name, cells, extra) in enumerate(rows):
        y = i * (s + gap)
        draw.text((6, y + s // 2 - 6), name, fill=(230, 230, 230, 255))
        for j, src in enumerate([*cells, *extra]):
            sheet.paste(tile(src, s, a.scale), (label + j * (s + gap), y))
    y, x = len(rows) * (s + gap) + gap, label
    draw.text((6, y + 50), "game size 1x / 2x", fill=(230, 230, 230, 255))
    for name, cells, _ in rows:
        one = Image.new("RGBA", (64, 64), DECK)
        one.alpha_composite(Image.open(cells[5]).convert("RGBA"))
        sheet.paste(one, (x, y))
        sheet.paste(one.resize((128, 128), Image.NEAREST), (x + 64 + gap, y))
        x += 64 + 128 + 3 * gap
        if x + 64 + 128 > sheet.width:
            x, y = label, y + 128 + 2 * gap
    sheet.save(a.out)
    print(a.out)


if __name__ == "__main__":
    main()

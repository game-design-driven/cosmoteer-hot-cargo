# /// script
# requires-python = ">=3.12"
# dependencies = ["pillow>=13", "numpy>=2.5"]
# ///
"""
Quick look at the part art without the game: each Hot Cargo part stacked as its rules stack it (vanilla deck and walls,
own machine, doodads, roof lit through its normal map), next to vanilla parts. Rows: inside, inside at _66, roof, roof at _66.
It reads the vanilla images from a local game folder and writes one PNG outside the repo. The PNG holds vanilla pixels:
never commit it.

Usage: uv run tools/sprites/preview.py <out.png> [--game <Data folder>] [--scale N] [vanilla part folder ...]
The default game folder is the cosmoteer-macos game next to this repo's container. Vanilla parts default to reactor_small,
power_storage and control_room_small.
"""
import argparse
import pathlib

import numpy as np
from PIL import Image, ImageDraw

REPO = pathlib.Path(__file__).resolve().parents[2]
MOD = REPO / "mod" / "autonomy"
PARTS = ("targeted_reactor", "funnel", "computer")
PAINT = np.array([78, 96, 128], np.float32)  # a sample ship paint
LIGHT = np.array([-0.45, 0.6, 0.66]) / np.linalg.norm([-0.45, 0.6, 0.66])  # normal-map space: G up


def rules_block(text, name):
    """The text of a top-level block `name { ... }` in a rules file."""
    start = text.index("\n" + name + "\n") + 1
    depth, i = 0, text.index("{", start)
    for j in range(i, len(text)):
        depth += {"{": 1, "}": -1}.get(text[j], 0)
        if depth == 0:
            return text[start:j + 1]
    raise ValueError(f"no end of block {name}")


def first_file(block):
    return block.split('File = "')[1].split('"')[0]


def game_path(data, rules_path):
    if not rules_path.startswith("./Data/"):
        raise ValueError(f"not a game path: {rules_path}")
    return data / rules_path.removeprefix("./Data/")


def damaged_path(p, dmg):
    q = p.with_name(p.stem + dmg + p.suffix)
    return q if q.exists() else p


def rgba(p):
    return Image.open(p).convert("RGBA")


def roof_lit(color, normals, glows=None):
    c = np.asarray(color).astype(np.float32)
    n = (np.asarray(normals).astype(np.float32)[..., :3] - 128) / 127
    green = np.clip((c[..., 1] - np.maximum(c[..., 0], c[..., 2])) / 230, 0, 1)[..., None]
    base = c[..., :3] * (1 - green) + PAINT * (c[..., 1:2] / 230) * green
    lit = np.clip(n @ LIGHT, 0, 1)[..., None] / LIGHT[2]
    img = Image.fromarray(np.dstack([np.clip(base * (0.35 + 0.65 * lit), 0, 255), c[..., 3:4]]).astype(np.uint8), "RGBA")
    if glows is not None:
        img.alpha_composite(glows)
    return img


def views(floor_layers, walls, roof, normals, glows):
    """Inside (floor layers, walls) and roof-on-top views."""
    inside = Image.new("RGBA", walls.size, (10, 10, 12, 255))
    for layer in floor_layers + [walls]:
        inside.alpha_composite(layer, ((walls.width - layer.width) // 2, (walls.height - layer.height) // 2))
    top = inside.copy()
    if roof is not None:
        top.alpha_composite(roof_lit(roof, normals, glows))
    return inside, top


def ours(part, data, shared):
    rules = (MOD / f"{part}.rules").read_text()
    walls_name = rules.split("Walls = &<part_art.rules>/")[1].split()[0]
    walls_block = rules_block(shared, walls_name)
    rot = int(walls_block.split("UVRotation = ")[1].split()[0].rstrip(";}")) if "UVRotation" in walls_block else 0
    deck_tile = game_path(data, first_file(rules_block(shared, "DeckTile")))
    d = MOD / "sprites" / part
    out = []
    for dmg in ("", "_66"):
        machine = rgba(d / f"machine{dmg}.png")
        deck = Image.new("RGBA", machine.size)
        tile = rgba(damaged_path(deck_tile, dmg))
        for y in range(0, machine.height, tile.height):
            for x in range(0, machine.width, tile.width):
                deck.alpha_composite(tile, (x, y))
        extra = [rgba(p) for p in (d / "doodads_on1.png", d / "vanes.png") if p.exists()]
        walls = rgba(damaged_path(game_path(data, first_file(walls_block)), dmg)).rotate(-90 * rot, expand=True)
        out.append(views([deck, machine] + extra, walls, rgba(d / f"roof{dmg}.png"), rgba(d / f"roof_normals{dmg}.png"),
                         rgba(d / "roof_glows.png")))
    return out


def vanilla(folder):
    out = []
    for dmg in ("", "_66"):
        floor = next(p for p in (folder / f"floor{dmg}.png", folder / f"floor_on1{dmg}.png", folder / "floor.png",
                                 folder / "floor_on1.png") if p.exists())
        layers = [rgba(floor)] + [rgba(folder / n) for n in ("reactor.png", "doodads_on1.png") if (folder / n).exists()]
        glows = folder / "roof_glows.png"
        out.append(views(layers, rgba(damaged_path(folder / "walls.png", dmg)), rgba(damaged_path(folder / "roof.png", dmg)),
                         rgba(damaged_path(folder / "roof_normals.png", dmg)), rgba(glows) if glows.exists() else None))
    return out


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("out")
    ap.add_argument("--game", default=str(REPO.parent.parent / "cosmoteer-macos" / ".game" / "Data"))
    ap.add_argument("--scale", type=int, default=2)
    ap.add_argument("vanilla", nargs="*", default=["reactor_small", "power_storage", "control_room_small"])
    a = ap.parse_args()
    data = pathlib.Path(a.game)
    if not (data / "ships" / "terran").is_dir():
        raise SystemExit(f"preview: no game Data folder at {data} (use --game)")
    out = pathlib.Path(a.out).resolve()
    if REPO in out.parents:
        raise SystemExit("preview: the image holds vanilla pixels; write it outside the repo")
    shared = (MOD / "part_art.rules").read_text()
    cols = [(p, ours(p, data, shared)) for p in PARTS]
    cols += [(v, vanilla(data / "ships" / "terran" / v)) for v in a.vanilla]
    cell, pad = 128 * a.scale, 10
    sheet = Image.new("RGBA", (pad + len(cols) * (cell + pad), 26 + 4 * (cell + pad)), (40, 40, 44, 255))
    draw = ImageDraw.Draw(sheet)
    for i, (label, ((in0, top0), (in66, top66))) in enumerate(cols):
        x = pad + i * (cell + pad)
        draw.text((x, 8), label, fill=(255, 255, 255, 255))
        for k, v in enumerate((in0, in66, top0, top66)):
            v = v.resize((v.width * a.scale, v.height * a.scale), Image.NEAREST)
            sheet.alpha_composite(v, (x, 26 + k * (cell + pad)))
    out.parent.mkdir(parents=True, exist_ok=True)
    sheet.save(out)
    print(out)


if __name__ == "__main__":
    main()

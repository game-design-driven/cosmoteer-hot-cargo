"""Where each open objective is, from the player fleet (spec 1993). Reads the newest session file of a live telemetry folder.

Usage: uv run hc-where [--dir <telemetry folder>]
On a Mac: uv run hc-where --dir "$HOME/Library/Application Support/Cosmoteer/<steam id>/Mods/hot_cargo/telemetry"
"""

from __future__ import annotations

import argparse
import json
import math
from pathlib import Path

COMPASS = ["N", "NE", "E", "SE", "S", "SW", "W", "NW"]


def compass(dx: float, dy: float) -> str:
    """Direction on the screen: north is up. The game's world y axis points down, so north is -y."""
    angle = math.degrees(math.atan2(dx, -dy)) % 360
    return COMPASS[round(angle / 45) % 8]


def latest(events: list[dict]) -> tuple[dict | None, dict[str, dict]]:
    """The newest fleet record and the newest record of each open mission, from snapshots and mission.positions events."""
    fleet, missions = None, {}
    for e in events:
        if e["e"] in ("snap", "mission.positions") and "fleet" in e:
            fleet = e["fleet"]
            if e["e"] == "snap":
                missions = {}
            for m in e.get("missions", []):
                missions[m["mission"]] = {**m, "gs": e["gs"], "sys": e.get("sys", "")}
        elif e["e"] == "mission.ended":
            missions.pop(e.get("mission"), None)
    return fleet, missions


def lines(fleet: dict | None, missions: dict[str, dict]) -> list[str]:
    """One block per open mission: each target ship, loose item and area, with its distance and direction from the fleet centre."""
    if not fleet or "center" not in fleet:
        return ["No player ship in this system (or no positions yet)."]
    cx, cy = fleet["center"]["x"], fleet["center"]["y"]
    out = [f"Fleet centre ({cx:.0f}, {cy:.0f}), {len(fleet.get('ships', []))} ships."]
    if not missions:
        out.append("No open mission.")

    def where(o: dict) -> str:
        dx, dy = o["x"] - cx, o["y"] - cy
        return f"{math.hypot(dx, dy):,.0f} {compass(dx, dy)}"

    for m in missions.values():
        out.append(f"\n{m['name']} [{m['type']}, {m['mission']}] at game time {m['gs'] / 60:.0f} min, {m['sys']}; objectives: {', '.join(m.get('objectives', [])) or '-'}")
        for s in m.get("ships", []):
            label = s.get("name") or ("ship in stasis" if s.get("stasis") else "ship")
            tags = f" tags {','.join(s['tags'])}" if s.get("tags") else ""
            out.append(f"  ship  {label} ({s.get('side', 'player ' + str(s.get('player')))}){tags}: {where(s)}")
        for i in m.get("items", []):
            out.append(f"  item  {i.get('item')} (loose crate): {where(i)}")
        for a in m.get("areas", []):
            out.append(f"  area  {a['objective']} (radius {a['r']:,.0f}): {where(a)}")
        if not (m.get("ships") or m.get("items") or m.get("areas")):
            out.append("  no position (the objective is in another system, or has no place)")
    return out


def newest_session(folder: Path) -> Path:
    """The newest session file of this machine (not a part file, not a client copy in players/)."""
    files = [p for p in folder.glob("*.jsonl") if ".part" not in p.name and p.name != "notes.jsonl"]
    assert files, f"no session file in {folder}"
    return max(files, key=lambda p: p.stat().st_mtime)


def read(path: Path) -> list[dict]:
    events = []
    for line in path.read_text(encoding="utf-8").splitlines():
        try:
            events.append(json.loads(line))
        except json.JSONDecodeError:
            pass  # the line that the game writes right now
    return events


def main() -> None:
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("--dir", type=Path, default=Path("../../../.telemetry/main"), help="the live telemetry folder of the host")
    args = p.parse_args()
    path = newest_session(args.dir)
    print(f"{path.name}")
    print("\n".join(lines(*latest(read(path)))))

"""Area dependency ratchet (spec 17, Rules): the list of which area uses which other area never grows by accident.

Usage: python3 tools/area-deps.py           (from the repo root; CI runs it in the build job of check.yml and in pr-text)
       python3 tools/area-deps.py --print   the edges of the code now, in the format of tools/area-deps.txt
Exit code 1 when the code has an edge that tools/area-deps.txt does not list, or the file lists an edge that is gone.

An area is a folder of src/HotCargo. Area A uses area B when a file in A/ has "using HotCargo.B", the namespace HotCargo.B, or
code that names B with a dot after it ("B.Type", "HotCargo.B.Type"). SHARED areas are the base of every area and are not edges.
Limit: text level. It does not see a use through "using static", a type alias or code inside an interpolated string, and a local or a member named like an area
("Hunters.Count" on a field) counts as a use of that area; a name after a dot ("State.Hunters.") does not. Revisit with a Roslyn
analyzer if a real edge gets through.
"""
import re
import sys
from pathlib import Path

SRC = Path("src/HotCargo")
ALLOW = Path("tools/area-deps.txt")
# The base that every area builds on (spec 17: Core, the shared UI, the test commands).
SHARED = {"Core", "Ui", "Dev"}


def edges() -> dict[tuple[str, str], str]:
    """Each edge (from area, to area) of the code, with one place that makes it."""
    areas = sorted(p.name for p in SRC.iterdir() if p.is_dir() and p.name not in ("bin", "obj"))
    # An area name that starts a dotted name: not after a letter, a digit or a dot (a member like State.Hunters), except "HotCargo.".
    uses = re.compile(r"(?:(?<![\w.])|(?<=HotCargo\.))(" + "|".join(areas) + r")\s*\.\s*[A-Za-z_]")
    found: dict[tuple[str, str], str] = {}
    for area in areas:
        for path in sorted((SRC / area).rglob("*.cs")):
            if {"bin", "obj"} & set(path.parts):
                continue
            for no, text in enumerate(path.read_text(encoding="utf-8").splitlines(), 1):
                # Code only: no comment, and no text in double quotes (a tunable path in a message names an area too).
                code = " ".join(text.split("//")[0].split('"')[::2])
                names = set(uses.findall(code))
                for prefix in ("using HotCargo.", "namespace HotCargo."):
                    if code.strip().startswith(prefix):
                        names.add(code.strip()[len(prefix):].rstrip(";").split(".")[0])
                for other in names & set(areas) - SHARED - {area}:
                    found.setdefault((area, other), f"{path.as_posix()}:{no}")
    return found


def listed() -> set[tuple[str, str]]:
    """tools/area-deps.txt: "<from area> <to area>  # reason", one line per edge."""
    out = set()
    for raw in ALLOW.read_text(encoding="utf-8").splitlines():
        line = raw.split("#")[0].split()
        if not line:
            continue
        if len(line) != 2:
            raise SystemExit(f"{ALLOW}: bad line '{raw}'")
        if tuple(line) in out:
            raise SystemExit(f"{ALLOW}: {line[0]} {line[1]} is listed twice")
        out.add((line[0], line[1]))
    return out


def main() -> int:
    code = edges()
    if sys.argv[1:] == ["--print"]:
        print("\n".join(f"{a} {b}" for a, b in sorted(code)))
        return 0
    allow = listed()
    new = sorted(set(code) - allow)
    gone = sorted(allow - set(code))
    for a, b in new:
        print(f"== new edge: {a} uses {b} ({code[(a, b)]})")
    for a, b in gone:
        print(f"== edge gone: {a} no longer uses {b}. Delete the line '{a} {b}' in {ALLOW}.")
    if new:
        print("== area deps: an area reaches into another area for the first time. Use Run.Hooks or an event in Core instead, or move the "
              f"shared part to Core. If the edge is right, add '<from> <to>  # <reason>' to {ALLOW} and say so in the PR's Decisions.")
    if new or gone:
        return 1
    print(f"== area deps: ok ({len(code)} known edges)")
    return 0


if __name__ == "__main__":
    sys.exit(main())

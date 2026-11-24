"""Spec requirement numbers: the block registry check, references, and renumbering.

  python3 tools/spec_refs.py check            the rules below; exit code 1 with one line per problem and its fix
  python3 tools/spec_refs.py renumber <map>   applies "<old> <new>" lines to definitions and references in every tracked file
  python3 tools/spec_refs.py blocks           prints a registry made from the current numbers and the stated ranges

Rules (check):
- Section numbers (the "42-" prefix of a spec file) and requirement numbers ("1004. " at a line start in spec/*.md) are unique.
- Every requirement lies in a block of its own file in spec/blocks.txt; blocks do not overlap; each block names a spec file.
- References: a number after "spec", "requirement(s)" or "req." (with lists: "1592, 1593", "1984 to 1986") in a tracked text
  file. A listed number must exist as a requirement or a section. A range "A to B" needs both ends to be requirements, or
  must lie inside the blocks of one file (an area's reserved block, "spec 1770 to 1799").
  The requirements column of spec/16-default-tunables.md counts as references too.
Limit: a reference without one of those words (a bare "(1880)") is not seen. Revisit if such references appear.
Called by: scripts/check-spec-numbers.sh (CI spec-numbers and pr-text).
"""
import re
import subprocess
import sys
from collections import defaultdict
from pathlib import Path

SPEC = Path("spec")
BLOCKS = SPEC / "blocks.txt"
TABLE = SPEC / "16-default-tunables.md"
NUM = r"\d+(?![\d.,]\d)"
PHRASE = re.compile(rf"(?i)\b(?:specs?|requirements?|req\.)\s+({NUM}(?:\s*(?:,|and|or|to|–)\s*{NUM})*)")
ITEM = re.compile(rf"({NUM})(?:\s*(?:to|–)\s*({NUM}))?")
TEXT_SUFFIXES = (".md", ".cs", ".txt", ".rules", ".sh", ".py", ".yml", ".ps1", ".json", ".bat")


def requirements() -> dict[int, str]:
    out: dict[int, str] = {}
    for f in sorted(SPEC.glob("*.md")):
        for line in f.read_text(encoding="utf-8").splitlines():
            if m := re.match(r"^(\d+)\. ", line):
                out.setdefault(int(m.group(1)), f.name)
    return out


def sections() -> set[int]:
    return {int(f.name.split("-")[0]) for f in SPEC.glob("*.md") if f.name[0].isdigit()}


def tracked() -> list[Path]:
    files = subprocess.run(["git", "ls-files"], capture_output=True, text=True, check=True).stdout.split()
    return [Path(f) for f in files if f.endswith(TEXT_SUFFIXES) and not f.startswith(".game")]


def table_cells(line: str) -> tuple[int, int] | None:
    """The span of the requirements column (the last cell) of a table row in spec/16-default-tunables.md."""
    if not line.startswith("|") or line.startswith("|--") or line.count("|") < 3:
        return None
    end = line.rstrip().rfind("|")
    start = line.rfind("|", 0, end)
    return start + 1, end


def references(path: Path, text: str):
    """(line number, start, end, first, last) of each referenced number or range; last is None for a single number."""
    for no, line in enumerate(text.splitlines(), 1):
        spans = [(m.start(1), m.group(1)) for m in PHRASE.finditer(line)]
        if path == TABLE and (cell := table_cells(line)):
            spans.append((cell[0], line[cell[0]:cell[1]]))
        for base, chunk in spans:
            for m in ITEM.finditer(chunk):
                yield no, base + m.start(), base + m.end(), int(m.group(1)), int(m.group(2)) if m.group(2) else None


def read_blocks() -> list[tuple[int, int, str]]:
    out = []
    for raw in BLOCKS.read_text(encoding="utf-8").splitlines():
        line = raw.split("#")[0].strip()
        if line:
            rng, name = line.split()
            lo, hi = map(int, rng.split("-"))
            out.append((lo, hi, name))
    return sorted(out)


def check() -> int:
    problems = []
    names = [f.name for f in SPEC.glob("*.md") if f.name[0].isdigit()]
    dup_sec = sorted({n.split("-")[0] for n in names if sum(m.split("-")[0] == n.split("-")[0] for m in names) > 1})
    if dup_sec:
        problems.append(f"two spec files share the section number {', '.join(dup_sec)}. Fix: give the newer file the next free section number.")
    seen: dict[int, str] = {}
    for f in sorted(SPEC.glob("*.md")):
        for line in f.read_text(encoding="utf-8").splitlines():
            if m := re.match(r"^(\d+)\. ", line):
                n = int(m.group(1))
                if n in seen:
                    problems.append(f"requirement {n} is in {seen[n]} and {f.name}. Fix: keep the number that main has, renumber yours from your block (scripts/spec-block.sh).")
                seen.setdefault(n, f.name)
    blocks = read_blocks()
    for lo, hi, name in blocks:
        if not (SPEC / name).exists():
            problems.append(f"spec/blocks.txt names spec/{name}, which does not exist. Fix: correct the file name or remove the line.")
    for (lo1, hi1, n1), (lo2, hi2, n2) in zip(blocks, blocks[1:]):
        if lo2 <= hi1:
            problems.append(f"spec/blocks.txt: {lo1}-{hi1} {n1} overlaps {lo2}-{hi2} {n2}. Fix: give one of them a free block (scripts/spec-block.sh).")
    reqs = requirements()

    def owner(n: int) -> str | None:
        return next((name for lo, hi, name in blocks if lo <= n <= hi), None)

    for n, name in sorted(reqs.items()):
        if owner(n) != name:
            problems.append(f"requirement {n} in {name} lies outside the blocks of that file. Fix: add your brief's block to spec/blocks.txt, "
                            f"or run scripts/spec-block.sh {name} for the next free block, and renumber into it (tools/spec_refs.py renumber).")
    secs = sections()
    for path in tracked():
        text = path.read_text(encoding="utf-8", errors="replace")
        for no, _, _, first, last in references(path, text):
            if last is None:
                if first not in reqs and first not in secs:
                    problems.append(f"{path}:{no}: references requirement {first}, which does not exist. Fix: point it at the right requirement, or drop it.")
            elif not (first in reqs and last in reqs) and not (owner(first) and all(owner(k) == owner(first) for k in range(first, last + 1))):
                problems.append(f"{path}:{no}: the range {first} to {last} has an end that is no requirement and does not lie inside the blocks of one spec file. Fix: use the first and last requirement it means, or register the block in spec/blocks.txt.")
    for p in problems:
        print(f"check-spec-numbers: {p}")
    if not problems:
        print(f"check-spec-numbers: OK ({len(reqs)} requirements in {len(blocks)} blocks)")
    return 1 if problems else 0


def renumber(map_file: str) -> int:
    mapping = {int(a): int(b) for a, b in (line.split() for line in Path(map_file).read_text().splitlines() if line.strip())}
    flagged = []
    for path in tracked():
        text = path.read_text(encoding="utf-8", errors="replace")
        lines = text.splitlines(keepends=True)
        edits = defaultdict(list)
        for no, start, end, first, last in references(path, text):
            if last is None:
                if first in mapping:
                    edits[no].append((start, end, str(mapping[first])))
            elif first in mapping or last in mapping:
                a, b = mapping.get(first, first), mapping.get(last, last)
                if first in mapping and last in mapping and b - a == last - first:
                    edits[no].append((start, end, lines[no - 1][start:end].replace(str(first), str(a), 1).replace(str(last), str(b))))
                else:
                    flagged.append(f"{path}:{no}: the range {first} to {last} breaks apart; fix it by hand")
        if path.parent == SPEC and path.suffix == ".md":
            for i, line in enumerate(lines):
                if (m := re.match(r"^(\d+)\. ", line)) and int(m.group(1)) in mapping and not any(s == 0 for s, _, _ in edits[i + 1]):
                    edits[i + 1].append((0, len(m.group(1)), str(mapping[int(m.group(1))])))
        if not edits:
            continue
        for no, items in edits.items():
            line = lines[no - 1]
            for start, end, new in sorted(set(items), reverse=True):
                print(f"{path}:{no}: {line[start:end]} -> {new}")
                line = line[:start] + new + line[end:]
            lines[no - 1] = line
        path.write_text("".join(lines), encoding="utf-8")
    for f in flagged:
        print(f)
    return 1 if flagged else 0


def blocks_from_numbers(reserved: list[tuple[int, int, str]]) -> list[tuple[int, int, str]]:
    """Blocks of ten for numbers from 70 up (one file each), exact runs below 70, plus reserved blocks that touch no other file."""
    reqs = requirements()
    out: list[tuple[int, int, str]] = []
    by_decade = defaultdict(list)
    for n, name in sorted(reqs.items()):
        by_decade[n // 10].append((n, name))
    for d, items in sorted(by_decade.items()):
        names = {name for _, name in items}
        if len(names) == 1 and d * 10 >= 70:
            out.append((d * 10, d * 10 + 9, items[0][1]))
            continue
        if d * 10 >= 70:
            raise SystemExit(f"shared block {d * 10}-{d * 10 + 9}: {sorted(names)}; renumber first")
        run = [items[0]]
        for n, name in items[1:]:
            if name == run[-1][1] and n == run[-1][0] + 1:
                run.append((n, name))
            else:
                out.append((run[0][0], run[-1][0], run[0][1])); run = [(n, name)]
        out.append((run[0][0], run[-1][0], run[0][1]))
    for lo, hi, name in reserved:
        if not any(a <= hi and lo <= b for a, b, _ in out):
            out.append((lo, hi, name))
    merged: list[tuple[int, int, str]] = []
    for lo, hi, name in sorted(out):
        if merged and merged[-1][2] == name and merged[-1][1] + 1 == lo:
            merged[-1] = (merged[-1][0], hi, name)
        else:
            merged.append((lo, hi, name))
    return merged


if __name__ == "__main__":
    cmd = sys.argv[1] if len(sys.argv) > 1 else "check"
    if cmd == "check":
        sys.exit(check())
    if cmd == "renumber":
        sys.exit(renumber(sys.argv[2]))
    if cmd == "blocks":
        reserved = [tuple(map(int, a.split("-"))) + (b,) for a, b in (line.split()[:2] for line in sys.stdin.read().splitlines() if line.strip())]
        for lo, hi, name in blocks_from_numbers(reserved):
            print(f"{lo}-{hi} {name}")
        sys.exit(0)
    raise SystemExit(__doc__)

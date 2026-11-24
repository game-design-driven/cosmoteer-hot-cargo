"""Co-op determinism lint (CLAUDE.md, "Co-op determinism"): code that can make two co-op machines disagree.

Usage: python3 tools/determinism-lint.py   (from the repo root; CI runs it in the build job of check.yml)
Exit code 1 when a file has more findings of a rule than tools/determinism-allow.txt allows. A line passes with the comment
"// determinism: <reason>" on it or on the line above.

Rules for sim code (src/HotCargo/**/*.cs, except Dev/, Telemetry/, *Autotest.cs and Log.cs, which never change the sim):
- clock: DateTime.Now/UtcNow/Today, Stopwatch, Environment.TickCount, App.Clock (use sim time: sim.Tick, State.GameSeconds).
- random: System.Random, new Random(, Random.Shared, Guid.NewGuid (use sim.Rand or a FastRand seeded from sim data).
- local: LocalPlayerIndex/ID/Money/Ships, IsLocalPlayerShip (each machine has another local player).
- hash: GetHashCode() (string and object hashes differ per process; an ObjectID hash is its id and is safe).
- threads: Parallel, AsParallel, Task.Run, ThreadPool (thread order changes the order of float sums and list adds).
- fs-order: a Directory listing without a sort on the same line (file order differs between Windows and macOS).
Rule for UI files (Ui/, Ux/, a name ending in Ui.cs, or a name with Panel, Window, Overlay, Gui, Hud, Dialog, Screen, Tooltip, Menu):
- ui-mutation: a direct sim change (AddHeat, ForceNextStage, AddMoney, Vaults.Add/Remove, an assignment to State.<field>).
  A UI change goes through an MPInput (NetManager.EnqueueInput).
Not a rule: Dictionary and HashSet iteration. .NET enumerates them in the order of their adds and removes, which the lockstep
keeps equal on every machine; the hash only places entries in buckets.
Limit: grep level. It sees a line, not where the method runs (a UI method in a sim file needs an allow-list entry or a mark).
The co-op state hash (tests/autotest-mp) catches what it misses. Revisit with a Roslyn analyzer if real desyncs get through.
"""
import re
import sys
from collections import Counter
from pathlib import Path

UI_PATH = re.compile(r"(^|/)(Ui|Ux)/|Ui\.cs$|Panel|Window|Overlay|Gui|Hud|Dialog|Screen|Tooltip|Menu")
EXEMPT = re.compile(r"^src/HotCargo/(Dev|Telemetry)/|Autotest\.cs$|^src/HotCargo/Log\.cs$")
SIM_RULES = {
    "clock": re.compile(r"DateTime\.(Now|UtcNow|Today)|\bStopwatch\b|Environment\.TickCount|\bApp\.Clock\b"),
    "random": re.compile(r"\bSystem\.Random\b|\bnew Random\(|\bRandom\.Shared\b|Guid\.NewGuid"),
    "local": re.compile(r"\bLocalPlayer(Index|ID|Money|Ships)\b|\bIsLocalPlayerShip\b"),
    "hash": re.compile(r"\.GetHashCode\(\)"),
    "threads": re.compile(r"\bParallel\.|\.AsParallel\(|\bTask\.Run\(|\bThreadPool\."),
    "fs-order": re.compile(r"\bDirectory\.(Enumerate|Get)(Files|Directories|FileSystemEntries)\((?!.*\bOrder(By)?\()"),
}
UI_RULES = {
    "ui-mutation": re.compile(r"\.AddHeat\(|\.ForceNextStage\(|\.AddMoney\(|\bVaults\.(Add|Remove)\(|\bState\.\w+(\.\w+)*\s*([+\-*/]?=)(?!=)"),
}
MARK = "// determinism:"
ALLOW = Path("tools/determinism-allow.txt")


def findings() -> tuple[Counter, dict]:
    """Counts per (file, rule), and the lines of each."""
    counts, lines = Counter(), {}
    for path in sorted(Path("src/HotCargo").rglob("*.cs")):
        rel = path.as_posix()
        if EXEMPT.search(rel):
            continue
        rules = UI_RULES if UI_PATH.search(rel) else SIM_RULES
        prev = ""
        for no, text in enumerate(path.read_text(encoding="utf-8").splitlines(), 1):
            if MARK not in text and MARK not in prev:
                code = text.split("//")[0]
                for rule, pattern in rules.items():
                    if pattern.search(code):
                        counts[(rel, rule)] += 1
                        lines.setdefault((rel, rule), []).append(f"{rel}:{no}: {rule}: {text.strip()}")
            prev = text
    return counts, lines


def allowed() -> Counter:
    """tools/determinism-allow.txt: "<file> <rule> <count>  # reason", one line per file and rule."""
    allow = Counter()
    for raw in ALLOW.read_text(encoding="utf-8").splitlines():
        line = raw.split("#")[0].strip()
        if not line:
            continue
        path, rule, count = line.split()
        if (path, rule) in allow:
            raise SystemExit(f"{ALLOW}: {path} {rule} is listed twice")
        allow[(path, rule)] = int(count)
    return allow


def main() -> int:
    counts, lines = findings()
    allow = allowed()
    bad = [(key, n) for key, n in sorted(counts.items()) if n > allow.get(key, 0)]
    for (path, rule), n in bad:
        print(f"== {path}: {n} '{rule}' lines, {allow.get((path, rule), 0)} allowed:")
        print("\n".join(f"  {line}" for line in lines[(path, rule)]))
    lower = [f"{p} {r} {allow[(p, r)]} -> {counts.get((p, r), 0)}" for (p, r) in sorted(allow) if counts.get((p, r), 0) < allow[(p, r)]]
    if lower:
        print("== determinism lint: lower these counts in tools/determinism-allow.txt (the code has fewer now): " + "; ".join(lower))
    if bad:
        print(f"== determinism lint: new lines that can desync co-op. Use sim time, sim.Rand, an MPInput or a sorted order; "
              f"if the line never changes the sim, add '{MARK} <reason>' (tools/determinism-lint.py).")
        return 1
    print(f"== determinism lint: ok ({sum(counts.values())} known lines in {len(counts)} file and rule pairs)")
    return 0


if __name__ == "__main__":
    sys.exit(main())

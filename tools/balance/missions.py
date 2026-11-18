"""Hot Cargo mission pay and offer chances against vanilla (BALANCE.md, finding 10).

Reads every mission spawner with a HotCargo* metatype from mod/**/*.rules, its money and fame multipliers (also through shared
reward blocks and the tunable factor of HotCargoScaled), its spawn chance (rules SpawnChance, or the C# tunable that replaces it)
and its spawn gate (rules conditions). Reads the vanilla mission multipliers from the game data the same way.
Prints a Markdown table: each Hot Cargo type, its pay, the vanilla mission that it compares to, and that mission's pay.

Usage: uv run --no-project tools/balance/missions.py <game Data folder>
"""

import re
import sys
from pathlib import Path

DATA = Path(sys.argv[1])
ROOT = Path(__file__).parents[2]
MOD = ROOT / "mod"
SRC = ROOT / "src/HotCargo"

# The vanilla mission of the same risk and time for each Hot Cargo type. A design judgment: no field of the mission data names it.
# Every Hot Cargo board type must have a row (the script stops otherwise).
PEER = {
    "HotCargoEscort": ("StationRescue", "defend ships under attack, several waves"),
    "HotCargoFleetAssault": ("FactionBaseDestruction", "attack a fleet above the system tier"),
    "HotCargoShadyBounty": ("Bounty", "destroy one target; a trap can spring"),
    "HotCargoChain": ("StationRescue", "story stage: escort, careful fight, salvage race"),
    "HotCargoCourier": ("Explore", "fly across the system and back"),
    "HotCargoDroneSiege": ("StationRescue", "hold a post against waves; its waves pay no kill fame"),
    "HotCargoPrototype": ("PirateHunt", "hunt one pirate, careful targeting"),
    "HotCargoSunMining": ("ResourceDelivery", "mine and deliver ore"),
    "HotCargoSalvageRights": ("Explore", "tow a hulk to a station"),
    "HotCargoCapture": ("PirateHunt", "take a ship intact past its escorts"),
    "HotCargoSurvey": ("Explore", "visit 3 points in clouds and storms"),
    "HotCargoClaimJumpers": ("ClearAsteroidFieldPirates", "guard a barge in an asteroid field; its waves pay no kill fame"),
    "HotCargoListeningPost": ("StationRescue", "escort a scout through 4 waves; its waves pay no kill fame"),
    "HotCargoMiningEscort": ("StationRescue", "escort a miner to a field and back through 2 waves; its waves pay no kill fame"),
    "HotCargoHoldPass": ("ClearAsteroidFieldPirates", "clear a pass, then hold it against waves"),
    "HotCargoSundive": ("Explore", "dive into a heat zone; the heat burns vault tech"),
    "HotCargoLairRaid": ("PirateBaseDestruction", "find and raid a pirate lair"),
    "HotCargoStash": ("Explore", "fly to a rumoured stash; the pay is the loot"),
    "HotCargoDebtCollect": ("Bounty", "collect a debt from one ship"),
    "HotCargoEventRivals": ("FleetBounty", "destroy a rival crew's fleet; fame: the ships' kill fame"),
    "HotCargoEventSiege": ("StationRescue", "drive raiders off a freighter"),
    "HotCargoEventBounty": ("Bounty", "destroy one bounty hunter; fame: the hunter's kill fame"),
    "HotCargoEventTow": ("Explore", "tow a dead freighter to a station"),
}
# Not compared: the Buyer is the run's goal, and a rescue tow is a service that the players pay for.
NOT_JOBS = {"HotCargoBuyer", "HotCargoRescueTow"}

# Per-system spawn chances that C# sets in place of the rules SpawnChance: (metatype, tunables file, field).
MECHANICS = dict(re.findall(r'new\("\w+", "(HotCargo\w+)", t => t\.(\w+)\)', (SRC / "Missions/Mechanics/MechanicsArea.cs").read_text()))


def tunable(path: str, field: str) -> float:
    m = re.search(rf"public \w+ {field} = ([\d.]+)f?;", (SRC / path).read_text())
    if not m:
        raise SystemExit(f"no default for {field} in {path}")
    return float(m.group(1))


def chance_of(metatype: str, block: str) -> str:
    if metatype in MECHANICS:
        return f"{tunable('Missions/Mechanics/MechanicsTunables.cs', MECHANICS[metatype]):.0%}"
    special = {
        "HotCargoCourier": lambda: f"{tunable('Events/EventsTunables.cs', 'CourierChance'):.0%} (first trade station of a system)",
        "HotCargoStash": lambda: f"{tunable('Stash/StashTunables.cs', 'OfferChance'):.0%}",
        "HotCargoChain": lambda: f"{tunable('Chains/ChainsTunables.cs', 'StationChance'):.0%} of stations, at most "
                                 f"{tunable('Chains/ChainsTunables.cs', 'MaxNewChainsPerSystem'):.0f} per system",
        "HotCargoDebtCollect": lambda: "once per system while in debt",
    }
    if metatype in special:
        return special[metatype]()
    m = list(re.finditer(r"SpawnChance = (\d+)%", block))[-1:]
    return f"{m[0].group(1)}%" if m else "100%"


def blocks(text: str, metatype_re: str):
    """(metatype, the rules text from that Metatype line to the next one, the text of its top-level group before it) per spawner."""
    hits = list(re.finditer(rf"Metatype = ({metatype_re})", text))
    for i, m in enumerate(hits):
        end = hits[i + 1].start() if i + 1 < len(hits) else len(text)
        # A top-level group starts at a line with its name alone at column 0; its SpawnChance and Conditions come before Metatype.
        group = re.split(r"\n[A-Z]\w*\n\{", text[:m.start()])[-1]
        yield m.group(1), text[m.start():end], group


def multipliers(block: str, whole: str) -> tuple[float, float]:
    ref = re.search(r"Rewards = &~/(\w+)", block)
    if ref:
        body = whole.split(f"\n{ref.group(1)}\n", 1)[1]
        block = body.split("\n]\n" if body.startswith("[") else "\n}\n", 1)[0]
    else:
        # The mission's own reward list is its last Rewards; earlier ones belong to objectives (for example a trap's survival pay).
        block = block[block.rfind("Rewards"):]
    money = re.search(r"MoneyPerTierMultiplier = ([\d.]+)", block)
    fame = re.search(r"FamePerTierMultiplier = ([\d.]+)", block)
    m = float(money.group(1)) if money else (1.0 if "Type = Money" in block else 0.0)
    scaled = re.search(r"Tunable = (\w+)", block)
    if scaled:
        m *= tunable("Missions/MissionTunables.cs", scaled.group(1))
    return m, float(fame.group(1)) if fame else (1.0 if "Type = Fame" in block else 0.0)


def gate(before: str) -> str:
    """The nearest enclosing spawner condition: an archetype flag or a sun rule."""
    conds = re.findall(r"\n\tConditions \{ (\w+SpawnFlags) = \[([^\]]*)\] \}", before)
    return "; ".join(f"{'needs' if k.startswith('Require') else 'not with'} {v}" for k, v in conds) or "any system"


categories = dict(re.findall(r"\n\t\t(HotCargo\w+)\n\t\t\{[^}]*?CategoryID = (\w+)", "\n".join(p.read_text() for p in MOD.rglob("actions.rules"))))
vanilla = {}
for f in (DATA / "modes/career/sectors").glob("*.rules"):
    text = f.read_text()
    for metatype, block, _ in blocks(text, r"\w+"):
        vanilla.setdefault(metatype, multipliers(block, text))

rows = {}
for f in sorted(MOD.rglob("*.rules")):
    text = f.read_text()
    for metatype, block, before in blocks(text, r"HotCargo\w+"):
        # A metatype can appear in a template without rewards first (chains); keep the spawner that pays.
        if metatype in NOT_JOBS or metatype in rows and rows[metatype][0] > 0:
            continue
        m, fm = multipliers(block, text)
        if metatype == "HotCargoChain":
            # Board offers are station stages, and they all use StandardRewards. Return and Arrival stages (BountyRewards) go
            # straight to the log, so they are not offers.
            m, fm = multipliers("Rewards = &~/StandardRewards", text)
        if metatype == "HotCargoDebtCollect":
            m = tunable("Debt/DebtTunables.cs", "CollectRewardFactor")
        if metatype == "HotCargoStash":
            # The pay is the loot (LootRewards, money × LootMoneyFactor); EmptyShare of the stashes hold nothing. Expected value.
            loot_m, fm = multipliers("Rewards" + block.split("LootRewards", 1)[1].split("]", 1)[0], text)
            m = loot_m * tunable("Stash/StashTunables.cs", "LootMoneyFactor") * (1 - tunable("Stash/StashTunables.cs", "EmptyShare"))
            fm *= 1 - tunable("Stash/StashTunables.cs", "EmptyShare")
        offered = "event outcome, straight to the log" if categories.get(metatype) == "encounter" else f"{chance_of(metatype, before)}, {gate(before)}"
        rows[metatype] = (m, fm, offered)

# Tier gates: any condition on the system tier, the sector or the start system in a Hot Cargo mission's rules.
tier_gates = sorted({f"{f.relative_to(MOD)}: {g}" for f in MOD.rglob("*.rules") if "Metatype = HotCargo" in f.read_text()
                     for g in re.findall(r"\b(MinTier|MaxTier|MinSector|MaxSector|IsInitNode)\b", f.read_text())})

missing = sorted(set(rows) - set(PEER))
if missing:
    raise SystemExit(f"no vanilla peer for {missing}: add a PEER row")
print("| Hot Cargo type | Offered | Money × std | Fame × std | Vanilla peer (same risk and time) | Peer money | Peer fame |")
print("|---|---|---|---|---|---|---|")
for metatype, (m, fm, offered) in sorted(rows.items(), key=lambda r: ("straight" in r[1][2], r[0])):
    peer, why = PEER[metatype]
    pm, pf = vanilla.get(peer, (1.0, 1.0))
    kill_fame = "kill fame" in why
    flag = "" if m >= pm and (fm >= pf or kill_fame) else " ⚠"
    print(f"| {metatype[8:]} | {offered} | {m:g} | {fm:g} | {peer}: {why} | {pm:g} | {pf:g} |{flag}")

# Offers per system: board types that any system can offer (no archetype flag), with a plain per-system chance.
plain = {k: float(o.split("%", 1)[0]) / 100 for k, (_, _, o) in rows.items() if re.match(r"\d+%, (any system|not with)", o)}
expected = sum(plain.values())
print()
print(f"Board types that any system can offer, with a per-system chance: {len(plain)}. Expected offers of them per system: {expected:.1f}.")
print(f"Rule for enough types: no type above 50%, so that a type shows in at most every other system. Highest chance: {max(plain.values()):.0%}. "
      f"{'Holds' if max(plain.values()) <= 0.5 else 'Breaks'}.")
print(f"Tier gates in Hot Cargo mission rules: {', '.join(tier_gates) if tier_gates else 'none'}.")

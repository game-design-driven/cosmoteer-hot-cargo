# Run map: plan

Brief: tapered run map (lens of bands, start to one Buyer), lockdown line that closes systems, warnings, confirm, balance.

PRs, each keeps main green:
1. Generator and tunable: `HotCargoRunMap` spawners, `RunSectorCount` 6 drives size and tier ramp, Buyer in the last band, sector = step. Spec 72 (2550-2556).
2. Lockdown line and closed state: line position in RunState, creep and jump follow, two-choice rule, closed = no jump heat cut and a hunter wave on arrival; galaxy map: line, red closed systems, old lanes, tooltip.
3. Warnings and confirm: 5 min / 1 min / closed notices, red relay, confirm before a jump to a closed system (after the tech-left-behind PR on the jump dialog).
4. Balance for 6 steps (BALANCE.md, model.py) and the going-back mechanics (chain Return stages, offers that come back, unpaid rewards, waiting ships and the recall).

Decisions so far: see the PR text.

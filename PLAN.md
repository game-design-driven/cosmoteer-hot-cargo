# Plan: escort freighter crawls near enemies (#372)

Cause: the freighter has the vanilla Normal AI. AggroEnemies takes targets on radar and flies with the hull turned to them
(TacticalRetreat), so the freighter crawls. This is the `missions.escort-ambush` failure of #305.

Steps:
1. Failing test `missions-escort-radar` (hostile platform on radar beside the route).
2. Freighter AI `HotCargoFreighter`: area patrol only, no aggro module. Convoy and chain freighters.
3. Remove the test-world workaround of #331 in `hc_escort_advance` (it hid the bug).
4. Spec 36 and 1337 lines.
5. Repeated runs: sim with SIM_JITTER seeds, PC.

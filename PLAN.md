# Plan: escorts wait for the players (spec 1850 and later)

Every escort kind uses one objective, `HotCargoEscort` (Convoy Duty, chain stage ChainEscort). Vanilla 0.30.5a has no escort objective.

Design:
- After the accept, the escort is in a meet phase. It flies to the nearest player ship and stops `EscortMeetGap` from its hull.
  It goes at most `EscortMeetRange` from its start, so it does not chase the players across the system.
- When a player ship is within `EscortHailGap`, it hails (vanilla AIHailPlayer). Its comms page: "Ready to leave?".
  "Ready" is a shared choice (domain `escort`), so co-op players see hovers and votes. "Not yet" closes the window.
- No timeout: the escort waits. It hails again every `EscortRehailSeconds` while a player ship is close. Reason: a timeout that
  starts the route brings back the reported problem. Abandon stays in the mission log.
- Findable: the objective line says "Meet X", the map area marks the escort while it waits, chat lines at the hail and the start.
- On the answer: the route starts from where the escort is. If the straight route from there passes a sun closer than the
  route clearance, the escort flies back to its start first.

Steps:
1. Objective meet phase, provider and domain, tunables, strings.
2. Tests: unit test for the meet spot math, update escort game tests, new hail test, co-op step.
3. Spec, MISSIONS.md, TUNABLES.md, screenshots.

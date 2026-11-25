# Plan: weld (spec 71)

Decisions
- Stacked on feat/ship-cut (PR 324): the joint jobs reuse the cut edge job. Merge origin/main after 324 lands.
- Pull without crew (the ship pulls, like a tractor); crew then work the joint edges.
- Merge rebuilds each target part in the ship at its place (instant, free) and copies health; resources move by type; crew move as souls and bodies; doors by the game's automatic rule.
- Targets: own ships, or the game's derelicts (junk) that no contract, relic or event names, with parts of the ship's part set.

Steps
1. Spec 71 + plan
2. WeldModel (grid transform, snap, valid place, pull step) + unit tests
3. Sim: orders, MP inputs, pull, joint jobs, merge
4. Editor tool: button, ghost, R, Tab, click
5. Game tests weld-own, weld-wreck, co-op step weld.join
6. Mac screenshots and video

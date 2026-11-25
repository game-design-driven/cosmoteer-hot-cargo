# Plan: cut ships apart (spec 70)

Decisions
- One crew job per worked edge (CutEdgeJob : Job, modeled on DeconstructDoorJob / RepairPartJob). Orders live in RunState.Cut; jobs are runtime only and the tick recreates them.
- Split: the game's own StructureContiguityManager split. While it recomputes contiguity, a postfix on CommonBasePart.GetAdjacentCellsWithAdjacency hides the cut edges.
- Design trim after the split with ShipUpdateInfo.TryRemoveBlueprintParts + ApplyBlueprints (the game's blueprint-only path).
- Cut-off piece keeps the game's split result (own design from its parts, same owner).
- Pure rules in CutModel.cs (line edges, blocked edges, pieces, loose ends, staying side).

Steps
1. Spec 70 + plan (this commit)
2. CutModel + unit tests
3. Sim: CutState, MP inputs, jobs, split, design trim, autotest commands
4. Editor tool: button, drag line, check message, world drawing
5. Game tests: cut-split, cut-refused, cut-resume, coop step cut.split
6. Mac screenshots and video

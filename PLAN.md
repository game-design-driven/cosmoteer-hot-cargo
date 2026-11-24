# Plan: ship quality pass

Goal: every ship that Hot Cargo designs or changes passes the ship editor's own design checks.

Vanilla checks to reuse (decompiled game):
- `Ship.UpdateBlueprintValidity` and `BlueprintPart.InvalidReasons` (red parts: not contiguous, blocked and so on).
- `BaseBuildState.GetCrewPathInfo` with `BlueprintPathfinder`: no door access, no crew, no resource (the build tool tooltip).
- `BuildToolbox` vital categories (`ShipRules.VitalCategories`): missing airlock, command room, reactor, crew quarters, fire extinguisher.

Steps:
1. `Core/ShipCheck.cs`: the problems of a design from those vanilla checks.
2. `ShipBuilder.DesignBytes` fails loudly when a built design has a problem.
3. Autotest `shipcheck_all` and test `core-ships`: scavenger, thieves at every tier, Ore Tender, starter presets, vanilla starters after Fit Tech Vaults, the prototype carrier, the breach.
4. Fix every failing ship at its generator.
5. Before and after screenshots, PR.

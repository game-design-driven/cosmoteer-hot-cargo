# Plan: tech watch (tech outside the vaults)

Playtest: a rebuild took a vault out of the design, the tech left the vaults, nobody saw it, the run was lost.

## Decisions

- UI only. Every line reads synced sim state, so every co-op player sees the same lines. No new sim state, no MP input.
- No new mission in the mission log. A real mission touches the mission manager, saves and the mission loops of other areas. The lines go in a "Tech watch" box in the left HUD column, under the heat panel, next to the vanilla Nearby Missions list. Each line clicks to its place with `MissionFocus` (#237).
- Spots come from where the tech is, so tech that was given away (deal, surrender, Buyer, fence, hand-over) has no spot: those paths remove it.
- Spots: normal storage (decays), waiting for a vault, design too short for the ship's tech, crates in space, derelict ships, hunters that grabbed crates, friendly ships that hold tech.
- Banner and sound when tech starts to be at risk (storage, crates, derelict, hunter, design), after a short delay. Not for waiting tech or friendly holders: the players chose those.
- Design warning: when a ship's vault blueprints hold less than its tech, at once (before crew take a vault apart).
- Jump dialog: also shows when tech stays in the system.
- Derelicts with tech get an indicator (vanilla shows none on junk ships).
- Code: `Ux/TechWatch.cs`, `Ux/TechWatchModel.cs` (unit tests), `Tech/TechMarkers.cs` (derelict indicator), `Ux/JumpWarnings.cs` (dialog).
- Spec: new section 69, requirements 2120 to 2139.

## Steps

1. Model + unit tests.
2. Spots from the sim, HUD box, click focus, banner and sound, tunables.
3. Design warning and jump dialog.
4. Derelict indicator.
5. Game test `ux-tech-watch` (rebuild, crate, friendly holder, given away).
6. Screenshots (Mac runner).
7. Spec, UX.md, TUNABLES.md.

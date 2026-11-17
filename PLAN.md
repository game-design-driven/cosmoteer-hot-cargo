# Plan: a moved vault never drops its tech

Bug: a vault moved with the grab tool dropped 4 tech as crates (other vault full, no free storage), with a toast.

## Decisions

- Tech that finds no room when crew take a vault apart waits on its ship: `TechState.Waiting` (ship ID, units), saved in the run state and in the integrity hash.
- A sim tick puts waiting tech into the first vault, then normal storage, of its ship that has room.
- The ship becomes a wreck: the tech drops as crates near it (red warning). The ship is gone from its system: the tech is lost (red warning). A ship in jump limbo, in stasis or in another system keeps it.
- Waiting tech counts as the ship's tech (`Vaults.ShipTech`): fleet count, heat, loss timer, score, ship tag. It does not decay: the player did nothing risky.
- No toast or chat line for a take-apart.
- Vault art: no roof, so the 4 items always show. A closed roof hid them, so a full vault looked empty.

## Steps

1. Waiting tech in the run state, the take-apart and the tick.
2. Remove the take-apart toast and strings.
3. Vault without roof; before and after screenshots.
4. Spec 1540, 1553, 1554; heat panel split line.
5. Tests: `tech-move` (full storage, save and load), co-op step `tech.move` in `coop-economy`.

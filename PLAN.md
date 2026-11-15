# Jump warnings (UX V21, spec 1560 to 1569)

Playtest: a captured fleet had ships with no hyperdrive, and the mothership was too big for its one drive. The player saw "NEED FUEL" and a dialog that named no ship.

## Vanilla ground truth

- `GameRoot.DoHumanPlayersFtlJump`: only ships with `FtlManager.IsReadyToJump` jump. Ready needs working drives, command, spun-up drives, and fuel for the jump cost.
- `FtlManager.GetFtlMapJumpFuelCost`: cost grows with mass. One drive gives at most 50 % efficiency (`JumpEfficiency`), and tiles far from every drive give less (`JumpEfficiencyDistanceRange`).
- Drives hold 20, 40 or 80 hyperium (`HyperiumStorage.MaxResources`). A cost above the summed capacity can never be paid: "too big".
- `FtlJumpGui.ShipStatus` shows NO DRIVE (also for a ship without control) or NEED FUEL. `FtlJumpGui.OnJumpClicked` shows one dialog that names no ship.

## Design

- Pure model `Ux/JumpWarningsModel.cs`: the reason a ship stays behind.
- `Ux/JumpWarnings.cs`: jump window status and tooltip, the vanilla dialog text with the list, own ship hover line, one notice when a ship joins.
- No sim state. Switch `Ux.JumpWarnings`.

## Steps

1. Model and unit tests.
2. Game side patches and strings.
3. Spec section 58, UX.md row V21, tunable doc.
4. Autotest steps in `ux-more` and a screenshot script.
5. Screenshots, PR, merge.

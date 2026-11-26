# Tech left behind is lost (issue #351)

Rule (user decision): tech that stays in a system when the players move to another one is lost.

## Decisions
- Moment: CareerGameModeManager.OnSwitchingSimulation (Run.Hooks.SystemLeaving). Every machine runs it after the synced
  JumpingSwitchSim signal; the ships that jump are already in the new sim. The old sim is purged and archived again.
- Scope: vaults, normal storage and waiting tech of ships that stay, derelicts, crew hands outside the jumping ships, crates,
  friendly holders (spec 575) and hunters (spec 446) in that system.
- Friendly holders lose the tech too: one rule, a holder cannot follow.
- Loss timer: no pause for ships in another system (they cannot hold tech now).
- Warnings: jump dialog, map switch dialog, stranded line; after: toast, chat, run log.
- New spec section 73, block 2520-2529 (72 is taken by the callbacks branch).

## Steps
- [x] Failing-first game test `tech-left-behind` (fails on main)
- [ ] Rule: Run hook, Tech/LeftBehind, re-archive, messages, telemetry
- [ ] Loss timer and telemetry: drop ships_elsewhere
- [ ] Warnings: jump dialog, map switch dialog, recall button, stranded line; tech watch cleanup
- [ ] Specs 06, 36, 37, 39, 58, 60, 69, 73; TELEMETRY.md, UX.md, README
- [ ] Unit tests, game tests (tech-left-behind, ux-stranded-recall, ux-jump-warnings, ux-tech-watch), co-op step tech.left-behind
- [ ] Screenshot of the jump warning

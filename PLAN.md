# Telemetry gaps from the co-op playtest of 2026-11-14 (spec 1580 to 1593)

Run r20261114-212053-eb3e. The small non-telemetry bugs (run log sale price, hunter log line, Visit mission) go in a second PR (fix/playtest-bugs).

## Design

- Telemetry reads only. New sources: Harmony patches (prefix/postfix, no ref changes) and events that areas raise (pattern: `EventRunner.OutcomeChosen`).
- Pure logic in `Telemetry/TelemetryModel.cs` (fight joining, wreck lineage, session number, privacy scrub, time split) with unit tests.
- Ship lineage: a split registers its pieces under the root ship. `combat.destroyed` fires once, when the last live piece of a lineage becomes a wreck, with the root's side, name and value. One side for every event of a ship.
- `combat.captured`: `ShipMetadata.PlayerIndex` setter, non-human to human; `how` from the caller frame. `combat.fleet_left`: left behind at a jump, gone at a co-op resync.
- `run.jump`: `jumped` and `left` lists; per ship drive, fuel, drive capacity, jump cost, tech.
- Diplomacy: `dip.hail` (vanilla `AIHailPlayer`), `dip.surrender` (vanilla `MPShipSurrenderedInput`), `dip.deal` (`Deals.Decided` event). Odds, stance and confidence from `Risk.Read`.
- `tech.loss_timer`: state changes (idle, running, paused + reason). Reason from `LossTimer.PauseReason`.
- Real time split: `perf` and `session.end` get `paused_s`, `build_s`, `play_s` (local frame time).
- Mission `duration_s`: accept times by mission ID in game seconds, kept in `TelemetrySaveData`.
- Money source `tech_unlock`: `MPBuyTechInput.Execute` sets Money directly.
- `combat.fight`: damage windows joined while enemy damage continues within `FightGapSeconds`.
- Bugs: vault_hit ship and player from the prefix; deconstruction apart from destruction; unique `session_no`; privacy scrub of log lines.
- Report: captures, jumps, fights.

## Steps

1. PLAN, draft PR.
2. Model and unit tests.
3. Game side: events and fixes.
4. Report script.
5. Spec and TELEMETRY.md.
6. Tests (telemetry-basic, telemetry-run, smoke), PR ready, auto-merge.

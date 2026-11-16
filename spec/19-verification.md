## 19. Verification

- **Mac**: `dotnet build` and unit tests for `HeatModel` and tunables parsing. These tests are fast and catch silent math errors.
- **PC smoke runs**: `scripts/run.sh` starts the game minimized. The mod writes a self-check to `hot_cargo_log.txt` at load: tunables parsed, patches applied, rules content found. Scripts read the log.
- **In game**: a new career in single player with debug keys. Checks: overlay values, heat growth, each stage spawn, sale price, vault loss, Buyer win, save and load.
- **Multiplayer**: co-op autotest sessions (`scripts/autotest-mp.sh`, see CONTRIBUTING.md) run a host and a client on this Mac (two sim processes) or on the PC. Each step reports on its own, and a session fails on any desync or difference in the hash of the whole run state. A co-op session with two players checks the rest (PLAYTEST.md, Co-op).
- **Playtest checklist** for the user, in `PLAYTEST.md`.

### Test worlds

1700. **No random Hot Cargo missions in a test.** A single-player autotest rolls no Hot Cargo board missions: at system generation the innermost spawner group of each `HotCargo*` mission spawner does not spawn, no chain starts at the stations (requirement 160), and no Rumoured Stash (requirement 970) or Courier Job (requirement 85) is offered. A script with the line `# missions: random` keeps them at the release odds. Test commands that force a mission (`hc_generate`, `mech_generate`, `chain_force` and the like) still work: a forced group run gives the groups inside it that the test world turned off their rules chance back for that run. Co-op sessions (a client runs another script) and the game outside an autotest do not change. So a test that is not about missions does not depend on the mission odds. Code: `src/HotCargo/Dev/TestWorld.cs`.
1701. **The same chains on every machine.** The chain rolls of requirement 160 use a roll seeded by the run, the sector and the station (`Run.SectorRand`), not the sim's random rolls. The sim's rolls depend on how many sim ticks ran before, and that depends on frame timing, so each machine built other chains for the same test. The outage and the hacker (spec 51) already roll this way.

## 17. Architecture

```
mod/                       rules content; scripts/assemble-mod.sh builds mod.rules and strings from it
  mod.header.rules         mod header
  tunables.rules           changed tunables only (live reload)
  <area>/                  one folder for each area, named like its src/HotCargo folder in lowercase
  <area>/actions.rules     the Actions of each area
  <area>/strings.en.rules  the strings of each area
  missions/mechanics/      mechanics missions (sysgen_mechanics.rules), Helion Ore, Prototype Core
  tactics/ai_hunter.rules  hunter AI (HotCargoHunter)
  tactics/ai_vanilla.rules enemy AI modules and tactics that actions.rules appends to the vanilla combat AIs
src/HotCargo.Loader/       startup hook entry point, assembly resolve
src/HotCargo/
  Mod.cs                   Harmony setup, area registration
  Core/                    Run, RunState (save), Tunables, TunablesFile (live reload, validation), HeatModel,
                           CareerPatches (lifecycle), Activation (requirement 63), Errors, SimQueries, ShipBuilder, ShipCheck, DebugKeys
  <Area>/                  one folder for each area: <Area>Area.Register, <Area>Hub.cs (its Tunables and RunState fields), tunables, patches
  Ai/                      enemy AI behaviors (flee, call, regroup, focus, counter kiting, learned reports) and personalities; TacticsArea registers it, Tactics tunables
  ShipAi/                  player ship tactics: model (ShipAiModel), per-ship state and save hooks (ShipTactics), sim controller, MP input, editor
  Autonomy/                autonomous ships (section 45): Computer manning and ownership, Funnel, cargo and trade (AutonomyModel, AutonomyWorld)
  Scavenge/                scavenging (section 56): the age check, the shared find, salvage, collect and sell steps (Scavenger), the HcScavenge AI module
  Storyteller/             the storyteller (section 62): adapt minutes from time and losses, the pressure on hunter waves (StorytellerModel)
  Pursuit/                 the Pursuit director: wave ramp, fleet factor, big event pacer (Pacer), reporting scout, relay blockade, network outage, hacker
  Debt/                    debt (section 55): loans, interest, collectors, the hostile lender, the comms pages and shared choice domain
  RunRecord/               run record (section 49): past runs in the save folder, rows in the career log tab and the Past Runs window of the main menu
  Debris/                  debris decay (section 50): old loose junk glows, then burns up (DebrisModel, DebrisGlow)
  Breach/                  crew breach (section 61): crew with no airlock into their ship cut one (BreachModel)
  Cut/                     cut ships apart (section 70): the Cut build tool, cut orders, crew jobs, the split and the design trim (CutModel)
  Thieves/                 pirate thieves (section 63): thief ships (ThiefShip), the cargo trigger, collect and flee (Thieves, ThievesModel), the HcThief and HcRob AI modules
  Friendly/                friendly encounters (section 54): the outcome effects of friendly ship pages (join, and later ones)
  Ui/                      overlay, heat panel (areas add lines through HeatPanel.ExtraLines), run intro and end panels
  Dev/Autotest.cs          headless test commands
  Telemetry/               session telemetry files (TELEMETRY.md); read-only hooks and patches
tests/HotCargo.Tests/      fast unit tests for the pure model files
tests/autotest/            game tests that scripts/autotest.sh runs on the PC and scripts/autotest-sim.sh runs in the headless sim
scripts/                   pull-game, stage, deploy, run, stop, logs, autotest, autotest-sim, suite-wait, pull-telemetry, release
tools/headless-sim/             headless host that runs the game loop and autotests on the Mac (docs/headless-sim.md)
tools/telemetry/           telemetry analysis (uv project): markdown and HTML reports
```

Rules:

- The model files (`*Model.cs`, for example `HeatModel`) have no game types, so that they are testable on the Mac without the game.
- Patches stay thin. They call into the mod classes.
- One tunables record type. The overlay and the code read the same values.
- Areas talk through `Run.Hooks` and events in `Core`. `Core`, `Ui` and `Dev` are the base of every area. Every other use of one area by another is an edge in `tools/area-deps.txt`. `tools/area-deps.py` (CI `build` and `pr-text`) fails when the code has an edge that the file does not list, or the file lists an edge that is gone. A new edge needs a reason in the file and in the PR's Decisions block. The edges of 2026-11-26 are known, not approved: the largest are ShipAi to Autonomy and Events to Missions, Tech and MultiChoice.
- A vanilla crash that a Hot Cargo feature can reach gets a guard in `Core/VanillaGuards.cs`: a Harmony patch with a narrow target that skips the call that crashes, a comment that names the vanilla method and the cause, a game test, and a report through `Errors` when it triggers. The report is an ERROR line, so a test whose code reaches the guard fails: the cause is still a bug. Guards: the grid search start (`AStarGridPathfinder.FindAllNodeDistancePairs`: a start outside the grid crashed the game, for example a crew job with a search origin far from the walkable tiles; test `core-vanilla-guards`).

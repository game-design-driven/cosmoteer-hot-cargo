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
- Plan, then apply. Code that changes several things in one step (ship parts, run state, money) checks every condition first and changes nothing until all hold. Reason: `Errors.Guard` keeps the game alive when a step throws, so a step that throws between two changes leaves a half-done state (issue 347: a weld merge). An exception in a test is an ERROR line and fails the test.
- A failed sim step rests. When the body of an `Errors.Guard` throws inside a sim phase (spec 1009) on the main thread, that place does not run again for `Debug.FailedStepRestSeconds` (60) game seconds, then it tries again. The rest is run state (`RunState.RestingSteps`), so every co-op machine skips the same ticks, also after a join or a load. Frame code (UI callbacks) never rests. The overlay shows the error and the rest. Test `core-errors`. Limit: the failed call itself is not undone.
- A vanilla crash that a Hot Cargo feature can reach gets a guard in `Core/VanillaGuards.cs`: a Harmony patch with a narrow target that skips the call that crashes, a comment that names the vanilla method and the cause, a game test, and a report through `Errors` when it triggers. The report is an ERROR line, so a test whose code reaches the guard fails: the cause is still a bug. Guards: the grid search start (`AStarGridPathfinder.FindAllNodeDistancePairs`: a start outside the grid crashed the game, for example a crew job with a search origin far from the walkable tiles; test `core-vanilla-guards`).

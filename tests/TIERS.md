# Test tiers

Three tiers of tests:

| Tier | Where | Runs | Cost |
|---|---|---|---|
| unit | `tests/HotCargo.Tests` (every `*Model.cs`) | every build, CI | under 1 s for all |
| smoke | game tests with the line `# tier: smoke` | `scripts/autotest-affected.sh` (default `AUTOTEST_TIER=smoke`) | about 60 to 150 s each |
| full | every game test | the CI `full-suite` check on `main` (`check.yml`), `AUTOTEST_TIER=full scripts/autotest-affected.sh` for the touched areas, `scripts/autotest-all.sh` | about 2.5 h of slot time |

Rules:
- Game tests only for critical systems (run start, jumps, vaults and tech, hunters and heat, co-op sync and desync, save and load, the playthrough), complex areas and areas with real bug history. Rolls, weights and formulas go to unit tests. No game test of text, layout, a single tunable value, vanilla behavior or a duplicate of another test.
- Put logic (math, rules, state machines) in a `*Model.cs` file with no game types, and test it there.
- A game test checks what only the game can show: spawns, AI, physics, UI behavior (not text or layout), saves, co-op sync, vanilla integration. It may compare a game value with a model value (a "gap" field). It does not test the model again with other inputs.
- Each area has one smoke test: it loads and runs the main flow. An area without a smoke test runs all its tests in the smoke tier.
- A touched area always runs every co-op session with a step of the area (`step <area>.<name>`): they guard co-op determinism. A change in shared code runs every smoke test and the co-op session marked smoke (`coop-core`).
- Put the marker at the end of the file, after `quit`, so it does not conflict with edits at the top. The `# runner: pc` line (headless sim) goes there too.

## Audit

The prune of 2026-11-14 classified every game test with evidence: git history (fix commits that a test caught), failures in CI runs on pull requests and merge queue groups, the flaky log, local runs in every worktree, and the run time in the last full CI run. Classes: KEEP; PC (kept, runs on the PC only, `# runner: pc`); TO_UNIT (removed, the named unit tests cover it); DELETE (removed). Co-op steps: each kept step checks that host and client stay in sync or a co-op-only flow (inputs, money split, shared choices, resync, save and load). A step that single player already covers, or that checks text or one value, went.

| Test | Class | Reason | CI s (before) |
|---|---|---|---|
| ai-call | KEEP | Hunters: a finished call brings the next group; disruptor and EMP stop calls. | 65 |
| ai-call-intercept | KEEP | New AI rule: no call from a radar blip, a call once seen (spec 196, [#132](https://github.com/game-design-driven/cosmoteer-hot-cargo/pull/132)); sensor interception. | 53 |
| ai-cover | TO_UNIT | Cover spot and peek/hide cycle: PersonalityModelTests (CoverSpot, NextCoverPhase). | 59 |
| ai-flee | KEEP | Hunters: a disarmed hunter flees and jumps out of the system. | 48 |
| ai-flee-station | KEEP | New complex flee-to-station path (spec 1314 to 1316). | - |
| ai-personality | KEEP | AI smoke test: faction rules, builds and strategies on vanilla AIs. | 48 |
| ai-team | TO_UNIT | Team roles: PersonalityModelTests (AssignRoles). | 38 |
| ai-weakside | TO_UNIT | Weak and strong sides: PersonalityModelTests (three side-profile tests). | 59 |
| autonomy-basic | KEEP | Complex area: Computer manning, ownership, wrecks; fix 7eff9be. | 134 |
| autonomy-escort | KEEP | Only escort guarding and return check against real hunters. | 60 |
| autonomy-looter | DELETE | Duplicate of co-op step autonomy.looter. | 55 |
| autonomy-miner | KEEP | Start-world fix elsewhere. | 61 |
| autonomy-miner-aim | KEEP | Regression test for fix 568e210. | 60 |
| autonomy-salvager | KEEP | Salvage jobs on a wreck: AI and physics. | 183 |
| autonomy-supply | KEEP | Only buy-and-transfer check. | 58 |
| balance-hunter-floor | KEEP | Hunters: a real spawn uses the route-tier floor. | 375 |
| balance-rules | KEEP | Station fuel floor (fix da82271); smoke test. | 218 |
| chains-blackbox | DELETE | Duplicate of chains-later (recorder, broker hand-over, payment). | 115 |
| chains-cargo | DELETE | Loose crate reward: duplicate of chains-later. | 46 |
| chains-content | KEEP | Every chain page renders and every stage spawns. | 71 |
| chains-extraction | KEEP | Crew hand-over in comms; added with fix e857807. | 71 |
| chains-fail | KEEP | Save and load mid-chain, real abandon input. | 94 |
| chains-guard | KEEP | Start-world fix elsewhere. | 54 |
| chains-later | KEEP | Widest chain flow, shelter after save and load. | 91 |
| chains-rivals-start | DELETE | Duplicate of chains-guard (offer guards neutral). | 46 |
| chains-walk | KEEP | Chains smoke test. | 68 |
| contracts-contracts | KEEP | Contracts smoke test: accept input, refused accept, fee money. | 121 |
| contracts-rivals | DELETE | Title markup text; dismissal duplicates contracts-contracts. | 69 |
| contracts-trail | KEEP | Informant heat on the sim tick, terms after save and load. | 79 |
| contracts-value | KEEP | Notoriety ratchet sits in Run.cs, not a model; move it before this goes. | 85 |
| core | KEEP | Core heat loop and save and load; smoke test; CI baseline. | 73 |
| core-playthrough | KEEP | The playthrough that blocks merges. | 181 |
| debris-decay | KEEP | Debris smoke test; it deletes world objects. | 55 |
| debt-core | KEEP | New complex feature: loan, collectors, wanted state. | - |
| debt-missions | KEEP | Mission payout hooks and refinance offer. | - |
| debt-help | KEEP | New complex feature: help spawns in a fight, real hits of both sides count, the hail and each answer. | - |
| diplomacy-friends | KEEP | Start-world fix elsewhere. | 67 |
| diplomacy-hunters | KEEP | Hunters; game fix acbc92b. | 64 |
| diplomacy-surrender | KEEP | Surrender smoke test (PC only). | PC |
| events-abandoned | KEEP | Relic and hulk spawns; caught a bug (d014687). | 62 |
| events-batch2 | TO_UNIT | Toll, Buyer price and flag age: EventModelTests. | 44 |
| events-courier | DELETE | Same steps as events-courier-handover; its once-per-system check moved there. | 78 |
| events-courier-handover | KEEP | Hand-over objective with bug history (b522f78, 9f47a58). | 166 |
| events-courier-lost | KEEP | Guards fix 9ae1abb. | 165 |
| events-courier-opened | KEEP | Contact turns hostile or jumps away. | 95 |
| events-hail | KEEP | Event pages in vanilla comms; fix d399801. | 56 |
| events-items | KEEP | Item actions, loose drops, tracer heat. | 85 |
| events-listen | KEEP | Start-world fix elsewhere. | 186 |
| events-offbooks | KEEP | Real comms click path; fix d399801. | 140 |
| events-options | TO_UNIT | Option gating is need and cost math: EventModelTests. | 69 |
| events-outcomes | KEEP | Events smoke test; validates every event. | 53 |
| events-rescue | KEEP | Tug tow uses AI movement. | 92 |
| events-research | TO_UNIT | Offer rates and premiums: EventModelTests (research trade and sale). | 32 |
| events-save | KEEP | Save and load of a pending page. | 97 |
| events-tow | KEEP | Tow Job objective success and failure. | 69 |
| events-triggers | KEEP | Arrival, Signal and Station triggers start every event. | 69 |
| events-unavoidable | TO_UNIT | Per-run cap and option gating: EventModelTests. | 48 |
| events-wanted | KEEP | Vanilla wanted with heat stages and hunters. | 52 |
| factions-combat-jump | KEEP | Jumps: a jump in a fight. | 45 |
| factions-controller | KEEP | Seed-dependent station placement. | 267 |
| factions-jumps | KEEP | Jumps and heat: relay and blind jumps. | 249 |
| factions-peace | KEEP | Ally peace in game AI; kept over the co-op step (3 fix commits). | 82 |
| factions-prices | KEEP | Smoke test; station refusals. | 52 |
| factions-watch | KEEP | Watch marks: a heat mechanic; kept over the co-op step. | 89 |
| factions-zones | KEEP | Heat: the tick applies zone factors. | 37 |
| friendly-apprentice | KEEP | Kill count and joining the fleet need the game. | 189 |
| friendly-debtor | DELETE | Duplicate of co-op step friendly.debtor. | - |
| friendly-forward | KEEP | Fix 166793b changed the game. | - |
| friendly-join | DELETE | Duplicate of co-op step friendly.join. | 65 |
| friendly-riddle | DELETE | Event data on the event system; events-outcomes covers effects. | - |
| friendly-trader | DELETE | Event options and payments; events-outcomes and events.options-draft cover them. | 90 |
| hunters-spawn | KEEP | Critical: stage spawns, hostility, strike waves, save and load. | 80 |
| hunters-tactics | KEEP | Critical: hunter tactic modules load and act. | 66 |
| hunters-takeover | KEEP | Playtest bug: new hunters joined the players (fix 8c10445). | 54 |
| hunters-waves | KEEP | Wave gaps, waiting groups across save and load, fuel drop. | 230 |
| journey-jump | KEEP | Buyer appears through the real jump hook. | 122 |
| journey-newgame-check | KEEP | Run start: presets legal, vanilla starter refused. | 59 |
| journey-run | KEEP | Journey smoke test. | 168 |
| mechanics-holdout | KEEP | Shared hold-out objective. | 65 |
| mechanics-listening | KEEP | Intensive pay rule is not in a model yet; move it before this goes. | 43 |
| mechanics-placement | KEEP | Smoke test: real generation places missions. | 134 |
| mechanics-prototype | KEEP | Bug history (4461489). | 102 |
| mechanics-salvage | KEEP | Bug history (7aa795d). | 119 |
| mechanics-save | KEEP | Save and load of every mechanics objective type. | 79 |
| mechanics-survey-mining | KEEP | Only storm survey and sun mining test. | 75 |
| missions-escort | KEEP | Smoke test; fixes 665d356, 2debd57. | 70 |
| missions-escort-ambush | KEEP | Raider targeting (fix 665d356). | 55 |
| missions-markers | KEEP | Regression test for fix 1483c8c. | 115 |
| missions-offers | KEEP | Offers and AI sides; kept over the co-op step. | 60 |
| missions-save | KEEP | Save and load of custom mission objectives. | 82 |
| missions-shady | KEEP | Shady roll and trap; kept over the co-op step. | 54 |
| onboarding-hints | KEEP | Onboarding smoke test (PC only). | PC |
| onboarding-log | KEEP | Run log hooks, saved with the run. | 215 |
| pursuit-blockade | KEEP | Bug history (4614036, d25f1c4); heat. | 72 |
| pursuit-director | TO_UNIT | Ramp, fleet factor band, ship counts, pacer: PursuitModelTests. | 60 |
| pursuit-hacker | KEEP | Heat deal and money; fix b94d6bd. | 71 |
| pursuit-outage | KEEP | Heat: the outage blocks gains, losses apply. | 56 |
| pursuit-scout | KEEP | Heat source through AI movement. | 55 |
| relays-blind | KEEP | Jumps: the blind jump out of a closed relay (#96). | 51 |
| relays-closed | KEEP | Jumps; bug history (#96). | 64 |
| relays-fee | KEEP | Jumps: the toll opens the relays. | 60 |
| relays-map | KEEP | Only test of a real rolled lockdown. | 90 |
| relays-pirates | KEEP | Complex comms and AI; fix 294bd14. | 97 |
| runmodes-difficulty | KEEP | Difficulty pressure through vanilla inputs; heat and hunters. | 48 |
| runmodes-ironman | KEEP | Deletes save files on a loss: player data. | 121 |
| runmodes-music | DELETE | Cosmetic audio switch; 0 failures. | 60 |
| runmodes-paneltip | DELETE | Tooltip text; PressureModelTests cover the factors. | 81 |
| runrecord-entry | KEEP | Writes the past-runs file: persistent player data. | 193 |
| runrecord-pastruns | TO_UNIT | Past Runs sorting, best row and a bad file: RunRecordModelTests. | - |
| scaling-counts | KEEP | Co-op scaling over real spawn paths; smoke test; fix 519dd32. | 54 |
| scaling-ui | DELETE | One setting value persists in a screen; coop-combat checks the setting in the game. | 73 |
| scavenge-basic | KEEP | New complex AI behavior: salvage, fetch and sell. | - |
| scavenge-factions | KEEP | New complex AI behavior of pirates and faction ships. | - |
| scavengers-faction | KEEP | New faction: arrivals, provocation, fleeing. | - |
| shipai-basic | KEEP | Ship tactics smoke test. | 262 |
| shipai-calls | TO_UNIT | EnemyCalling fact and disruptor rows: ShipAiModelTests. | 70 |
| shipai-editor | TO_UNIT | Term edits and presets: ShipAiModelTests, plus a new RemoveTerm test. | 74 |
| shipai-formation | KEEP | Start-world fix elsewhere. | 81 |
| shipai-hotcargo | KEEP | Tactics facts read tech, heat, hunters and timers in the game. | 68 |
| shipai-save | KEEP | Critical save and load of tactics and designs. | 78 |
| stash-rolls | KEEP | Outcomes in the world; fixes 15a99bb, 2ac5725, 35c15be. | 85 |
| tactics-combat-repair | KEEP | Complex AI: repairs cancel under fire; runs in a quiet world ([#133](https://github.com/game-design-driven/cosmoteer-hot-cargo/pull/133)). | - |
| tactics-fallback-cover | KEEP | New complex fall-back behavior; runs in a quiet world ([#133](https://github.com/game-design-driven/cosmoteer-hot-cargo/pull/133)). | - |
| tactics-fallback-pool | 60 s | a damaged combat ship repairs from a faction mate's stock | pooled crew construction | PoolModel.Pools | keep |
| tactics-fallback-wreck | 60 s | a damaged cargo ship salvages a ripe wreck, then repairs | scavenge pick and collect, crew repair | RepairModel.PickPlan | keep |
| tactics-fallback-station | KEEP | Complex: station gives crew and resources; runs in a quiet world ([#133](https://github.com/game-design-driven/cosmoteer-hot-cargo/pull/133)). | - |
| tech-count | KEEP | Tech count; kept over the co-op step. | 59 |
| tech-design-vaults | KEEP | Run start; fixes ee4b44c, 454f50e. | 95 |
| tech-drop | KEEP | Tech loss: crates or burn. | 55 |
| tech-fit-vaults | KEEP | Vaults and tech: every starter maps to a vault preset; main menu, cheap. | - |
| tech-hunters | KEEP | A hunter takes tech crates. | 54 |
| tech-move | KEEP | Fix 1489053 (moving a vault cost tech). | 45 |
| tech-outside | KEEP | Fix c797dea (crew paths). | 75 |
| tech-sell | DELETE | Duplicate of co-op step tech.trade-loss and the playthrough's sale. | 95 |
| tech-start | KEEP | Tech smoke test; CI baseline. | 96 |
| tech-timer | KEEP | Save and load of a running loss timer. | 116 |
| telemetry-basic | KEEP | Only telemetry test (PC only). | PC |
| telemetry-run | KEEP | Makes the data for the telemetry report (PC only). | PC |
| telemetry-stress | PC | A benchmark with no expect lines: now PC only, run on demand. | 159 |
| tunablesmenu-file | KEEP | File reload and tune requests; 3 CI failures in queue groups. | 44 |
| tunablesmenu-live | KEEP | Settings window smoke test. | 64 |
| ux-basic | KEEP | UX smoke test: the patches build and run headless. | 60 |
| ux-highlight | DELETE | A marker click highlights a list row: UI convenience. | - |
| ux-missions | TO_UNIT | The filter: MissionSelectModelTests; icons are UI counts. | 65 |
| ux-more | KEEP | Patch-target bug d204779. | 59 |
| worlds-current | KEEP | Start-world fix elsewhere. | 61 |
| worlds-fit | KEEP | Worlds smoke test. | 101 |
| worlds-lair | DELETE | One distance check; worlds-fit covers the lair spawn. | 68 |
| worlds-pass | TO_UNIT | Pass state machine: WorldsModelTests (pass hold, loss, waves). | 69 |
| worlds-rings | KEEP | Seed-dependent placement; fix 212d6ea. | 86 |
| worlds-save | KEEP | Save and load of worlds objectives. | 81 |
| worlds-solar | KEEP | Sundive survival (fix 73ccc56). | 60 |
| worlds-sundive | KEEP | Mission item rules (fix 796ff9e). | 75 |
| mp:coop-autonomy | KEEP | Looter under the co-op hash. The miner step removed. | 236 |
| mp:coop-combat | KEEP | Host settings, votes, AI fights, money split. 8 steps removed, 3 restarts fewer. | 552 |
| mp:coop-core | KEEP | The broad sync session: tunables, resync, save and load. 7 steps removed. | 388 |
| mp:coop-economy | KEEP | Who pays, money split, drafts, votes. 9 steps removed. | 349 |
| mp:coop-friendly | KEEP | Ship ownership and payback to the client. 4 steps removed. | 118 |
| mp:coop-shared | KEEP | The only test of shared-index co-op. | 101 |
| mp:coop-stress | PC | A cost measure (spec 42): now PC only, run on demand. | 285 |

### Co-op

Co-op tests are sessions (`tests/autotest-mp/`): `coop-core` (the broad sync session: tunables input, resync, save and load), `coop-combat`, `coop-economy`, `coop-autonomy`, `coop-friendly`, `coop-shared` (one shared player index) and `coop-stress` (a cost measure, spec 42, on the PC only). Each session compares a hash of the whole run state on both machines, so a step that adds no client input still finds a desync.

## Findings

- Most logic already lives in models: 15 of 20 model files had unit tests for every function. Game tests mostly check that the game calls the model (gap fields). So few game checks can move as a whole.
- The large cost is the number of game tests that a change runs. Before the tiers, a change in Core or shared UI ran all 100 game tests. A change in one area ran all its tests (Events: 11 single player and 1 co-op).
- The fixed cost of a game test is about 30 s (deploy, game start) plus about 10 s for a new career. Merging two tests with overlapping checks saves about 40 s of slot time.

## Numbers

Medians from `../.state/logs` (November 2026). A co-op test counts as two slots for about 115 s each. "Ideal" divides slot time by 6 slots; a busy PC takes longer (the full suite on 12 November took 2 h 40 min).

| | Before | After |
|---|---|---|
| Game tests (single player + co-op) | 79 + 21 | 77 + 21 (co-op later: 3 sessions) |
| Full suite slot time | 12,257 s (ideal 34 min) | 12,052 s (ideal 33 min) |
| `autotest-affected.sh` after a Core change | 100 tests, 12,257 s | 23 tests, 2,332 s (ideal 6.5 min) |
| `autotest-affected.sh` after an Events change | 13 tests, 1,163 s | 3 tests, 408 s |
| Unit tests | 145 | 149 |

### The prune of 2026-11-14

The policy above, applied to every game test with the evidence that the Audit section names. Single-player times: the last full CI run on main (2026-11-14), for tests that it ran. Co-op times: one local run of the changed sessions before and after, 7 tests at once on the same Mac.

| | Before | After |
|---|---|---|
| Game tests in CI | 154 (147 single player + 7 co-op) | 123 (117 + 6) |
| Removed | | 29: 16 deleted, 13 replaced by unit tests (`startjobs-pick` left with its feature on main) |
| Out of CI, on the PC only | 4 | 6 (`telemetry-stress`, `coop-stress` moved) |
| Single-player CI time | 11,660 s | 9,873 s (−15 %) |
| Co-op sessions, local (core, combat, economy, autonomy, friendly) | 1,749 s | 1,297 s (−26 %) |
| Co-op CI time | 2,029 s | about 1,320 s (estimate: the measured −26 %, `coop-stress` out) |
| Co-op steps removed | | 29 (core 7, combat 8, economy 9, friendly 4, autonomy 1) |
| Unit tests | 344 | 345 (one new: `RemoveTerm` keeps the OR with its term) |

Kept on purpose, against a shorter suite: tests of new complex features with their own spec (`ai-flee-station`, `relays-blind`), and single-player tests whose co-op step went instead (`factions-peace`, `factions-watch`, `missions-offers`, `missions-shady`, `tech-count`). Not done yet: the shorten proposals (long fixed waits in kept tests), and the moves of the rules in `contracts-value` and `mechanics-listening` into models so that those tests can go.

# Adaptive enemy AI: the storyteller (draft)

Status: proposal for review. No code exists. The requirements use draft numbers (ST1, ST2 and so on). When Yarden approves the proposal, the requirements move to a new `spec/` section with a number block from the coordinator, and the draft numbers go away.

The idea: a hidden storyteller watches how the players play and changes the enemy pressure: what spawns, when, and how hard. Intel gives the players part of its plan.

## 1. What the game already adapts

The game already reacts to the players in many places. The storyteller builds on these parts and does not replace them.

| Part | What it reads | What it changes | Spec |
|---|---|---|---|
| Heat | carried tech, fame, contracts, time | the heat stages start the hunter waves | 23 to 29, 30, 31 |
| Pursuit ramp | sector index | hunter ship count, Strike wave interval | 901 |
| Pursuit fleet factor | fleet value per player | hunter ship count, ± 15 % | 902 |
| Pacer | run history of big events | when a blockade, outage, hacker or relay lockdown can start | 905, 906 |
| Event weights | heat, fleet tech | which event a trigger picks | 71 |
| Station prices | heat | prices, crew price, refusals | 655 to 657 |
| Co-op scale | player count, enemy slider | the count of every hostile spawn | 330 to 336 |
| Difficulty | vanilla career settings | heat rate, hunter count, tech value | 680 to 683 |
| Risk model | strength on both sides | demands, flee and surrender of AI ships | 1520 to 1528 |

What is missing: the game reacts to how much the players carry and own, but not to how they play (their risks, their pace, the factions that they anger). It also has no plan that the players can find out about.

## 2. Three ways to do it

**A. Pressure dial.** A profile of the crew gives one pressure number in a narrow band (for example 0.85 to 1.25). The number multiplies the hunter count, the Strike wave interval and the event weights. Small change; it reuses the code of the fleet factor (spec 902). Weak point: there is no plan, so Intel can only say "more" or "less".

**B. Planner on top of heat (recommended).** Heat stays the clock, and the stages start as they do now. The storyteller keeps the profile and the pressure dial of A. At each system arrival it also picks one planned beat (for example "a blockade at the relay to your destination"), from beats that the game already has. The beat goes through the Pacer. Intel shows part of the plan. Medium change. Each beat reuses its existing spawn code and tells.

**C. Threat budget.** Heat stages go away. The storyteller earns points over time from the players' wealth and spends them on raids that it composes (ship mix, timing, tactics). Big rewrite. It conflicts with the pillar "the heat clock is the core resource" (spec 51), and every hunter test and balance number changes.

**Recommendation: B, in two steps.** Step 1 is the profile and the dial (approach A) with telemetry and no visible change, so playtest data shows the pressure curves. Step 2 adds the plan and Intel. 🟠 Step 1 alone already meets the request "adjust enemy pressure". The plan and Intel are what make the storyteller visible to the players. The requirements below describe B.

## 3. Requirements

### Model

ST1. **Switch.** `Storyteller.Enabled` (default off until a playtest). Off: the game behaves as it does now, and every existing test stays valid. Autotests start with it off, like the Pursuit (spec 943).
ST2. **Place.** The storyteller works on top of heat and the Pursuit. Heat starts the stages as now (spec 30). The storyteller changes the size and the timing of what the existing code spawns, and it adds at most one planned beat per system visit.
ST3. **Profile.** At each system arrival, the sim computes a profile of the crew: one score 0 to 1 for each input of section 4, and an anger score for each faction. Each score has a pure function in `StorytellerModel` with no game types.
ST4. **Memory.** The saved score = `ProfileMemory` (0.6) × the old score + (1 − `ProfileMemory`) × the new reading. So a change in play style moves the profile over 2 to 3 systems, not at once.
ST5. **Pressure.** Pressure = 1 + the weighted sum of the scores (weights in tunables, centered so that a typical crew gets 1), clamped to `PressureMin` (0.85) to `PressureMax` (1.25). In sector 1 the maximum is `EarlyPressureMax` (1.0).
ST6. **Plan.** At each system arrival, the storyteller picks one beat from the beat list (section 5) by weight, or "quiet". The weights come from the profile. The plan has a beat kind, a time window (run game seconds), a place (a relay, a station or a direction from the anchor ship of spec 1537) and a faction where it applies. The beat asks `Pacer.MayStart` when its window opens. When the Pacer refuses, the beat does not happen, and the run log says nothing about it.
ST7. **Save.** `RunState.Storyteller` saves the profile, the pressure, the plan, the Intel level and the beat history. The career integrity hash (`CareerPatches`) includes the profile, the pressure and the plan.
ST8. **Determinism.** The storyteller reads sim state and run state only, in sim order. Rolls use a `FastRand` from the system ID and the sector index, like the Pursuit rolls (spec 904). All changes run in the sim tick and in Run hooks.

### Inputs

ST9. **Inputs.** The profile reads the inputs of section 4 only. Telemetry is not an input: it can be off, and each co-op machine writes its own file (spec 543). Telemetry records the same values for tuning (ST31).
ST10. **Per player.** Fleet strength and wealth are per human player (the sum ÷ the human player count), like the fleet factor (spec 902). The co-op scale already counts the players (spec 334), so the storyteller never counts them again.
ST11. **Choices, not skill.** Wealth, risk, pace, fleet strength and faction anger can raise the pressure. Fights that the players win do not raise it. The condition input (losses, damage) can only lower the pressure: a crew that is down gets a lighter plan, never a heavier one.

### Levers

ST12. **Stage response size.** The ships of each stage response × pressure, next to the ramp and the fleet factor (`Pursuit/Director.cs`, spec 901, 902). The product of the fleet factor and the pressure stays within `CombinedMin` (0.8) to `CombinedMax` (1.3). `MaxLiveHunters` (spec 31) and the co-op scale (spec 334) apply after it, as now.
ST13. **Strike interval.** The repeat Strike wave interval ÷ pressure, through the existing hook `Run.Hooks.StrikeWaveIntervalFactor` (spec 944).
ST14. **Event weights.** Each event trigger weight (spec 71) gets a new optional term `WeightPerPressure` × (pressure − 1). Content decides which events react. The Unavoidable category (spec 87) uses it first.
ST15. **Faction anger.** For a faction with an anger score at or above `AngerThreshold` (0.5), its patrols in its own systems × (1 + `AngerPatrolFactor` (0.5) × anger), through the existing spawn count patch (`SimObjectSpawner.GetCount`, spec 332). The police count of a wanted crew (spec 335) gets the same factor. Shelter (spec 643) and the ally peace (spec 1150) stay as they are.
ST16. **Beats.** A beat uses its existing code and tells (section 5). The storyteller only changes when the beat comes, where it comes, and its weight.

### Intel

ST17. **Levels.** The plan of the current system has an Intel level 0 to 3. 0: nothing. 1, rumor: the beat category ("corporate", "faction", "quiet"). 2, report: the beat kind and its time window, rounded to the minute. 3, confirmed: the place and the ship count range as well.
ST18. **True and fuzzy.** Intel never lies. A range always holds the true value. Each text uses only values that the plan holds; a unit test checks this, like spec 1528.
ST19. **Sources.** Each source raises the level by one, at most once per plan:
- a patrol listen that succeeds (spec 1122);
- a sensor array that intercepts an enemy call (spec 1300);
- a buy at a shelter station (spec 643): a clear deal on the comms page, with the price shown before the buy (design pillar "gamble in the world");
- a new option on the hacker's page (spec 931): intel for money.

The players take over a hunter (spec 1511): the level goes to 3 at once (the ship's orders).
ST20. **Display.** The heat panel shows one INTEL line with the text of the level. A chat line and a short center message show when the level rises. At level 3, the system map shows a custom marker at the place (the marker of spec 660). Strings go in `mod/storyteller/strings.en.rules`.
ST21. **After the beat.** The run log (spec 731) names the beat when it starts, and says whether Intel saw it. So the players learn how the storyteller reacts to them.
ST22. **Reset.** A jump ends the plan. The new system gets a new plan at level 0.

### Fair limits

ST23. **Band.** The pressure never leaves its band (ST5). Each lever that it moves has its own clamp (ST12 to ST15).
ST24. **Rate.** The profile, the pressure and the plan change only at a system arrival. Nothing changes during a fight.
ST25. **Tells.** Every beat shows a tell at least `BeatTellSeconds` (30) before it reaches the players: a chat line, a ship marker (for example REPORTING, spec 911), or an FTL jump-in at the spawn distance. Nothing spawns in sight without an FTL jump-in.
ST26. **Caps.** The existing caps stay: `MaxLiveHunters`, the Pacer limits (spec 906), `MaxEventsPerSystem` (spec 70). Beats go through the Pacer.
ST27. **Co-op.** One profile and one plan for the whole crew. Intel is shared: any player can earn it, and every player sees it. A beat picks its target ship by the anchor rules (spec 1537, 1538), never by "the weakest player". The anger that one player causes counts for the crew, because the run is shared.
ST28. **Difficulty.** The difficulty factors (spec 682) apply after the storyteller, as they do after the Pursuit.

### Tests

ST29. **Unit tests.** `StorytellerModelTests`: each score from plain numbers, the memory mix, the pressure band and the early maximum, the combined clamp of ST12, "condition only lowers", the beat weights, and the Intel texts (each one holds only plan values, and each range holds the true value).
ST30. **Headless tests.**
- `story-profile`: existing commands set money, tech, heat and reputation. The pressure moves the right way and stays in its band.
- `story-plan`: a forced beat starts through the Pacer, its tell comes first, and a Pacer refusal stops it.
- `story-intel`: a patrol listen raises the level, and the heat panel text names the category. A hunter takeover sets the level to 3.
- Co-op step `story.coop` in `coop-combat`: both machines have the same plan (state hash). The client earns Intel, and the host sees it.
ST31. **Telemetry.** New events `story.profile`, `story.plan`, `story.intel` and `story.beat` (start, result, Intel level at the start). `tools/telemetry` reports the pressure per system. `tools/balance/model.py` gets three crew styles (hoarder, seller, sprinter) to compare the pressure curves before a playtest.

## 4. Inputs and where they exist

| Input | Score idea | Where it exists now | Telemetry now |
|---|---|---|---|
| Fleet strength | firepower and fleet value per player ÷ the tier reference | `Ai/Firepower.cs` (spec 1533), `Diplomacy/Situation.Strength` (spec 1520), `Pursuit/Director.FleetFactor` (spec 902) | snapshots (spec 538), combat census (spec 537) |
| Wealth | money per player, carried tech × tech value, debt balance (negative) | `GameRoot.GetPlayerState(i, deterministic: true).Money`, `RunState.FleetTech`, tech value (spec 19), `RunState.Debt` | economy (spec 535), snapshots |
| Risk taken | heat at arrival, time at the Strike stage, tech held instead of sold, open informants, contract trail | `RunState.Heat`, `SecondsSinceStrikeWave`, `FleetTech`, `DeliveredUnits`, `ContractsState.Informants` and `Terms` (spec 100 to 104), `RunState.Notoriety` | heat (spec 534), missions (spec 536) |
| Pace | game time per sector, systems visited, blind jumps | `RunState.GameSeconds`, `SectorIndex`, `VisitedNodes`, jump kinds (spec 650, 651) | `run.jump` (spec 1582) |
| Faction anger | reputation, wanted, debt stage, rival role, per faction | vanilla `GetFactionReputation`, vanilla `WantedManager` (crew-wide in vanilla), `Lenders.StageOf` (spec 1407 and later), `FactionsArea.Roles` (spec 640) | reputation changes (spec 535) |
| Condition (lowers only) | fleet health share, crew share, ships lost in the sector | `HunterOrders.HealthFraction`, crew counts, fleet changes | `combat.captured` and fleet changes (spec 1581) |

The last-column values exist only on each machine. The storyteller reads the middle column; telemetry stays a tuning tool.

## 5. Beats

Every beat in step 2 uses code that exists. No new spawn type.

| Beat | Category | Existing code | Profile that raises its weight |
|---|---|---|---|
| Early blockade at the relay to the destination | corporate | blockade (spec 915, 916) | fast pace, high heat |
| Reporting scout | corporate | scout (spec 910 to 914) | slow pace, high risk |
| Counter fleet: the next wave's personalities fit the crew's builds | corporate | personalities and build scores (spec 361, 362) | high fleet strength |
| Informant tip: the next contract informant rolls with a higher chance | corporate | informants (spec 104) | many open contracts |
| Faction sweep: patrols of an angry faction search this system | faction | spawn count patch (spec 332), police (spec 335) | faction anger |
| Early collector | faction | debt collectors (spec 1407) | debt, low wealth |
| Quiet | quiet | none | low pressure, poor condition |
| Help nearby: a higher Wayfarers or debt help chance | quiet | Wayfarers (spec 1370), debt help (spec 1430) | poor condition only |

## 6. Open questions

1. Does the storyteller have a name in the fiction (for example "the corporate analyst"), or does it stay unnamed? A name makes Intel texts easier to write.
2. Patrol listen: does Intel replace its heat gain of −4 (spec 1122), or come with it?
3. Price and place of bought Intel: shelter stations only, or every trade station at a higher price?
4. The band: is 0.85 to 1.25 enough to feel, and small enough to stay fair?
5. Default on or off for the first playtest of step 1? Off means no visible change; on gives real data sooner.
6. Faction sweep: is it acceptable to scale vanilla faction patrols, or only the police of spec 335?
7. The final spec needs a number block (about 31 requirements) from the coordinator.

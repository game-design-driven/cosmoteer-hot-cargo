# Adaptive enemy AI: the storyteller (draft)

Status: step 1 is built and its requirements are in `spec/62-storyteller.md` (1720 to 1731): the adaptation dial of section 2, approach A. **Step 2 is parked**: the plan of beats, Intel, the faction sweep, presets and a name for the storyteller. Do not build it. This doc keeps its background, its draft requirements (ST numbers) and its open questions. Yarden's decisions on 2026-11-17: step 1 only; the band is 0.85 to 1.25 (sector 1 at most 1.0); default on (off in autotests); long stays: measure first (option L0), no L1 or L2 now.

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

Revised after the RimWorld comparison (section 7) and the playtest (section 8). The main lessons: RimWorld reads few inputs, it lowers the pressure for a while after a loss, it always gives rest periods, and its wealth input makes players hide wealth. Hot Cargo already has the RimWorld "threat points": heat (from carried tech, fame and time) is the budget, and the stages spend it.

**A. Adaptation dial.** One factor, like the RimWorld adaptation factor: it rises slowly with game time without losses, and it falls at once when the crew loses a ship, tech or crew. It multiplies the hunter count and the Strike wave interval, in a narrow band. It reads no money, no pace and no fleet growth, so nothing invites the players to hide wealth. Small change: one pure model, two existing hooks (spec 901, 944). Weak point: no plan, so Intel can only say "more" or "less".

**B. Planner on top of heat (recommended).** A, plus one planned beat per system from beats that the game already has, with RimWorld pacing rules: a grace period after arrival, a minimum gap between big beats (the Pacer), a quiet or helpful beat after a loss, and help beats that follow a need curve with a floor. Intel shows part of the plan. Medium change. Each beat reuses its existing spawn code and tells.

**C. Threat budget.** Heat stages go away, and the storyteller spends points from wealth and time on raids that it composes, like RimWorld raid points. The comparison makes C look less useful, not more: heat already is a points budget with a clock and visible stages, so C is a rewrite for a model that the game already has. It conflicts with the pillar "the heat clock is the core resource" (spec 51), and every hunter test and balance number changes.

**Recommendation: B, in two steps.** Step 1 is A (the adaptation dial) with telemetry, and one of the long-stay options of section 8 if Yarden picks one. Step 2 adds the plan, the pacing rules and Intel. 🟠 Step 1 alone already meets "adjust enemy pressure". The playtest found hunter strength balanced, so the band stays narrow: the storyteller changes timing and choice more than size. The requirements below describe B.

## 3. Requirements (draft; step 1 moved to spec section 62, the rest is parked)

The step 1 parts (ST1, ST5, ST12, ST13, ST28 and ST32 to ST34) are now requirements 1720 to 1731, with these changes: the band is 0.85 to 1.25; tech losses do not count (no counter tells a lost unit from a sold one), crew deaths do; the pressure is read when a wave is sized, not only at a system arrival. Everything else here is parked.

### Model

ST1. **Switch.** `Storyteller.Enabled` (default off until a playtest). Off: the game behaves as it does now, and every existing test stays valid. Autotests start with it off, like the Pursuit (spec 943).
ST2. **Place.** The storyteller works on top of heat and the Pursuit. Heat starts the stages as now (spec 30). The storyteller changes the size and the timing of what the existing code spawns, and it adds at most one planned beat per system visit.
ST3. **Profile.** At each system arrival, the sim computes a profile of the crew: one score 0 to 1 for each input of section 4, and an anger score for each faction. Each score has a pure function in `StorytellerModel` with no game types. Only the adaptation factor (ST32) and faction anger (ST15) change the pressure. The other scores only weigh the beats (ST6), because carried tech already drives heat, and a second wealth input invites wealth hiding (section 7).
ST4. **Memory.** The saved score = `ProfileMemory` (0.6) × the old score + (1 − `ProfileMemory`) × the new reading. So a change in play style moves the profile over 2 to 3 systems, not at once.
ST5. **Pressure.** Pressure = the adaptation factor (ST32), clamped to `PressureMin` (0.85) to `PressureMax` (1.15). In sector 1 the maximum is `EarlyPressureMax` (1.0).
ST6. **Plan.** At each system arrival, the storyteller picks one beat from the beat list (section 5) by weight, or "quiet". The weights come from the profile. The plan has a beat kind, a time window (run game seconds), a place (a relay, a station or a direction from the anchor ship of spec 1537) and a faction where it applies. The beat asks `Pacer.MayStart` when its window opens. When the Pacer refuses, the beat does not happen, and the run log says nothing about it. No window opens in the first `ArrivalGraceSeconds` (180) of a visit, like the RimWorld grace before the first threat cycle.
ST7. **Save.** `RunState.Storyteller` saves the profile, the pressure, the plan, the Intel level and the beat history. The career integrity hash (`CareerPatches`) includes the profile, the pressure and the plan.
ST8. **Determinism.** The storyteller reads sim state and run state only, in sim order. Rolls use a `FastRand` from the system ID and the sector index, like the Pursuit rolls (spec 904). All changes run in the sim tick and in Run hooks.

### Inputs

ST9. **Inputs.** The profile reads the inputs of section 4 only. Telemetry is not an input: it can be off, and each co-op machine writes its own file (spec 543). Telemetry records the same values for tuning (ST31).
ST10. **Per player.** Fleet strength and wealth are per human player (the sum ÷ the human player count), like the fleet factor (spec 902). The co-op scale already counts the players (spec 334), so the storyteller never counts them again.
ST11. **Choices, not skill.** Only game time without losses (ST32) and faction anger can raise the pressure. Fights that the players win do not raise it. Losses can only lower it: a crew that is down gets a lighter plan, never a heavier one.

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
ST32. **Adaptation.** The run state saves adapt minutes, from −30 to 60, start 0. They rise by 1 per game minute while the crew loses nothing, but not above 0 in sector 1. A lost player ship removes `AdaptLossShip` (20), a lost tech unit `AdaptLossTech` (10), and each `AdaptCrewPerMinute` (5) lost crew 1. The factor follows a curve: −30 → 0.85, 0 → 1.0, 60 → 1.15. This copies the RimWorld adaptation factor (section 7) in a narrower band.
ST33. **Impact by difficulty.** The adaptation effect × `AdaptImpact` from the difficulty row (spec 680): 1 on easy levels, down to 0.5 on the hardest, so mercy is smaller on hard levels, as in RimWorld.
ST34. **No mid-fight mercy.** Adaptation changes the next stage response and the next plan. It never stops a wave that is on its way or a Strike that has started. The playtest's final wipe felt fair because the players were not ready to jump, and that stays possible.
ST35. **After a loss.** When a single loss removes at least 10 adapt minutes, the next plan is "quiet" or a help beat.
ST36. **Need curve.** The weight of a help beat (section 5) × a need factor from the fleet health share: 2 at 0.4 or less, 1 at 0.7, 0.05 at 1 (the floor; RimWorld uses 0.05 and 0.2 for the same purpose). So help is likely when the crew is down and stays possible otherwise.

### Tests

ST29. **Unit tests.** `StorytellerModelTests`: each score from plain numbers, the memory mix, the adaptation steps and curve (ST32, ST33), the need curve (ST36), the pressure band and the early maximum, the combined clamp of ST12, "condition only lowers", the beat weights, and the Intel texts (each one holds only plan values, and each range holds the true value).
ST30. **Headless tests.**
- `story-profile`: a lost player ship lowers the pressure at once, game time raises it again, and it stays in its band. Existing commands set money, tech and reputation, and the beat weights follow.
- `story-plan`: a forced beat starts through the Pacer, its tell comes first, and a Pacer refusal stops it.
- `story-intel`: a patrol listen raises the level, and the heat panel text names the category. A hunter takeover sets the level to 3.
- Co-op step `story.coop` in `coop-combat`: both machines have the same plan (state hash). The client earns Intel, and the host sees it.
ST31. **Telemetry.** New events `story.profile`, `story.plan`, `story.intel` and `story.beat` (start, result, Intel level at the start). `tools/telemetry` reports the pressure per system. `tools/balance/model.py` gets three crew styles (hoarder, seller, sprinter) to compare the pressure curves before a playtest.

## 4. Inputs and where they exist

| Input | Score idea | Where it exists now | Telemetry now |
|---|---|---|---|
| Fleet strength | firepower and fleet value per player ÷ the tier reference | `Ai/Firepower.cs` (spec 1533), `Diplomacy/Situation.Strength` (spec 1520), `Pursuit/Director.FleetFactor` (spec 902) | snapshots (spec 538), combat census (spec 537) |
| Wealth (beat weights only) | money per player, carried tech × tech value, debt balance (negative) | `GameRoot.GetPlayerState(i, deterministic: true).Money`, `RunState.FleetTech`, tech value (spec 19), `RunState.Debt` | economy (spec 535), snapshots |
| Risk taken | heat at arrival, time at the Strike stage, tech held instead of sold, open informants, contract trail | `RunState.Heat`, `SecondsSinceStrikeWave`, `FleetTech`, `DeliveredUnits`, `ContractsState.Informants` and `Terms` (spec 100 to 104), `RunState.Notoriety` | heat (spec 534), missions (spec 536) |
| Pace (beat weights and section 8 only) | game time per sector, systems visited, blind jumps | `RunState.GameSeconds`, `SectorIndex`, `VisitedNodes`, jump kinds (spec 650, 651) | `run.jump` (spec 1582) |
| Faction anger | reputation, wanted, debt stage, rival role, per faction | vanilla `GetFactionReputation`, vanilla `WantedManager` (crew-wide in vanilla), `Lenders.StageOf` (spec 1407 and later), `FactionsArea.Roles` (spec 640) | reputation changes (spec 535) |
| Condition (lowers only) | fleet health share, crew share, ships lost in the sector | `HunterOrders.HealthFraction`, crew counts, fleet changes | `combat.captured` and fleet changes (spec 1581) |

The last-column values exist only on each machine. The storyteller reads the middle column; telemetry stays a tuning tool. Leaving ships in another system lowers the fleet value that the Pursuit reads (spec 902). RimWorld has the same gap with caravans (section 7); the narrow band keeps it small.

## 5. Beats (parked)

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

Answered on 2026-11-17: 4 (band 0.85 to 1.25), 5 (on), 7 (section 62, 1720 to 1759), 9 (L0: measure first). The others belong to the parked step 2.

1. Does the storyteller have a name in the fiction (for example "the corporate analyst"), or does it stay unnamed? A name makes Intel texts easier to write.
2. Patrol listen: does Intel replace its heat gain of −4 (spec 1122), or come with it?
3. Price and place of bought Intel: shelter stations only, or every trade station at a higher price?
4. The band: is 0.85 to 1.25 enough to feel, and small enough to stay fair?
5. Default on or off for the first playtest of step 1? Off means no visible change; on gives real data sooner.
6. Faction sweep: is it acceptable to scale vanilla faction patrols, or only the police of spec 335?
7. The final spec needs a number block (about 36 requirements) from the coordinator.
8. Storyteller presets, like the RimWorld personalities: is one tuned storyteller enough, or does the new career screen offer presets (section 7)?
9. Long stays: which option of section 8, if any, goes into step 1?

## 7. RimWorld comparison

RimWorld's storyteller is the best-known design of this kind. This section lists what it does, from public sources (section 9), and what Hot Cargo takes from it. The wiki marks the storyteller pages as unverified, and its raid point numbers are verified for version 1.3; later versions can differ.

### How RimWorld does it

**Personalities pace the events.** Each storyteller has the same threat size, and a different schedule:
- **Cassandra:** a threat cycle of 4.6 days "on" and 6 days "off", from day 11. Each on phase sends 1 or 2 major threats, at least 1.9 days apart; about 8.5 per game year. "She'll push you with dangerous events, then give breathing room, then come back to push once more."
- **Phoebe:** 8 days on and 8 days off, from day 13, exactly 1 major threat per on phase, so major threats come 8 to 24 days apart.
- **Randy:** no cycle. An event every 1.35 days on average, a forced major threat after 13 days without one, and raid points × a random 0.5 to 1.5. "He doesn't care if they make a story of triumph or utter hopelessness."

**Threat points come from wealth and colonists.** Raid points = (wealth points + pawn points) × threat scale × starting factor × adaptation factor, from 35 to 10,000.
- Wealth points are 0 up to 14,000 wealth, 2,400 at 400,000 and 4,200 at 1,000,000, and flat above that.
- Buildings count at half. Items and colonists on a caravan away from the map do not count.
- Each colonist adds 15 to 200 points, more in a richer colony.
- The wiki pages describe no smoothing in the wealth count: the "lag" is in the adaptation factor, which changes over days.

**Adaptation lowers the pressure after a loss, and good times raise it.** The game keeps "adapt days":
- They start at 0 (factor 0.8) and cannot rise above 0 in the first 30 days. Then they rise by 1 per day (3 per day below 0, and slower above 60).
- A colonist death removes 20 to 30 days, and a downed colonist 3.5 to 8 days.
- The factor runs from 0.4 (at −30 days) to about 1.47 (at the maximum, 100 days). After a death the colony gets weaker raids for weeks.
- A starting factor of 0.7 rises to 1.0 between day 10 and day 40.

**Population intent balances good and bad events.** Events that add colonists use a factor from the population (8 at 0 colonists, 1 at 5, 0 at 12, −1 at 20) and from the days at the location (0 at day 0, 1 at day 10), with a floor of 0.05 (Randy: 0.2). So a small colony gets wanderers and refugees, and a big one rarely does. The wiki describes Phoebe sending trade caravans or helpful travelers after a tough raid; it describes no formal "good after bad" rule.

**Difficulty scales the threat and the mercy.** Threat scale: 10 % (Peaceful) to 220 % (Losing is fun), custom 0 to 500 %. "Adaptation impact" is 100 % on easy levels and 40 % on Losing is fun, so mercy after a loss is smaller on hard levels. "Wealth-independent mode" replaces wealth with a time curve per map tile that restarts on a new tile ("years until maximum threat", default 12).

**The design idea.** Tynan Sylvester (GDC 2017): "Loss is an essential part of a story, not its conclusion", and "If you're generating the whole story, your mechanics must include loss and recovery." The same talk says that players see more complexity than the game has ("apophenia"), helped by "abstracted feedback" and "long-term relevance".

**Known complaints.** Players feel punished for building: "it gets tedious knowing that enemy raids scale with the value of my colony". This leads to "wealth management": burn corpses, smelt weapons, gift goods away, use dirt floors, park items in caravans, and "accept a lower amount of progress". The wiki says the effect is often overstated and that colonist count is often the real driver. A developer post quoted on Steam says: "The idea that storytellers use only (or even primarily) wealth to determine how to scale threats is a myth." The design softens it with the half count for buildings, the dead zone below 14,000, the caps, adaptation and the wealth-independent mode. It still causes it, because wealth is easy to make and easy to hide, and both are chores.

### What Hot Cargo takes

| RimWorld idea | Hot Cargo | Why |
|---|---|---|
| Threat points from wealth | **Skip.** Heat is the budget already: carried tech sets the heat multiplier (spec 26), fame and time add heat, and the stages spend it. | A second wealth input (money, fleet growth) asks players to hide wealth, the top RimWorld complaint. "Hold or sell" (design pillar) is the one wealth choice, and the fiction explains it. Yarden also rejected heat that grows with fleet size. |
| Buildings at half, dead zone, caps | **Copied already.** The Pursuit fleet factor is a log curve clamped to ± 15 % (spec 902). | Keep it small. Do not make it larger. |
| Pawn points per colonist | **Change.** The co-op slider scales the ship count per extra player (spec 330 to 336). The storyteller never counts players again (ST10). | Co-op players chose the slider; a hidden second factor surprises them. |
| Adaptation (loss lowers, time raises) | **Copy, narrower.** ST32 to ST35: adapt minutes, −20 for a lost ship, a band of 0.85 to 1.15, no change to a wave on its way. | Loss and recovery make the run a story. The band is narrow because the playtest found hunter strength balanced, and the final wipe felt fair. |
| Adaptation impact by difficulty | **Copy.** ST33. | Hard levels promise less mercy. |
| Starting factor and 30-day grace | **Copied already.** The Pursuit ramp (spec 901) and the hunter tier floor (spec 305). | No change. |
| Threat cycle with off days | **Change.** In Hot Cargo the player makes the off phase: a jump cuts heat (spec 28, 650), and each system starts calm (spec 30). The storyteller adds an arrival grace (ST6) and keeps the Pacer gaps (spec 905). | A fixed calendar does not fit systems that the players leave when they want. The jump decision is the core loop and stays the player's. |
| Minimum spacing between major threats | **Copied already.** `MinEventGapSeconds` and the per-kind gaps of the Pacer (spec 905, 906). | No change. |
| Population intent | **Copy as a need curve.** ST36: help beats follow the fleet health share, with a floor of 0.05. | The same shape gives help when the crew is down and keeps it possible otherwise. |
| Good after bad (Phoebe) | **Copy as a rule.** ST35: after a big loss the next plan is quiet or help. | The wiki describes it only as behavior; a rule makes it testable. |
| Personalities | **Later, as presets.** Open question 8. A preset sets the band, the grace and the beat weights. A "Randy" preset keeps its randomness in beat choice, not in wave size. | One tuned storyteller first. In co-op the host picks the preset at the new career, like the enemy slider. Random wave size (× 0.5 to 1.5) undoes the balanced hunter strength. |
| Wealth-independent mode | **Skip as a mode.** Heat already has a time part (spec 24). | Section 8 uses its other idea: a timer per place that restarts on a move. |
| Apophenia and abstracted feedback | **Copy.** Intel (ST17 to ST22) and the run log after each beat (ST21). | A simple model looks smart when the players see its reasons. RimWorld shows the storyteller only by its effects; Hot Cargo adds partial Intel. |
| Caravan wealth gap | **Note.** Ships left in another system lower the fleet value of spec 902. | The narrow band keeps the gain small. |

## 8. Long stays (decision: L0, measure first)

**Playtest context.** Yarden found hunter strength balanced. The final wipe felt fair, because the players were not ready to jump. The co-op playtest of 2026-11-15 had stays of 58 and 111 minutes in the two finished sectors. The balance model assumes 17.5 minutes and 6 missions per sector (BALANCE.md). The players took 10 and 11 missions and finished 6 and 7, about the model's count. So they did not do more missions than the model: the time in and between missions was 3 to 6 times longer. Time heat gave 62 % of their heat. The fixes offered so far (more time heat, heat that grows with fleet size) push the players out by threat. Yarden dislikes both.

RimWorld has no direct answer: a colony stays in one place by design. Two of its ideas still fit, because they reduce the reasons to stay instead of adding threat.

**Option L1: the system runs dry (recommended).** Like population intent's "days at the location" curve, but turned around: the good things of a system fade with the time spent there.
- After `DryStartMinutes` (25) of game time in a system, mission pay and the weights of good events × a factor that falls in a straight line to `DryFloor` (0.5) at `DryEndMinutes` (60). New board offers come more slowly.
- A jump restarts the clock in the new system. A return to an old system continues its clock (the run state saves it per system).
- Tell: a line on the mission panel and in station comms: "Work is drying up here: pay −20 %". Intel is not needed; the players see it at once.
- No heat, no hunters, no fleet input. It does not punish a slow fight or a repair; it only stops a long stay from paying more.
- Risk: players who like a slow pace can feel nagged. The floor (0.5) keeps every mission worth doing.

**Option L2: the corporation learns the system.** Like the wealth-independent timer per map tile: the time in this system raises the precision of the plan, not its size.
- At 1 × the model stay the plan uses the beat list as now. At 2 × it places its beat at the relay to the players' destination. At 3 × it adds a reporting scout to the next stage (spec 910).
- Intel shows it: "They have mapped your routes here."
- It still adds pressure with time, so it is close to "more time heat". It is a better fit than more heat only because it changes where the threat comes from, not how much heat or how many ships.

**Option L0: measure first.** Before L1 or L2, the next playtest telemetry records what the time goes into: travel, fights, repairs, building and trade. The mission records (spec 536) give only a part: bounties took 13 minutes on average and shady bounties 21. If most of the time is repairs or building, L1 is the right tool. If it is travel, smaller systems or faster missions are the fix, not the storyteller.

## 9. Sources

- RimWorld Wiki, [AI Storytellers](https://rimworldwiki.com/wiki/AI_Storytellers): descriptions, population intent, difficulty presets, wealth-independent mode.
- RimWorld Wiki, [Cassandra Classic](https://rimworldwiki.com/wiki/Cassandra_Classic), [Phoebe Chillax](https://rimworldwiki.com/wiki/Phoebe_Chillax), [Randy Random](https://rimworldwiki.com/wiki/Randy_Random): threat cycles and event rates.
- RimWorld Wiki, [Raid points](https://rimworldwiki.com/wiki/Raid_points): the formula, wealth and pawn points, adaptation, the starting factor (verified for 1.3).
- RimWorld Wiki, [Wealth management](https://rimworldwiki.com/wiki/Wealth_management): player strategies and the claim that the effect is overstated.
- Tynan Sylvester, GDC 2017, "RimWorld: Contrarian, Ridiculous, and Impossible Game Design Methods": [slides](https://media.gdcvault.com/gdc2017/Presentations/Sylvester_Tynan_RimWorld_Contrarian_Ridiculous.pdf), [video](https://www.youtube.com/watch?v=VdqhHKjepiE).
- Steam discussions: [the developer quote on wealth](https://steamcommunity.com/app/294100/discussions/0/360671583800707840/) (quoted by a player; the original post was not found), [how much wealth and time affect raids](https://steamcommunity.com/app/294100/discussions/0/1643170269579573147/), [storyteller differences](https://steamcommunity.com/app/294100/discussions/0/3058492406518108068/).
- Not read: the Ludeon forum thread ["Difficulty system not based on wealth"](https://ludeon.com/forums/index.php?topic=43206.0) refuses automated reads.
- Playtest numbers: the telemetry report of the 2026-11-15 co-op playtest (local file, not in the repo).


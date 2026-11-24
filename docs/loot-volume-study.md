# Study: loot volume and building after every fight

Status: decided 2026-11-24. Built: A (2× stack for steel and coil, spec 2410) and B (bay ammo back to vanilla, spec 1835), with a new rule for laser breaks (spec 1834). Rejected: C (price cut) and D (cash-out).

## The question

Players build and upgrade after every fight because the fight leaves more loose loot than the ship can store, and leaving it behind makes no sense (user, 2026-11-24). The building is a storage workaround. Repair after each fight is fine.

So: **how do we stop loot volume from forcing a build, without making building worse?**

Out of scope (rejected by the user): station-only blueprint changes, a refit fee or a loss on removed parts, salvage that favors repair, a build cooldown.

## How the numbers were made

- `econ_loot` (test command, `src/HotCargo/Balance/BalanceArea.cs`): `aispawn pirates 4 <tier>`, then `econ_loot mark`, `econ_loot break salvage|weapon`, `econ_loot report`. It counts the loose units per resource, the enemy cargo, and the player ship's bay tiles.
- Every run used the "fast" economy (enemy drop factor 2). The playtest telemetry of 12 to 14 November shows `econ_difficulty: fast`, so these are play numbers. On "normal", part drops halve.
- "Salvage" breaks every part with salvage damage, like cutting up a wreck with salvage beams. "Weapon" breaks every part by damage. A real fight is in between: the hulk dies by weapons, the wreck by beams. The tables use salvage (the upper end, about 10–40 % above weapon).
- `econ_census` (same file) gives the part, ship and price data for the price cut appendix.

## 1. Loot on the ground per fight

Measured per enemy ship (4 ships per run, `fast`):

| Fight | Steel | Coil | Bullets | Hyperium | Other |
|---|---|---|---|---|---|
| Tier 1, per ship, salvage | 57 | 39 | 0 | 8 | coil2 1, enriched uranium 1.5, processor 0.75 |
| Tier 1, per ship, weapon | 50 | 27 | 0 | 4 | |
| Tier 3, per ship, salvage | 129 | 54 | **166** | 13 | coil2 2, enriched uranium 2, processor 0.75 |
| Tier 3, per ship, weapon | 121 | 33 | **110** | 5 | |

- **Bullets come from the 1:1 bay drop** ([#209](https://github.com/game-design-driven/cosmoteer-hot-cargo/pull/209) "cargo comes back in full", spec 2370). The tier-3 pirates carried 440 bullets in their bays, and all 440 dropped in both runs. Vanilla drops 0 ammo from a bay that weapons break (`AmmoDropRates`: `StorageDestroyed = 0%`), but all of it from a bay that a salvage beam cuts (`StorageSalvaged = 100%`).
- These pirates carried no steel or coil cargo. All steel and coil on the ground came from their parts, not from #209.
- Storage: the starter Model-L has **4 bay tiles** (one 2×2 bay). A tile holds one stack of one resource: steel 40, coil 40, bullets 20, hyperium 10, processor 5.

Two typical fights:

| Fight | On the ground | Bay tiles needed |
|---|---|---|
| Tier 1, 2 ships | 114 steel, 78 coil, 16 hyperium, 2 coil2, 3 enriched uranium, 2 processors | 10 |
| Tier 3, 3 ships | 386 steel, 160 coil, 498 bullets, 38 hyperium, 6 coil2, 6 enriched uranium, 2 processors | 47 |

## 2. What share fits, per lever

The player fills the free tiles with the most valuable stacks first. "4 tiles": the starter ship. "12 tiles": a ship with 3 bays.

| Lever | Tier 1, 2 ships, 4 tiles | Tier 3, 3 ships, 4 tiles | Tier 3, 3 ships, 12 tiles |
|---|---|---|---|
| Today | 10 tiles needed; steel + coil stored 21 %; value stored 61 % | 47 tiles; steel + coil 7 %; value 44 % | steel + coil 29 %; value 78 % |
| A. 2× stack for steel and coil | 8 tiles; steel + coil 40 %; value 77 % | 40 tiles; 15 %; 52 % | **steel + coil 100 %**; value 88 % |
| B. Bay ammo back to vanilla (0 % when weapons break the bay) | no change (no bay ammo at tier 1) | 31 tiles; 7 %; 45 % | 29 %; 80 % |
| A + B | 8 tiles; 40 %; 77 % | **24 tiles**; 15 %; 53 % | **100 %**; value 90 %; all units 74 % |
| A + B + salvage drop factor 0.6 → 0.4 | 7 tiles; 40 %; 78 % | 22 tiles; 22 %; 64 % | 100 %; value 96 % |
| C. Price cut (steel 12, coil 50) | no volume change | no volume change | no volume change |

Notes:
- **A** is the strongest volume lever for steel and coil. It doubles what each tile holds and changes nothing else in combat or building. Crew trips and crate size stay the same (they use `MaxPerNugget`, appendix A3).
- **B** removes up to a third of the tier-3 volume (498 → 168 bullets) when weapons break the bays. Bays that a salvage beam cuts still drop their ammo, as in vanilla. Bullets are cheap (4 each) and useless for building, but they take bay tiles and invite pickup.
- **The salvage factor** cuts volume and income alike (finding 11 in BALANCE.md set it to 0.6 for income). It does not separate "too much to carry" from "too little to earn".
- **C** does not change volume. It halves what is left behind: the tier-3 fight's steel and coil are worth 25,650 today (3.8 tier-3 missions) and about 12,600 after. Leaving it hurts less, but it is still worth taking. The price cut has many side effects (appendix A1, A2).
- A starter ship cannot store a tier-3 fight with any of these levers. It needs more bays, which is building. This is a reason for lever D.

## 3. Other levers that cut "can't carry it all"

| Lever | Effect | Side effects | Knob | Effort |
|---|---|---|---|---|
| **D. Cash out the overflow.** Loose crates that the ship cannot store sell where they lie, for example at the station refund (50 %), with a button on the collect tool or automatically after the collect order | Nothing is wasted, and nothing forces a build. The money buys upgrades later, at a station, in one session. | Money income rises by the part that players left behind. A player with storage still prefers to store. Co-op: one sim input, the money goes to the clicker. | new MP input; `GetResourceSellRefund` for the price; a tunable refund factor | medium |
| **E. Junk stays, decay off for loose crates** | Loose crates already never expire: vanilla `Nugget` has no lifetime, and Hot Cargo debris decay removes junk pieces, not crates (spec 1240). Players can come back for them. | Players must remember and fly back; does not solve storage. | none (state today) | none |
| **F. Second ship as a hold** | Hot Cargo autonomous ships with the Looter preset already collect and sell. | A second ship is building too, but one time. | spec 1076 to 1080 | none (exists) |
| **G. 2× stack for other salvage forms too** (hyperium 10, processor 5, enriched uranium 10) | A few more tiles free | Each stack change is a balance change of its own | rules `MaxStackSize` | small |

## 4. Recommendation

1. **A: 2× stack for steel and coil** (and `ResourcesPerStackSprite = 2`, appendix A3). With 3 bays, a whole tier-3 fight's steel and coil fit. Small effort, no combat or price side effects.
2. **B: bay ammo back to vanilla** in the 1:1 bay rule: 0 % when weapons break the bay (spec 2370 keeps cargo like steel at 1:1). It removes up to 30 % of the tier-3 volume. Small effort.
3. **D: cash out the overflow** for what is still left. It ends "leaving it makes no sense" for every resource and every ship size, without touching building. Medium effort.
4. **Skip the price cut (C) for this goal.** It does not change volume and moves thieves, hunters, rewards and event payouts (appendix A1, A2). Keep it only if cheaper repair is a goal of its own; then take the counter-changes of appendix A4.
5. Keep the salvage drop factor at 0.6: lowering it cuts income too.

## A1. Price cut: what reads prices or ship value

A part's cost is Σ (units × `BuyPrice`). So the cut changes every ship value, not only the steel price.

### Does not change

| System | Why | Code or rules |
|---|---|---|
| Enemy ship tiers and fleet sizes (vanilla) | Each built-in ship has a fixed `Tier` in the ship lists. Fleets come from a fixed tier table. | `builtin_ships/*/*.rules` (`Tier=`), `modes/career/fleet_tier_remap_table.rules` |
| Mission, bounty and contract money, and fame per tier | Fixed numbers: tier value maximum × 0.05 | `modes/career/tier_tables.rules` (`TierValueMaximums`, `ValueMoneyMultiplier`) |
| Hunter tier and spawn count base, difficulty dial (`EnemyScale`) | Go by tier and player count | `Hunters/HunterSpawner.cs:121`, `Scaling/` |
| Mercenary hire price, pay-offs, hold fee, loans, overdraft, relay fee and bribe price | Tier money or tech value | `Diplomacy/Quotes.cs:68, 88, 101–113, 144`, `Debt/`, `Relays/RelaysArea.cs:77–79` |
| Tech value and the Buyer | Fixed tier values | `Tech/TechPatches.cs:20–38`, `Journey/Buyer.cs:129` |
| Salvage cut | Steel and coil stay below the component price | `Debris/SalvageDrops.cs:56` (spec 1832) |
| Crew trips and crate size | Both use `MaxPerNugget` (10), not the stack | section A3 |

### Changes

| System | What changes | Size | Code |
|---|---|---|---|
| Ship value and build cost | Every steel or coil part costs less | Ships −25 to −34 % (table A2.1). Armor, corridor, storage, crew quarters, thrusters −50 %. Small reactor −18 %, ion beam −8 %. | `PartRules.Cost` |
| Repair and rebuild money | Steel and coil units cost half | −50 % for those units | vanilla |
| **Thief size** | A thief design grows until it reaches 0.85 × the fixed tier value | 2 → 4 segments at tier 1 (table A2.3) | `Thieves/ThiefShip.cs:131` |
| **Hunter count** | Fleet value against the fixed tier value, 0.15 per doubling, clamped at ±0.15 | About −9 % hunter ships | `Pursuit/Director.cs:30` |
| Surrender payout and surrender price | Uses ship value | About −30 % | `Diplomacy/Quotes.cs:47`, vanilla `GetSurrenderForPrice` |
| Reputation loss for a kill (vanilla) | Uses ship value | About −30 % | vanilla `GetReputationValue` |
| AI target weighting (Focus fire, "strongest", Small or Large class) | Part and ship value | Aim moves toward reactors and ion beams | `ShipAi/ShipAiController.cs:591, 775, 911` |
| Goods delivery events (steel, coil) | Reward = units × sell price × 0.75 | −50 %: 100 steel pays about 900 instead of 1,900 | `Events/GoodsDelivery.cs:71` |
| Event and hulk loot, tolls, repair payments, the wandering trader | Counted in units | Loot worth half; trader return 2× → about 4–5× | `mod/events/events.rules`, `mod/events/hulks.rules` |
| Relay bribe in goods | Goods at 0.8 × `BuyPrice` against a fixed bribe price | 2× the units | `Relays/RelaysArea.cs:229–233` |
| Thief collect trigger | Sums the value of dropped player cargo | 2× the units | `Thieves/Thieves.cs:174` |
| Autonomous ships: loot filters, trade money, cargo full | Sell value and stack size | Steel (12) fails the "≥ 20" filter. Scavengers carry 2× the units before full. | `Autonomy/Funnel.cs:31, 57`, `Autonomy/AutonomyWorld.cs:45, 104, 210, 231` |
| Credit cap of a build | Buy cost | 2× the steel or coil per credit | `Debt/CreditCap.cs:30` |
| Mod part prices | Part cost | Vault 200 → 96, Funnel −50 %, Computer −15 % | `mod/autonomy/*.rules`, `mod/tech/tech_vault.rules` |

Test-only and telemetry-only reads are left out.

## A2. Price cut: scenarios

### A2.1 Ship value

Built-in combat ships (all factions), mean value. Starter: Hot Cargo's Model-L.

| Ship | Before | After | Change | Steel + coil share of value |
|---|---|---|---|---|
| Starter Model-L | 57,750 | 39,094 | −32 % | 64 % |
| Tier 1 (33 ships) | 55,741 | 38,949 | −30 % | 60 % |
| Tier 3 (35 ships) | 79,791 | 52,614 | −34 % | 68 % |
| Tier 6 (39 ships) | 151,110 | 103,432 | −32 % | 64 % |
| Tier 12 (44 ships) | 678,818 | 504,568 | −26 % | 51 % |

### A2.2 Encounters

| Encounter | Before | After |
|---|---|---|
| Vanilla tier-3 pirates, 3 ships | 3 ships, about 239,000 value | **3 ships**, about 158,000 value. Same ships, same strength. |
| Hunter stage response, tier 1, one Model-L | fleet factor 0.99 | 0.91: 4 hunters become 3.6 on average |
| Hunter stage response, tier 3, a typical tier-3 player ship | 0.97 | 0.88 (−9 %) |
| Thief, tier 1 | 2 segments, value 63,200 | **4 segments**, value 53,300 |
| Thief, tier 3 | 4 segments | **6 segments** |
| Thief, tier 4 | kit 0 × 5 segments | kit 1 × 5 segments |
| Thief, tier 6 | kit 1 × 5 segments | kit 2 × 4 segments |
| Thief, tier 10 | 8 segments | 10 segments |

Enemy and player ships lose about the same share of value, so vanilla fights stay as they are. Thieves get about 1.5–2× stronger at the same tier.

### A2.3 Money

| Scenario | Before | After |
|---|---|---|
| Tier-3 standard mission (× `MissionMoneyScale` 1.5) | 5,625–6,750 | the same. It buys 8.5 % of a tier-3 ship before and 12.8 % after (about 1.5× more ship). |
| Tier-1 standard mission | 3,000–4,500 | the same, about 1.4× more ship |
| Mercenary hire, tech value, Buyer price | tier money | the same, about 1.4× more ship |
| Salvage of a tier-3 kill: steel and coil part drops (before the difficulty drop factor) | 78 units, about 3,100 | 78 units, about 1,500 |
| Salvage of a tier-6 kill | 154 units, about 5,500 | 154 units, about 2,700 |
| Steel factory: 1 iron → 1 steel per second | 3,600 steel per hour, 90,000 at buy price; margin over iron 5 per steel | 7,200 steel per hour (2× per cycle), 86,400; margin 2 × 12 − 20 = 4 per cycle |
| Coil factory: 1 copper → 1 coil per 1.5 s | 2,400 coil per hour, 240,000 | 4,800 coil per hour, 240,000; margin unchanged (20 per cycle) |
| Coil2 factory: 2 coil + 1 copper → 1 coil2 per 2 s | input 280, output 300 | input 180, output 300: margin 6× (20 → 120) |
| Processor factory: 5 coil + 3 gold → 1 processor | input 2,000, output 2,500 | input 1,750: margin +50 % |

### A2.4 Building and storage

| Ship | Steel, coil units | Storage tiles at stack 40 | At stack 80 | Crew trips (10 per trip) | Steel + coil buy cost |
|---|---|---|---|---|---|
| Model-L | 562, 227 | 21 | 11 | 80 (unchanged) | 36,750 → 18,094 |
| Tier 1 (median) | 368, 255 | 17 | 9 | 63 | 34,700 → 17,166 |
| Tier 3 (median) | 872, 324 | 31 | 16 | 121 | 54,200 → 26,664 |
| Tier 6 (median) | 1,832, 463 | 58 | 29 | 231 | 92,100 → 45,134 |

A storage bay (2×2) holds 4 tiles. So a tier-3 rebuild needs about 8 bays of stock now and about 4 after.

## A3. The stack question

**What a stack is.** `MaxStackSize` is the number of units in one storage tile. Other resources already use other stacks: iron, copper and processor 5, helion ore 5, stolen tech 4, mod items 1, steel, coil, coil2 and tristeel 40. A crate in space holds `MaxPerNugget` units (steel 10, coil 10), not a stack.

What `MaxStackSize` controls (decompiled game):

| Use | Code | Effect of 80 |
|---|---|---|
| Units in a storage (flex) tile | `FlexResourceGrid.cs:171, 344, 734` | 2× per tile. The ship's storage capacity for steel and coil doubles. |
| Units in a typed part buffer: factory input and output, ammo buffers | `TypedResourceGridRules.cs:59` (tiles × stack) | The steel factory output (2×2 tiles) holds 320 instead of 160. With 2 per cycle, that is the same 160 cycles. |
| Station stock and trade ship cargo | `TradeRoutesManager.cs:221, 248, 415`, `ShipSpawner.cs:402` | 2× the units, the same money |
| Enemy and wreck cargo at spawn | `ShipSpawner.cs:402` | 2× the units. With the 1:1 bay drop (spec 2370, from #209) a broken bay drops 2× the units as crates, so 2× the crates in space. |
| Trade and transfer window step | `CommTradeTab.cs:198`, `CrewAndResourceTransferWindow.cs:247` | Steps of 80 |
| Right click "collect one stack" from a crate | `PlayerInputHandler.cs:1074` | 80 |
| The stack picture of a tile | `StackSprites` index = (units − 1) / `ResourcesPerStackSprite` | Steel and coil have 40 pictures and `ResourcesPerStackSprite` 1, so a tile looks full at 40. **Set `ResourcesPerStackSprite = 2`.** |
| Crew load per trip | `IResourceSource.MaxPickUp` = `MaxPerNugget` | No change: 10 per trip |
| Crate size in space | `MaxPerNugget` | No change: 10 per crate |

**Is it odd?** No. Stacks already differ by resource, and the game reads the stack everywhere it needs it. Two follow-ups: `ResourcesPerStackSprite = 2`, and twice as many crates from a broken bay.

**Coil2 and tristeel.** Leave their stack at 40. Their price and volume do not change. Coil2 is made from coil, so its factory margin grows 6× (table A2.3). There is no buy-and-sell loop: a station pays 50 % of `BuyPrice` (`ResourceSaleRefund`, `career.rules`). If the coil2 margin matters, cut coil2 to about 200.

**If "crate" means a crate in space or a crew load**, the field is `MaxPerNugget`, not `MaxStackSize`. Doubling `MaxPerNugget` halves crew trips and crates. It speeds up building and hauling, which the stated goal does not ask for.

## A4. Price cut: counter-changes

| Side effect | Counter-change | Knob |
|---|---|---|
| Thieves 1.5–2× stronger | Size thieves by a fixed price table, or multiply the target by the value ratio (about 0.69) | `ThiefShip.ValueShare` is a `const` (0.85). Make it a tunable first. |
| Hunters −9 % | `Pursuit.FleetReferenceShare` 1 → 0.69 | exists |
| Rewards buy about 1.4–1.5× more ship | Keep it (it serves the goal), or `Journey.MissionMoneyScale` 1.5 → about 1.05 for the same ship value per mission. Middle ground: 1.25. | exists |
| Salvage income from steel and coil halves | Fine for the goal. Raise `Debris.SalvageDropFactor` if salvage feels thin. | exists |
| Goods delivery rewards halve | Double the steel and coil units of the delivery events | rules (`GoodsDelivery`) |
| Wandering trader return 2× → 4–5× | Double the steel and coil units in the trades | `mod/events/events.rules:879–880` |
| Autonomy loot filter "≥ 20" skips steel | Retune the filter steps | `Autonomy/Funnel.cs` presets |
| Surrender payout −30 % | Accept, or scale by the value ratio | `Diplomacy/Quotes.cs:47` |
| Stack picture full at half | `ResourcesPerStackSprite = 2` for steel and coil | rules |
| Coil2 margin 6× | Accept, or coil2 `BuyPrice` about 200 | rules |
| Steel price must be an int | 12 or 13 | rules |

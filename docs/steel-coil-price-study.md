# Study: cheaper steel and coil

Status: analysis only. Nothing in the game changed. The user decides.

## The proposal

- Steel and coil cost 50 % less to buy and to sell.
- The steel and coil factories make 2× per cycle, so a factory keeps its value.
- Steel and coil hold 2× per stack (`MaxStackSize` 40 → 80).

Goal: less reason to rebuild after every fight, more reason to build in fewer, focused sessions.

## How the numbers were made

- `econ_census` (test command, `src/HotCargo/Balance/BalanceArea.cs`) logs every part, every built-in ship and every thief kit with its resources, from the loaded rules. Run it in the headless sim: a test script with `newgame run`, `wait 5`, `econ_census`, `quit`.
- "After" prices: steel 12, coil 50. `ResourceRules.BuyPrice` is an `int`, so steel cannot be 12.5. Choose 12 (−52 %) or 13 (−48 %).
- Ship values: the vanilla part cost (`PartRules.Cost` = Σ units × `BuyPrice`), summed over the ship's blueprint parts.

## 1. What reads prices or ship value

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
| Crew trips and crate size | Both use `MaxPerNugget` (10), not the stack | section 3 |

### Changes

| System | What changes | Size | Code |
|---|---|---|---|
| Ship value and build cost | Every steel or coil part costs less | Ships −25 to −34 % (table 2.1). Armor, corridor, storage, crew quarters, thrusters −50 %. Small reactor −18 %, ion beam −8 %. | `PartRules.Cost` |
| Repair and rebuild money | Steel and coil units cost half | −50 % for those units | vanilla |
| **Thief size** | A thief design grows until it reaches 0.85 × the fixed tier value | 2 → 4 segments at tier 1 (table 2.3) | `Thieves/ThiefShip.cs:131` |
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

## 2. Scenarios

### 2.1 Ship value

Built-in combat ships (all factions), mean value. Starter: Hot Cargo's Model-L.

| Ship | Before | After | Change | Steel + coil share of value |
|---|---|---|---|---|
| Starter Model-L | 57,750 | 39,094 | −32 % | 64 % |
| Tier 1 (33 ships) | 55,741 | 38,949 | −30 % | 60 % |
| Tier 3 (35 ships) | 79,791 | 52,614 | −34 % | 68 % |
| Tier 6 (39 ships) | 151,110 | 103,432 | −32 % | 64 % |
| Tier 12 (44 ships) | 678,818 | 504,568 | −26 % | 51 % |

### 2.2 Encounters

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

### 2.3 Money

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

### 2.4 Building and storage

| Ship | Steel, coil units | Storage tiles at stack 40 | At stack 80 | Crew trips (10 per trip) | Steel + coil buy cost |
|---|---|---|---|---|---|
| Model-L | 562, 227 | 21 | 11 | 80 (unchanged) | 36,750 → 18,094 |
| Tier 1 (median) | 368, 255 | 17 | 9 | 63 | 34,700 → 17,166 |
| Tier 3 (median) | 872, 324 | 31 | 16 | 121 | 54,200 → 26,664 |
| Tier 6 (median) | 1,832, 463 | 58 | 29 | 231 | 92,100 → 45,134 |

A storage bay (2×2) holds 4 tiles. So a tier-3 rebuild needs about 8 bays of stock now and about 4 after.

## 3. The stack question

**What a stack is.** `MaxStackSize` is the number of units in one storage tile. Other resources already use other stacks: iron, copper and processor 5, helion ore 5, stolen tech 4, mod items 1, steel, coil, coil2 and tristeel 40. A crate in space holds `MaxPerNugget` units (steel 10, coil 10), not a stack.

What `MaxStackSize` controls (decompiled game):

| Use | Code | Effect of 80 |
|---|---|---|
| Units in a storage (flex) tile | `FlexResourceGrid.cs:171, 344, 734` | 2× per tile. The ship's storage capacity for steel and coil doubles. |
| Units in a typed part buffer: factory input and output, ammo buffers | `TypedResourceGridRules.cs:59` (tiles × stack) | The steel factory output (2×2 tiles) holds 320 instead of 160. With 2 per cycle, that is the same 160 cycles. |
| Station stock and trade ship cargo | `TradeRoutesManager.cs:221, 248, 415`, `ShipSpawner.cs:402` | 2× the units, the same money |
| Enemy and wreck cargo at spawn | `ShipSpawner.cs:402` | 2× the units. With the 1:1 bay drop (spec 1814, from #209) a broken bay drops 2× the units as crates, so 2× the crates in space. |
| Trade and transfer window step | `CommTradeTab.cs:198`, `CrewAndResourceTransferWindow.cs:247` | Steps of 80 |
| Right click "collect one stack" from a crate | `PlayerInputHandler.cs:1074` | 80 |
| The stack picture of a tile | `StackSprites` index = (units − 1) / `ResourcesPerStackSprite` | Steel and coil have 40 pictures and `ResourcesPerStackSprite` 1, so a tile looks full at 40. **Set `ResourcesPerStackSprite = 2`.** |
| Crew load per trip | `IResourceSource.MaxPickUp` = `MaxPerNugget` | No change: 10 per trip |
| Crate size in space | `MaxPerNugget` | No change: 10 per crate |

**Is it odd?** No. Stacks already differ by resource, and the game reads the stack everywhere it needs it. Two follow-ups: `ResourcesPerStackSprite = 2`, and twice as many crates from a broken bay.

**Coil2 and tristeel.** Leave their stack at 40. Their price and volume do not change. Coil2 is made from coil, so its factory margin grows 6× (table 2.3). There is no buy-and-sell loop: a station pays 50 % of `BuyPrice` (`ResourceSaleRefund`, `career.rules`). If the coil2 margin matters, cut coil2 to about 200.

**If "crate" means a crate in space or a crew load**, the field is `MaxPerNugget`, not `MaxStackSize`. Doubling `MaxPerNugget` halves crew trips and crates. It speeds up building and hauling, which the stated goal does not ask for.

## 4. Counter-changes

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

## 5. Does it serve the goal?

- It makes a rebuild cost half the money for steel and coil (−25 to −34 % for a whole ship). The work stays the same: the same units, the same crew trips.
- So money stops being a reason not to rebuild. Rebuilding gets cheaper, not rarer.
- The larger stack doubles the stock that a ship can hold. A player can keep a full rebuild in store and build in one session.
- If the aim is fewer rebuilds, the price cut alone works against it. The stack change and fixed rewards help it.

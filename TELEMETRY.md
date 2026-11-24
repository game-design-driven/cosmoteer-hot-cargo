# Telemetry

Hot Cargo records each game session to a JSONL file, so test runs and playtests can be analyzed afterwards.
Spec: `spec/36-telemetry.md` (requirements 530 to 545 and 1580 to 1593). Code: `src/HotCargo/Telemetry/`. Analysis: `tools/telemetry/`.

## Files

- Folder: `<mod folder>/telemetry/` on the PC, for example `%USERPROFILE%\HotCargoSlots\t3\Mods\hot_cargo\telemetry\` for a test slot.
  `scripts/deploy.sh` and the player installers keep this folder (spec 1989).
- One file for each session: `<run id>__<session id>.jsonl`. A session is one game from its start or load until it closes.
- A run is one career. The run ID is in the career save (path `HotCargoTelemetry`), so a loaded game continues the run ID.
  A co-op client that joins a running game or resyncs gets the run ID with the host's game state. A new co-op game gets its own run ID on each machine;
  the `seed` and `players` in `session.start` match on all machines of that game.
- A file that reaches `MaxFileMegabytes` continues in `<run id>__<session id>.part<N>.jsonl`. Each part starts with a `file.part` line.
- At session start the oldest files go until the folder is below `MaxFolderMegabytes`. The client copies in `players/` count too.
- Co-op: each client also sends its files to the host (spec 1592, 1593). The host keeps a copy of each in `telemetry/players/<player name>/`, under the client's file name. The client keeps its own files. Chunks of 32 KB go over a reliable RPC on the game's MP connection, one at a time from the length that the host confirmed, so a late join, a resync or a reconnect sends nothing twice and skips nothing. A closing client sends its rest once. Lines that a client writes after its last chunk reached the host arrive at its next join of the same run.

## Event format

Each line is one JSON object. Every event has these fields:

| Field | Meaning |
|---|---|
| `e` | Event name, for example `heat.change`. |
| `t` | Real seconds since the session started. |
| `gs` | Game seconds of the run (`RunState.GameSeconds`). It stops after a win. A load continues from the save. |
| `ss` | Sim seconds of this session. It keeps counting after a win. |
| `sec` | Sector index. |
| `sys` | System name. |
| `tier` | Danger tier of the system (low tier). |
| `arch` | Sector type ID, for example `hc_trade_hub` or `basic`. |
| `heat` | Heat after the event. |
| `tech` | Fleet tech count of the last sim tick. |

Numbers have at most 3 decimals.

## Events

Category switches are in the `Telemetry` tunables section. The table names the switch of each event.

| Event | Switch | Fields |
|---|---|---|
| `session.start` | Run | `schema`, `run`, `session`, `session_no` (unique for the run on this machine, also after a load of an older save), `new_run`, `loaded`, `wall_utc`, `game_version`, `mod_version` (`1.0.0+<commit>`), `tunables_hash` (of the file), `tunables_full_hash` (of the values in effect), `seed`, `map`, `map_size_index`, `combat_difficulty`, `econ_difficulty`, `crew_scarcity`, `ironman`, `pressure` (run mode pressure: `heat_rate`, `hunter_ships`, `hunter_tiers`, `contract_fees`, `tech_value`), `money_reward_factor`, `fame_reward_factor`, `players`, `enemy_scale_setting`, `enemy_scale_factor`, `local_player`, `mp`, `host`, `autotest`, `label`, `cores`, `os`, `tunables` (every tunable value) |
| `session.end` | Run | `reason` (`game_closed`, `game_closed_after_loss`, `replaced`, `process_exit`), `result`, `real_s`, `sim_s`, `paused_s`, `build_s`, `play_s` (real time split, spec 1591), `events`, `dropped`, `overhead_ms`, `overhead_pct_wall`, `writer_ms`, `frames`, `frame_ms`, `ticks`, `tick_ms`, `trace_missing` |
| `run.new`, `run.loaded` | Run | `starting_tech`; `result`, `sessions` |
| `run.save` | Run | `caller` (who saved: a save, an autosave, a co-op sync) |
| `run.jump` | Run | `from`, `to`, `kind` (View, RelayWatched, RelayClear, Blind), `heat_kept` (share of heat the jump keeps), `stay_s`, `time_use` and `time_any` (time use of the stay, see below), `open_contracts`, `jumped` and `left` (jump ships, see below). The first tick with player ships in the new system takes a snapshot (`why` = entry). |
| `system.enter` | Run | `from`, `first_visit`, `prev_stay_s`, `prev_stay_real_s`, `prev_time_use`, `prev_time_any`, `prev_missions_done`, `prev_missions_accepted`, `heat_before`, `jump_kind`, `heat_kept`, `stages`, `peak_tier`, `visited` |
| `system.poi` | Run | `poi` |
| `run.won`, `run.lost` | Run | `reason`, `score`, `delivered`, `notoriety`, `peak_tier`, `money` (per player). A snapshot follows. |
| `heat.change` | Heat | `src`, `caller`, `amount` (asked), `delta` (applied), `before`, `after`, `stages` |
| `heat.accrual` | Heat | `window_s`, `time`, `trail` (heat summed over the window), `mult` (heat multiplier with the run mode pressure), `time_per_min` (time heat rate with every factor), `trail_per_min` |
| `heat.stage` | Heat | `stage`, `name`, `live_hunters`, `since_entry_s` |
| `heat.strike_wave` | Heat | `live_hunters` |
| `tech.count` | Tech | `from` (null for the first count of a session), `to`, `delta`, `cause`, `hints` |
| `tech.value` | Tech | `from`, `to`, `buyer_unit`, `buyback`, `notoriety`, `peak_tier` |
| `tech.vault_hit` | Tech | A hit that costs a vault tech: the hit that destroys it (spec 65), or the end of a deconstruction. `ship` (ID), `name`, `player`, `cause` (`vault_destroyed`, `vault_deconstructed`, `vault_damage`), `damage`, `health`, `max_health`, `tech_before`, `tech_after`, `destroyed` (false for a deconstruction) |
| `tech.loss_timer` | Tech | `state` (`idle`, `running`, `paused`), `reason` of a pause (`local_jump`, `jump_limbo`, `ships_elsewhere`), `timer_running`, `elapsed_s`, `left_s`, `waiting` (ships by system, for `ships_elsewhere`). At each state change. |
| `econ.money` | Economy | `player`, `amount`, `before`, `after`, `src`, `caller` |
| `econ.fame` | Economy | `amount`, `after`, `src`, `caller`, `std_mission_fame` |
| `econ.reputation` | Economy | `faction`, `from`, `to` |
| `econ.trade`, `econ.trade_pickup` | Economy | `trade` (Buy, Sell, SellBack, ...), `resource`, `amount`, `unit_price`, `total`, `price_src` (`order`: the price of the crew job; `current`: the station price at delivery), `is_tech`, `buyer`, `crew_player`, `from`, `from_side`, `to`, `to_side`, `tech_value`. Pickups only for tech. |
| `mission.offered` | Missions | mission fields, `list` (station, system, galaxy), `terms` |
| `mission.accepted` | Missions | mission fields, `terms`, `open_contracts` |
| `mission.ended` | Missions | mission fields, `result` (success, timeout, lost, fail, abandoned), `duration_s` (run game seconds since the accept; the save keeps the accept time), `terms`, `sold_out` |
| `event.shown` | Story | `event`, `serial`, `page_tier`, `page_tech_value`, `chain_page` |
| `event.choice` | Story | `serial`, `player`, `outcome` (all outcome rules fields) |
| `event.result` | Story | `serial`, `title`, `lines` (one line per effect) |
| `chain.start`, `chain.stage` | Story | `chain`, `station`, `from_stage`, `stage`, `part`, `ended`, `result`, `flags`, `story` |
| `story.profile` | Story | `adapt` (adapt minutes), `pressure`, `impact`, `sector`, `ships_lost`, `crew_killed` (losses of the last visit). Once per system entry (spec 1730). |
| `combat.census` | Combat | `live` (ships by side), `factions`, `stasis_ships`. At the first visit of a system. |
| `combat.spawn` | Combat | ship fields, `from_stasis`. Every non-player ship that appears after the census, without derelicts (the pieces of a broken ship). |
| `combat.destroyed` | Combat | `ship`, `name`, `player`, `side`, `faction`, `hunter`, `value`, `tags`. A ship that became a derelict, once: a broken ship counts when its last live piece is a wreck, with the fields of the ship before it broke (spec 1580). |
| `combat.captured` | Combat | `ship`, `name`, `player` (new), `old_player`, `old_side`, `old_faction`, `hunter`, `how` (`claim`: crew took a wreck; `joined`: a friendly ship joined; `test`, `debug`, `other`), `value`, `drive`, `tech`, `caller`. A ship that became a player ship. |
| `combat.fleet_left` | Combat | `ship`, `name`, `player`, `reason` (`left_behind`: it stayed at a jump; `resync`: gone after a co-op resync; `control_lost`: it became another side's ship), `value`, `tech`. A wreck is `combat.destroyed`. |
| `combat.fight` | Combat | `start_gs`, `end_gs`, `duration_s`, `fight_sec`, `sides`, `damage` and `destroyed` by side, `kills` (hunters and hostiles), `losses` (player ships), `crew_lost`, `windows`. Damage windows and wrecks joined until `FightGapSeconds` pass without either (spec 1588). |
| `combat.removed` | Combat | ship fields, `src`, `caller`. A ship that left for good: a hunter that jumps out (`flee_jump`), a trader that exits. |
| `combat.crew_died` | Combat | `player`, `cause`, `carried_tech` |
| `thief.trigger` | Combat | `group`, `tier`, `value`, `threshold`, `thieves`. A thief group stops fighting to collect spilled player cargo (spec 1778). |
| `thief.end` | Combat | `group`, `tier`, `outcome` (`escaped`, `died`), `phase`, `stolen` (cargo value that escaped). One thief leaves the fight for good (spec 1782). |
| `combat.damage` | Combat | per side of the damaged ship: `damage`, `repaired`, `parts_lost`. Summed over `AccrualSeconds`. Side `vault` holds the Tech Vault part of the player damage again. |
| `debt.overdraft` | Economy | `player`, `lender` (faction ID), `amount`, `balance`, `new` (the overdraft opened the debt). Money below 0 became a debt (spec 1417). |
| `debt.hail` | Economy | `player`, `lender`, `balance`, `cause` (`overdraft`). The lender of an overdraft debt gets in touch (spec 1418). |
| `dip.hail` | Diplomacy | AI fields, `player`, `kind` (`offer_surrender`, `demand_surrender`, `other`), `price`. A vanilla AI hail. |
| `dip.surrender` | Diplomacy | AI fields, `player`, `by` (`players`, `ai`), `price`, `accepted`. A vanilla surrender input. |
| `dip.deal` | Diplomacy | AI fields, `player`, `offer` (the hail choice, spec 561 to 596), `amount`, `price`, `chance`, `roll`, `accepted`. Chance, roll and accepted only for a choice with a roll. |
| `ai.switch` | Ai | `ship`, `name`, `side`, `ai`, `from`, `to` (module types), `integrity`, `dist` |
| `snap` | Snapshots | `why` (timer, entry, result, test), `stay_s`, `time_use`, `time_any`, `result`, `stages`, `notoriety`, `peak_tier`, `tech_value`, `buyer_mult`, `heat_mult`, `enemy_scale_factor`, `fame`, `money`, `score`, `delivered`, `open_contracts`, `trail_per_min`, `mission_log`, `pending_informants`, `waiting_hunter_groups`, `events_seen`, `chains`, `crew`, `ships` (value, blueprint, integrity, parts, firepower, vaults, crew of each player ship), `fleet_value`, `fleet_blueprint`, `fleet_firepower`, `fleet_parts`, `vaults`, `live` (ships by side), `stasis`, `ai` (sums, see below) |
| `perf` | Perf | `window_ms`, `frames`, `fps`, `paused_s`, `build_s`, `play_s` (real time of the frames while paused, in build mode, in play), `frame_mean_ms`, `frame_p50_ms`, `frame_p95_ms`, `frame_p99_ms`, `frame_max_ms`, `frames_over_33ms`, `ticks`, `tick_mean_ms`, `tick_max_ms`, `tick_total_ms`, `tele_ms`, `tele_pct_wall`, `tele_pct_tick`, `tele_writer_ms`, `tele_events`, `tele_bytes`, `tele_pending`, `tele_dropped`, `gc0`, `gc1`, `gc2`, `gc_pause_ms`, `heap_mb`, `working_set_mb` |
| `mp.players` | Coop | `from`, `to` |
| `mp.desync` | Coop | `input_tick`, `host`, `expected` (a test resync) |
| `mp.game_loaded` | Coop | `host`, `players`. A co-op game that the host's state rebuilt: a join or a resync. |
| `note` | Run | A playtest note (spec 1984 to 1987): `text`, `player` (the player's name), `local_player`, `pressed_real_s` (real seconds at the key press), `screen` (what the player saw: `event`, `comms`, `build`, `dialog`), `screenshot` (the file in `notes/`, or null), `wall_utc`. Also one line in the live file `notes.jsonl` at once (spec 1988). |
| `log` | Log | `level`, `msg`: every `hot_cargo_log.txt` line, with user folder names and Steam IDs replaced by `<user>` and `<steam id>` (spec 1585) |
| `trace` | Trace | `method`, `args`, `result`, `ms`, `ctx` |

Mission fields: `mission`, `type`, `name`, `category`, `faction`, `tier_min`, `tier_max`, `reward_money`, `reward_fame`, `time_left_s`.
AI fields: `ship`, `name`, `side`, `faction`, and the risk model view (spec 1520 to 1524): `odds`, `confidence`, `bravery`, `stance`, `allies`.
Jump ships: `ship`, `name`, `player`, `drive`, `fuel`, `capacity` (hyperium the drives hold), `cost` (hyperium of this jump, null without a destination), `too_big` (cost above capacity), `tech`, `parts`.
Ship fields: `ship`, `name`, `player`, `side` (player, hunter, hostile, neutral, derelict; a piece of a broken ship has the side of the ship), `faction`, `tags`, `hunter`, `value`, `parts`, `ai`, `dist` (from the fleet center).
Terms: `fee`, `bonus`, `flat_heat`, `trail`, `rival`, `informant`.

### Sources and causes

- `src` of a heat, money or fame change comes from the code that called it. Run.OnTick and ContractBook.OnTick add heat every tick, so they are marked without a stack walk (`time`, `trail`). Other changes walk the stack and name the first frame that a rule in `TelemetryPatches.Sources` matches: for example `fame`, `contract_accept`, `informant`, `escort_cover`, `event`, `chain`, `mission_reward`, `contract_bonus`, `trade`, `build`, `build_refund`, `crew_hire`, `surrender`, `fame_cap` (heat above the fame cap, spec 302), `diplomacy` (hunter deals, spec 565 and 593), `scout_report` (spec 912), `hacker` (the hacker's price, spec 931), `tracer` (a Tracer Beacon aboard, spec 1116), `patrol_listen` (a finished patrol listen, spec 1122), `rescue_beacon` (a fired Rescue Beacon, spec 1147), `tech_unlock` (a blueprint unlock: vanilla `MPBuyTechInput` sets the money directly, spec 1586), `test`, `debug`. A heat change with no matching rule breaks the `heat_source` invariant (spec 999). Without a match it is the type of the first frame. `caller` holds up to 3 frames, innermost first, joined with ` < `.
- `untracked` is a money change outside `PlayerState.AddMoney` and the blueprint unlock. The next second finds it.
- Above `CallerLookupsPerSecond` stack walks in one real second, `src` is `rate_limited`.
- `cause` of a tech count change is the newest hint within 5 game seconds: `jump` (the ships are in jump limbo, so the count is 0 until they arrive), `sale`, `buyer`, `buyback`, `vault_destroyed`, `vault_deconstructed`, `decay` (tech outside a vault broke down), `ship_lost`, `crew_death`, `event`, `starting_vault`, `test`, `debug`. `initial` is the first count of a session. `other` means no hint.

### Time use

Spec 1760 to 1763. Each system stay splits its game seconds into what the players' fleet does: `fight`, `build`, `trade`, `travel`, `repair`, `idle`. One activity at a time, the first in that order whose signal is on, so `time_use` adds up to `stay_s`. `time_any` holds the seconds each signal was on at all, also under a higher activity (for example repairs while the fleet travels). The signals:

| Activity | Signal |
|---|---|
| fight | an open `combat.fight` whose last damage window is at most `AccrualSeconds` old |
| build | this machine's build tool is open, new parts or doors are placed and not built yet, or a deconstruction is queued |
| trade | a human ship is in comms, or crew (often the station's) carry a trade to or from a human ship |
| travel | a move order to a point at least `TravelMinDistance` away, a relay jump order, or a local FTL jump |
| repair | repairs or rebuilds of destroyed parts are ordered and not done yet |
| idle | none of these |

The sums count from the system entry or the session start, like `stay_s`. The build tool is local, so in co-op each file counts its own player's editor.

### AI sums

The `ai` object of a snapshot holds sums for the session:

- `<StrategyModule>`: `ticks` (AI ticks that this module drove the ship) and `switches` (ships that switched to it). The game asks the strategy modules from last to first, and the first that returns true drives the ship. That module is the decision of the tick.
- `tactic:<TacticsModule>`: AI ticks with that tactic active (`UpdateAttackAngle` or `UpdateAttackRadiusAndRotation` returned true).
- `focus_evals`, `focus_boosted`: target scores, and the ones that focus fire (spec 200) raised.

`ai.switch` events log the switches one by one, at most one per ship per `AiSwitchMinSeconds` and `AiEventsPerMinute` in total. The sums count every switch.

## Tunables

Section `Telemetry` in `tunables.rules`. Defaults:

| Tunable | Default | Meaning |
|---|---|---|
| `Enabled` | true | Master switch. |
| `Run`, `Heat`, `Tech`, `Economy`, `Missions`, `Story`, `Combat`, `Ai`, `Snapshots`, `Perf`, `Coop`, `Log`, `Trace`, `Diplomacy` | true | Category switches. |
| `SnapshotSeconds` | 30 | Game seconds between snapshots. |
| `PerfSeconds` | 10 | Real seconds between perf events. |
| `AccrualSeconds` | 5 | Game seconds over which time heat, trail heat and damage are summed. |
| `ScanSeconds` | 5 | Game seconds between scans for new offers and ships that spawned into stasis. |
| `TravelMinDistance` | 1500 | A move order to a point at least this far away counts as travel in the time use. |
| `FightGapSeconds` | 90 | Game seconds without damage to the players or their enemies and without a wreck that end a fight. |
| `AiSwitchMinSeconds`, `AiEventsPerMinute` | 3, 600 | Limits of `ai.switch` events. |
| `CallerLookupsPerSecond` | 50 | Limit of stack walks for `src`. |
| `MaxFileMegabytes`, `MaxFolderMegabytes` | 32, 1024 | File rotation and folder size. |
| `NoteKey` | `BackslashPipe` (`\`) | The key of a playtest note. Vanilla binds nothing to it. |
| `RelayToHost` | true | Co-op: a client sends its files to the host (`players/<name>/`). Each machine follows its own switch. |
| `TraceMethods` | see `TelemetryTunables.cs` | Methods for `trace` events, as `Namespace.Type:Method`. Read at the first game start; a change needs a restart. A missing method is skipped with a log line. |

Telemetry is not sim state. Each co-op machine writes its own file with its own switches, and the switches can differ without a desync. The relay to the host is no MP input either.

## Cost

- Game threads only build a line and add it to a list under a short lock. A background thread writes and flushes once per second.
- Telemetry measures its own time on game threads (every patch, handler and event build) and reports it in `perf` (`tele_ms`, `tele_pct_wall`) and `session.end` (`overhead_pct_wall`). At a steady frame rate the share of real time is the share of frame time. Target: well below 1 %.
- Headless test games run at about 400 frames per second without drawing, so their frame times are no measure of play frame times. The share of real time is.
- Limit: a hard kill of the game loses the lines of the last second.

## Playtest notes

During play, press `\` (`Telemetry.NoteKey`), type a short note and press Enter; Escape cancels. The key does nothing while a menu or dialog is open, or while a text box such as the co-op chat has the focus. The note goes into the session file as a `note` event, with what was on screen at the key press, and a chat line says "Note saved." A co-op client's note travels to the host with the client's file (`players/<name>/`). In co-op the game keeps running while a player types; in single player it pauses, like every vanilla dialog.

The game also saves a screenshot through its own capture path (`App.Graphics.GetEndOfFrameScreenShot`, the path of the vanilla screenshot key) into `telemetry/notes/<run>__<time>.png` on that machine. The Mac app has the same capture (the Metal graphics manager); a headless game has no graphics device and saves the note without a screenshot. Screenshots do not travel to the host.

To read notes during play, follow the live file on the host: `tail -F "$HOME/Library/Application Support/Cosmoteer/"*/Mods/hot_cargo/telemetry/notes.jsonl` on a Mac (`%USERPROFILE%\Saved Games\Cosmoteer\<steam id>\Mods\hot_cargo\telemetry\notes.jsonl` on Windows). Each note is one JSON line, written at once (spec 1988). A co-op client sends its note to the host at once, so the host's file holds every player's notes. Fields: the common fields (`e` = `note`, `t`, `gs`, `ss`, `sec`, `sys`, `tier`, `arch`, `heat`, `tech`), then `text`, `player`, `local_player`, `wall_utc`, `pressed_real_s`, `screen`, `screenshot`.

The report lists the notes first, in play order, with the events within 60 game seconds of each.

## Pull and analyze

1. Pull: `scripts/pull-telemetry.sh [SLOT...]`. Without a slot it pulls the main slot and every test slot into `../.telemetry/<slot>/` (outside the repo). Files that are already here with the same size are skipped. The client copies of a co-op host come along in `players/<name>/`, and the report shows each client as its own run (`<run> (client <n>)`).
2. Analyze: `cd tools/telemetry && uv run hc-telemetry`. It reads `../.telemetry` and writes `../.telemetry/report/report.md` and `report.html` (self-contained, charts inline).
   Options: `--dir`, `--out`, `--run <id>`, `--latest N`, `--since <UTC ISO time>`, `--no-autotest`, `--min-game-seconds`.
3. Tests: `scripts/autotest-all.sh --telemetry [timeout] [test ...]` pulls the telemetry after the tests and writes a report of the runs that they made.

The report has, for each run: facts (versions, tunables hash, difficulty, seed), heat by source, stage starts, a table per sector visit (stay, heat in and out, stages, missions, hunters, sales, the share of each time use activity) with a bar chart of the shares, money and fame by source, power and money over time, tech changes by cause, tech trades, missions by type with results and time to finish, events and chains, ships by side, hunter outcomes, damage by side, fights, jumps with the ships left behind, fleet changes (captures, ships that left), the loss timer, diplomacy, the real time split, AI module frequencies, performance and errors. Across runs: a run table, pooled heat sources and findings.

For your own analysis, each line is one JSON object: for example `jq -c 'select(.e=="heat.change")' file.jsonl`.

## Autotest

`scripts/autotest.sh` and `scripts/autotest-mp.sh` send `telename <script name>` first (`<session>.host` and `<session>.client` for co-op), so `session.start` has the test name in `label`.

- Commands: `telename <name>`, `telemove <distance>` (a move order of the own ship to a point this far ahead), `telerepair` (the repair button of the own ship), `teleactivity` (logs the time use signals now), `telerelaychunk <bytes>` (a smaller relay chunk on this machine), `damagehostiles <fraction>` (test only: weakens nearby hostile ships so the player wins the fight), `shieldplayers on|off` (test only: human ship parts lose no health and human crew do not die; in co-op only with `mp`), `shieldhunters on|off` (test only: the parts of corporate hunters lose no health; in co-op only with `mp`), `teleflush` (wait until the writer wrote every queued event), `telesnap [why]`, `telecount <event> <op> <n>` (counts events of the current run in its files), `telestats` (logs the overhead numbers).
- Fields: `tele_active`, `tele_events`, `tele_dropped`, `tele_nested_dropped`, `tele_overhead_ms`, `tele_overhead_pct` (telemetry time in percent of the session's real time), `tele_trace_missing`, `tele_trace_patched`, `tele_time_<activity>` (game seconds of each time use activity in the session), `tele_relay_pending` and `tele_relay_pending_closed` (client: bytes the host has not confirmed, all files or the closed ones), `tele_relay_received`, `tele_relay_copies`, `tele_relay_bad` (host: bytes received, valid and broken client copies).
- Tests: `tests/autotest/telemetry-basic.txt` (every category and a save and load), `tests/autotest/telemetry-run.txt` (a scripted run over three systems that ends with a win; it checks that each event kind shows and makes data for a report).

## Add an event

1. Call `Tele.Emit(Cat.<Category>, "<area>.<name>", w => { ... })` from read-only code. Pass the state as an argument (`Tele.Emit(cat, name, state, static (w, s) => ...)`) on hot paths, so no closure allocates.
2. Read state only. Do not call code that changes sim state, and do not use sim random sources.
3. Add the event to the table above.

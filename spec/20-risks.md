## 20. Risks

- **Game updates** can break Harmony patches. `scripts/pull-game.sh` refreshes the game files and decompiled code for a fix.
- **Desync** in co-op if any logic leaves the simulation tick or uses a non-simulation random source. Requirement 58 covers this, and the multiplayer test checks it.
- **Short career**: the run map has its own spawners (requirement 2550) and uses the vanilla Factions spawner and name generator from `base_galaxy.rules`. A game update that changes those can need a matching change in `mod/journey/galaxy.rules`.
- **The startup hook loads into every Cosmoteer start.** Requirement 63 keeps the game vanilla when the rules mod is off.

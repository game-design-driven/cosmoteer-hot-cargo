## 2. Design pillars

Every design question goes back to these pillars. A change that breaks one needs the player's yes first.

The run:

2200. **Hold or sell.** Every tech unit you hold makes the chase harder. Every unit you sell makes the run easier now and poorer at the end.
2201. **Move on or burn.** Missions earn fame, and fame brings the hunters faster. A sector supports only a few missions before you must leave.
2202. **Protect the cargo.** The tech is a physical thing in your ship. If you lose it, the run ends when the last unit is gone.
2203. **Losing is fine.** A run can end in a loss. When a change takes away a way out of a hard spot, nothing replaces it on its own: no automatic exit, and no "wait and it ends". If the players can neither pay nor win, they lose. Reason: the player rejected lockdown timers that an agent added after the blind jump went away.
2204. **Short co-op runs.** A run fits in a co-op evening. Content and pacing serve short runs, not long campaigns.

How a mechanic works:

2205. **In the world.** A mechanic uses the game's own systems where it can: parts, items, ships, comms, the map. A new screen or a menu-only rule comes last.
2206. **Gamble in the world.** Risky actions and gambles happen in the world: an item action in the hold, a fight, a ship that reacts. A dialog offers only clear deals, where the player sees what they give and what they get (spec 46).
2207. **The AI follows the players' rules.** Enemy and friendly ships play by the same rules as the players: the same parts, damage and limits. The spec of a mechanic names each exception, for example the hunter waves that spawn (spec 23) and the difficulty scaling (spec 28).
2208. **Fix the cause, never punish the habit.** When players do something too often, find out why and change that. No fees, cooldowns or limits on building. Example: players built after every fight because loose loot could not be stored; the fix is less loot volume (`docs/loot-volume-study.md`), not a cost on building.
2209. **No dominant choice.** Each choice has a cost or a falling return. Repeated actions give less each time. No loop makes money without end.
2210. **Rare things stay rare.** A rare find, item or event stays rare: no mechanic makes it common, and no loop farms it.

What the player sees:

2211. **Every effect is visible.** Each mechanic shows its effect where the player looks: a number, a part, a short line. No silent rules. A warning shows once, at the moment it matters, and does not cover play.
2212. **Simple and short.** Few rules, short text, readable at a glance during play. A mechanic that needs a long explanation is too complex.
2213. **Tune live.** All numbers are tunables in one file. The game picks up changes without a restart, also in co-op from the host.

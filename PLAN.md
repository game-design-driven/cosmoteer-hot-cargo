# Callbacks: consequences that come back later

Design:
- A memory (`Memory` in `EventsState.Memories`): kind (typed enum), subject ship name, faction, system name, sector, recalled. Saved in run state, count in the co-op hash.
- Mechanics record memories: surrender accepted (Diplomacy `Deals.Spared` event, raised by the diplomacy surrender and the vanilla surrender), hunter pay-off and bluff (existing `Deals.Decided`), mayday won.
- Callback events: `Trigger = Callback` and `Recalls = <kind>` in events.rules. Eligible only with an unrecalled memory of the kind, at least `CallbackMinSectors` old. Opening binds the oldest such memory; the carrier ship takes the memory's ship name; tokens `{mem_ship}`, `{mem_system}`.
- Trigger: the Arrival due point of a first visit tries a callback first (chance `CallbackChance`), then the normal Arrival roll. One per sector is structural (one Arrival point per first visit).
- Limits: the Pursuit pacer kind `Callback` (MaxPerRun, MinGapSeconds, the global gap).
- Pure logic in `Events/MemoryModel.cs` with unit tests.

Steps:
1. Memory model, state, hash, record API, unit tests.
2. Callback trigger, rules fields, tokens, carrier rename, pacer kind.
3. Surrender callback (`cb_spared_thanks`) and game test `events-callbacks`.
4. Interim catalog message.
5. The rest of the catalog (pirate grudge or debt, route sold, bluff grudge, mayday gift).
6. Spec 72, EVENTS.md catalog, TUNABLES.md, co-op step.

# Tech vault rework (2x2, storage, decay)

Spec numbers: 1540-1559 (new section `spec/58-tech-vault-2x2-and-decay.md`), patch 14, 15, 17, 440-449, 1010-1018, 1470-1476 in place.

## Decisions
- Vault part: same ID `yarden_zamir.tech_vault`, 2x2, 4 tiles, 1 tech per tile (`MaxStackSize` = 1 on the resource), 12,000 health and 32 steel (same per tile as before). Breaking: old 1x1 vaults in designs and saves.
- Vault count: `TechModel.VaultsNeeded(units, perVault)` = ceil. `perVault` comes from the part rules. Used by the design tool, Fit Tech Vaults and the start checks.
- Storage: `FlexGridBlocksTechPatch` goes. Vault first comes from vanilla sort priorities (a storage with a consumer: 262144; flex tile: at most 196608). `Vaults.Add` fills vaults, then storage. `Vaults.Remove` takes storage first. Fleet count = vaults + storage + carried.
- Decay: pure model `TechDecayModel` (keys, wear, matching through crew jobs). Exposed: tech in a flex storage tile of a player or derelict ship, or carried by crew. Crates: no decay. Tunables `DecaySeconds` = 360, `DecayInVault` = Pause | Reset (default Pause). State in `RunState.Tech`, integrity hash.
- Visible: tile tooltip (cause + time left), heat panel line, blip tag, toast at first exposure, destroy toast + chat line + effect + run log.
- Vault UI: vanilla consumption toggle on the vault consumer ("fill" on/off in the Resources view); parts card shows the vault contents.
- Start: no refusal. Ship pick with too little vault room: Fit Tech Vaults / Start without / Cancel, big warning. Run start without vaults: big toast. Starting tech overflows into storage.
- Presets: rebuild `mod/tech/starters` with the design tool on the Mac runner (no main slot).

## Steps
1. Part rules, resource stack size, strings.
2. Vault count model + unit tests; design tool, Fit, start checks.
3. Storage allowed, Vaults API, fleet count, invariant.
4. Decay model + unit tests, sim glue, state, tunables, UI lines.
5. Vault UI: toggle, parts card.
6. Start warnings.
7. Presets rebuilt on the Mac.
8. Tests: sim test tech-decay, update tech tests, co-op step.
9. Spec, docs, screenshots, PR.

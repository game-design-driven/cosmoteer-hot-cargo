# Plan: number breakdowns on hover (spec 75)

Design:
- `Core/BreakdownModel.cs`: `Breakdown`, an ordered list of terms (base, plus, times, clamp, round). The total is the fold of the terms, and the only way to change it is to add a term. The model functions return a `Breakdown`, and the game uses its `Total`. So the tooltip and the number come from one computation.
- Terms carry a string key (and an optional text argument), not text. The UI resolves keys. A term can name a hint ("Leave ally space") with an other value; the UI shows the change of the total with that value.
- `Ux/BreakdownText.cs` renders a breakdown as tooltip XML. Factors show as percent by default; tunable `Ux.BreakdownFactors` (enum `FactorStyle { Percent, Multiplier }`) switches to ×1.20.
- Heat rate: `Run.Hooks.TimeHeatFactor` (Func chain) becomes `Run.Hooks.TimeHeatTerms` (each area adds its factor term). `Run.TimeHeat(units)` returns the breakdown; `TimeHeatPerMinute` is its total. The panel adds the contract trail term from the same contract code.
- Tech value: `TechModel.Value` returns a breakdown (base share, game time, notoriety, round, difficulty).
- Diplomacy: `DiplomacyModel.SurrenderChance`, `BluffChance`, pay-off chance return breakdowns; `Quotes` returns them; buttons get tooltips.
- Tooltips refresh every `Ux.HudRefreshSeconds` (spec 872).
- Tests: unit tests per number (the fold of the terms equals the total, the total equals the old formula values); autotest `ux-breakdowns`: hover the real widget, read the real tooltip label.

Steps:
1. Breakdown model, render, tunable, unit tests.
2. Heat rate (hook change, heat panel status line tooltip).
3. Tech value (heat panel tech line tooltip).
4. Surrender, bluff, pay-off chance tooltips on the demand page.
5. Autotest, screenshots, spec 75, PR text.
6. Second PR: station prices, reputation, other composite numbers.

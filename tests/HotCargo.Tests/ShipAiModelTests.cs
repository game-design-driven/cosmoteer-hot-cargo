using HotCargo.ShipAi;
using Xunit;

namespace HotCargo.Tests;

/// <summary>A ship's facts and values for tests: unset facts are false, unset values are missing (null).</summary>
public sealed class TestFacts : IShipFacts
{
    public readonly HashSet<(Cond, int)> Facts = [];
    public readonly Dictionary<Operand, double?> Values = [];

    public TestFacts With(Cond cond, int param = 0)
    {
        Facts.Add((cond, param));
        return this;
    }

    public TestFacts With(Value value, double? v) => With(new Operand(value), v);

    public TestFacts With(Operand operand, double? v)
    {
        Values[operand] = v;
        return this;
    }

    public bool Fact(Cond cond, int param) => cond == Cond.Always || Facts.Contains((cond, param));

    public double? Value(Operand operand) => Values.GetValueOrDefault(operand);
}

public class ShipAiModelTests
{
    static TacticRow Row(Term term, Act act, int param = 0) => new(term, act, param);

    static readonly Term Always = Term.Fact(Cond.Always);

    /// <summary>Whether the conditions in text form hold, for example "Always&amp;(Health&lt;40|CarryingTech)".</summary>
    static bool Holds(IShipFacts f, string conditions) =>
        ShipAiModel.Evaluate(ShipAiModel.Decode($"HC2:{conditions}->Kite"), f, _ => true).Matched[0];

    /// <summary>The condition side of a row in text form.</summary>
    static string Text(TacticRow row)
    {
        string text = ShipAiModel.Encode([row]);
        return text[ShipAiModel.Prefix.Length..text.LastIndexOf(ShipAiModel.Arrow, StringComparison.Ordinal)];
    }

    static TacticRow Parse(string conditions) => ShipAiModel.Decode($"HC2:{conditions}->Kite")[0];

    [Fact]
    public void Every_fact_value_and_action_is_in_the_registry_exactly_once()
    {
        Assert.Equal(Enum.GetValues<Cond>().OrderBy(c => c), ShipAiRegistry.Facts.Select(f => f.Cond).OrderBy(c => c));
        Assert.Equal(Enum.GetValues<Value>().OrderBy(v => v), ShipAiRegistry.Values.Select(v => v.Value).OrderBy(v => v));
        Assert.Equal(Enum.GetValues<Act>().OrderBy(a => a), ShipAiRegistry.Acts.Select(a => a.Act).OrderBy(a => a));
        // Spec 2323: every unit has choices inside its range and a default among them.
        foreach (var unit in Enum.GetValues<Unit>())
        {
            var (min, max) = ShipAiRegistry.RangeOf(unit);
            Assert.All(ShipAiRegistry.Choices(unit, 5), n => Assert.InRange(n, min, max));
            Assert.Contains(ShipAiRegistry.Default(unit), ShipAiRegistry.Choices(unit, 5));
        }
    }

    [Fact]
    public void First_matching_row_of_each_kind_wins_and_later_rows_of_that_kind_are_ignored()
    {
        TacticRow[] rows =
        [
            Row(Term.Less(Value.Health, 30), Act.RetreatRepair),
            Row(Always, Act.CloseIn),
            Row(Always, Act.TargetWeakest),
            Row(Always, Act.TargetNearest),
            Row(Always, Act.Kite),
        ];
        var hurt = ShipAiModel.Evaluate(rows, new TestFacts().With(Value.Health, 20), _ => true);
        Assert.Equal(0, hurt.MoveRow);
        Assert.Equal(2, hurt.TargetRow);
        var fine = ShipAiModel.Evaluate(rows, new TestFacts().With(Value.Health, 90), _ => true);
        Assert.Equal(1, fine.MoveRow);
        Assert.Equal([false, true, true, true, true], fine.Matched);
    }

    [Fact]
    public void A_row_whose_action_cannot_run_is_skipped_and_can_run_is_asked_only_when_the_terms_hold()
    {
        TacticRow[] rows = [Row(Always, Act.TargetAllyAttacker), Row(Term.Fact(Cond.CarryingTech), Act.TargetWeakest), Row(Always, Act.TargetNearest)];
        var asked = new List<Act>();
        var d = ShipAiModel.Evaluate(rows, new TestFacts(), r => { asked.Add(r.Act); return r.Act != Act.TargetAllyAttacker; });
        Assert.Equal(2, d.TargetRow);
        Assert.Equal([Act.TargetAllyAttacker, Act.TargetNearest], asked);
    }

    [Theory]
    [InlineData("Health<30", 20.0, true)]
    [InlineData("Health<30", 30.0, false)]
    [InlineData("Health>30", 31.0, true)]
    [InlineData("Health=30", 30.4, true)]
    [InlineData("Health=30", 31.0, false)]
    [InlineData("!Health>30", 30.0, true)] // NOT > is "at most"
    public void A_comparison_compares_the_value_with_the_number(string conditions, double health, bool expected) =>
        Assert.Equal(expected, Holds(new TestFacts().With(Value.Health, health), conditions));

    [Fact]
    public void A_missing_value_makes_every_comparison_false_and_nothing_to_measure_to_is_infinitely_far()
    {
        var f = new TestFacts().With(Value.NearestEnemy, double.PositiveInfinity);
        Assert.False(Holds(f, "Shields<30"));
        Assert.False(Holds(f, "Shields>30"));
        Assert.True(Holds(f, "!Shields<30"));
        Assert.False(Holds(f, "NearestEnemy<1500"));
        Assert.True(Holds(f, "NearestEnemy>1500"));
    }

    [Fact]
    public void A_comparison_with_a_value_reads_both_sides()
    {
        var steel = new Operand(Value.Resource, Resource: "steel");
        var cost = new Operand(Value.Cost, 1, "steel");
        var f = new TestFacts().With(steel, 40).With(cost, 48);
        Assert.True(Holds(f, "Resource[steel]<Cost[1,steel]"));
        f.With(steel, 50);
        Assert.False(Holds(f, "Resource[steel]<Cost[1,steel]"));
        // An empty slot has no cost value: false either way.
        f.With(cost, null);
        Assert.False(Holds(f, "Resource[steel]>Cost[1,steel]"));
    }

    [Fact]
    public void Facts_read_their_parameter()
    {
        var f = new TestFacts().With(Cond.PartLost, (int)PartKind.Thruster).With(Cond.Flag, 2);
        Assert.True(Holds(f, "PartLost=Thruster"));
        Assert.False(Holds(f, "PartLost=Weapon"));
        Assert.True(Holds(f, "Flag=2"));
        Assert.False(Holds(f, "Flag=1"));
    }

    [Fact]
    public void A_percent_comparison_that_holds_keeps_holding_inside_the_margin()
    {
        TacticRow[] rows = [Row(Term.Less(Value.Health, 30), Act.RetreatRepair)];
        var h = new Hysteresis(0.05f, 0);
        var on = ShipAiModel.Evaluate(rows, new TestFacts().With(Value.Health, 20), _ => true, null, 0, h);
        Assert.Equal(0, on.MoveRow);
        // 34 < 30 + 5 points: still on. 36: off. From off, 34 does not turn it on.
        var inside = ShipAiModel.Evaluate(rows, new TestFacts().With(Value.Health, 34), _ => true, on.Memory, 1, h);
        Assert.Equal(0, inside.MoveRow);
        var outside = ShipAiModel.Evaluate(rows, new TestFacts().With(Value.Health, 36), _ => true, inside.Memory, 2, h);
        Assert.Equal(-1, outside.MoveRow);
        Assert.Equal(-1, ShipAiModel.Evaluate(rows, new TestFacts().With(Value.Health, 34), _ => true, outside.Memory, 3, h).MoveRow);
    }

    [Fact]
    public void A_distance_margin_is_a_share_of_the_other_side_and_counts_and_equal_get_none()
    {
        var h = new Hysteresis(0.1f, 0);
        TacticRow[] near = [Row(Term.Less(Value.NearestEnemy, 1000), Act.Kite)];
        var on = ShipAiModel.Evaluate(near, new TestFacts().With(Value.NearestEnemy, 900), _ => true, null, 0, h);
        Assert.Equal(0, ShipAiModel.Evaluate(near, new TestFacts().With(Value.NearestEnemy, 1090), _ => true, on.Memory, 1, h).MoveRow);
        Assert.Equal(-1, ShipAiModel.Evaluate(near, new TestFacts().With(Value.NearestEnemy, 1110), _ => true, on.Memory, 1, h).MoveRow);
        TacticRow[] count = [Row(Term.Greater(Value.Enemies, 2), Act.Kite)];
        var counted = ShipAiModel.Evaluate(count, new TestFacts().With(Value.Enemies, 3), _ => true, null, 0, h);
        Assert.Equal(-1, ShipAiModel.Evaluate(count, new TestFacts().With(Value.Enemies, 2), _ => true, counted.Memory, 1, h).MoveRow);
        Assert.False(ShipAiModel.IsThreshold(new CompareTerm(new Operand(Value.Health), Op.Equal, null, 30)));
        Assert.False(ShipAiModel.IsThreshold(new FactTerm(Cond.CarryingTech)));
    }

    [Fact]
    public void A_threshold_comparison_keeps_a_new_result_for_the_minimum_hold_time()
    {
        TacticRow[] rows = [Row(Term.Less(Value.Health, 30), Act.RetreatRepair)];
        var h = new Hysteresis(0f, 2);
        var on = ShipAiModel.Evaluate(rows, new TestFacts().With(Value.Health, 20), _ => true, null, 10, h);
        Assert.Equal(0, ShipAiModel.Evaluate(rows, new TestFacts().With(Value.Health, 80), _ => true, on.Memory, 11, h).MoveRow);
        Assert.Equal(-1, ShipAiModel.Evaluate(rows, new TestFacts().With(Value.Health, 80), _ => true, on.Memory, 12, h).MoveRow);
    }

    [Fact]
    public void Memory_of_other_rows_is_ignored()
    {
        TacticRow[] rows = [Row(Term.Less(Value.Health, 30), Act.RetreatRepair)];
        var d = ShipAiModel.Evaluate(rows, new TestFacts().With(Value.Health, 50), _ => true, [new TermMemory(true, 0), new TermMemory(true, 0)], 5, new Hysteresis(0.5f, 99));
        Assert.Equal(-1, d.MoveRow);
    }

    [Fact]
    public void Text_round_trips_every_preset_and_every_entry_of_the_registry()
    {
        foreach (var p in Enum.GetValues<Preset>())
        {
            var rows = ShipAiModel.PresetRows(p);
            Assert.Equal(rows, ShipAiModel.Decode(ShipAiModel.Encode(rows)));
        }
        foreach (var f in ShipAiRegistry.Facts)
        {
            var row = Row(Term.Fact(f.Cond, ShipAiRegistry.Default(f.Param)), Act.Kite);
            Assert.Equal(row, ShipAiModel.Decode(ShipAiModel.Encode([row]))[0]);
        }
        foreach (var v in ShipAiRegistry.Values)
        {
            var body = ShipAiModel.Picked(new FactTerm(Cond.Always), v.Value, "steel");
            var row = new TacticRow(new Term(body), Act.Kite);
            Assert.Equal(row, ShipAiModel.Decode(ShipAiModel.Encode([row]))[0]);
        }
        foreach (var a in ShipAiRegistry.Acts)
        {
            var row = Row(Always, a.Act, ShipAiRegistry.Default(a.Param));
            Assert.Equal(row, ShipAiModel.Decode(ShipAiModel.Encode([row]))[0]);
        }
    }

    [Fact]
    public void Text_names_enum_parameters_and_writes_arguments_in_square_brackets()
    {
        const string text = "HC2:!CarryingTech&Health<40|HeatStage>2->CollectTech;StrikeIn<30|LossTimerRunning->Face=BroadsideLeft;ShipsOf[1]<3&Resource[steel]>Cost[2,steel]->BuildShip=1";
        var rows = ShipAiModel.Decode(text);
        Assert.Equal(text, ShipAiModel.Encode(rows));
        Assert.Equal(Side.BroadsideLeft, (Side)rows[1].ActParam);
        Assert.Equal(new CompareTerm(new Operand(Value.Resource, Resource: "steel"), Op.Greater, new Operand(Value.Cost, 2, "steel"), 0), rows[2].Terms[1].Body);
        Assert.True(rows[0].Terms[2].Or);
    }

    [Theory]
    [InlineData("HC1:HealthBelow=30>RetreatRepair")] // the old form
    [InlineData("HC2:Health<30>RetreatRepair")]      // the old arrow
    [InlineData("HC2:Health<101->Kite")]
    [InlineData("HC2:Health<-1->Kite")]
    [InlineData("HC2:Health<30.5->Kite")]
    [InlineData("HC2:Health->Kite")]
    [InlineData("HC2:Health~30->Kite")]
    [InlineData("HC2:Health<NearestEnemy->Kite")]     // different units
    [InlineData("HC2:Resource<5->Kite")]              // missing argument
    [InlineData("HC2:Resource[steel,1]<5->Kite")]
    [InlineData("HC2:Cost[4,steel]<5->Kite")]          // slot out of range
    [InlineData("HC2:CarryingTech<1->Kite")]          // a fact compared
    [InlineData("HC2:PartLost->Kite")]
    [InlineData("HC2:PartLost=1->Kite")]              // enum parameters are names
    [InlineData("HC2:Always->Face=Top")]
    [InlineData("HC2:Always->Kite=1")]
    [InlineData("HC2:Always->BuildShip=4")]
    [InlineData("HC2:Always->SetFlag=5")]
    [InlineData("HC2:&Always->Kite")]
    [InlineData("HC2:Always||Always->Kite")]
    [InlineData("HC2:!!Always->Kite")]
    [InlineData("HC2:(Always->Kite")]
    [InlineData("HC2:!(Always)->Kite")]
    public void Bad_text_throws(string text) => Assert.Throws<FormatException>(() => ShipAiModel.Decode(text));

    [Fact]
    public void Too_many_rows_throw()
    {
        string text = "HC2:" + string.Join(";", Enumerable.Repeat("Always->Kite", ShipAiModel.MaxRows + 1));
        Assert.Throws<FormatException>(() => ShipAiModel.Decode(text));
    }

    [Fact]
    public void A_row_takes_any_number_of_terms()
    {
        var row = Row(Always, Act.Kite);
        for (int i = 0; i < 99; i++) row = ShipAiModel.AddTerm(row);
        Assert.Equal(100, row.Terms.Length);
        Assert.Equal(row, ShipAiModel.Decode(ShipAiModel.Encode([row]))[0]);
        Assert.Single(ShipAiModel.RemoveTerm(Row(Always, Act.Kite), 0).Terms);
    }

    [Theory]
    // Health 30, tech carried, no enemy targets us. Without brackets AND binds tighter: (false & true) | true.
    [InlineData("TargetedByEnemy&CarryingTech|Health<40", true)]
    [InlineData("TargetedByEnemy&(CarryingTech|Health<40)", false)]
    [InlineData("(TargetedByEnemy|CarryingTech)&Health<40", true)]
    [InlineData("((TargetedByEnemy|CarryingTech)&!Health<40)|TargetedByEnemy", false)]
    [InlineData("!TargetedByEnemy&((Health<20|CarryingTech)&(VaultDamaged|Health<40))", true)]
    [InlineData("(CarryingTech)", true)]
    public void Brackets_group_terms(string conditions, bool expected) =>
        Assert.Equal(expected, Holds(new TestFacts().With(Value.Health, 30).With(Cond.CarryingTech), conditions));

    [Fact]
    public void Bracketed_text_round_trips()
    {
        const string text = "HC2:!TargetedByEnemy&((Health<20|!CarryingTech)&(VaultDamaged|NearestEnemy<800))|Always->CollectTech";
        Assert.Equal(text, ShipAiModel.Encode(ShipAiModel.Decode(text)));
    }

    [Theory]
    [InlineData("Health<20&CarryingTech", 1, "Health<20&(CarryingTech)")]
    [InlineData("Health<20&(CarryingTech)", 1, "Health<20&((CarryingTech))")]
    public void Bracket_wraps_one_term(string before, int j, string after) => Assert.Equal(after, Text(ShipAiModel.Bracket(Parse(before), j)));

    [Theory]
    [InlineData("Health<20&(CarryingTech)", 1, "Health<20&(CarryingTech&Always)")]
    [InlineData("(Health<20|(CarryingTech))&VaultDamaged", 1, "(Health<20|(CarryingTech&Always))&VaultDamaged")]
    [InlineData("Health<20", 0, "Health<20&Always")]
    public void Insert_term_stays_in_the_brackets(string before, int j, string after) => Assert.Equal(after, Text(ShipAiModel.InsertTerm(Parse(before), j)));

    [Theory]
    [InlineData("(Health<20|CarryingTech)&VaultDamaged", 0, "Health<20|CarryingTech&VaultDamaged")]
    [InlineData("((Health<20|CarryingTech)&VaultDamaged)|Always", 0, "(Health<20|CarryingTech&VaultDamaged)|Always")]
    [InlineData("(Health<20|(CarryingTech&VaultDamaged))", 1, "(Health<20|CarryingTech&VaultDamaged)")]
    public void Unbracket_removes_the_innermost_bracket_and_its_match(string before, int j, string after) =>
        Assert.Equal(after, Text(ShipAiModel.Unbracket(Parse(before), j)));

    [Theory]
    [InlineData("Health<20&(CarryingTech)|Always", 1, "Health<20|Always")]
    [InlineData("(Health<20|CarryingTech)&Always", 0, "(CarryingTech)&Always")]
    [InlineData("(Health<20|CarryingTech)&Always", 1, "(Health<20)&Always")]
    [InlineData("((Health<20)|CarryingTech)&Always", 0, "(CarryingTech)&Always")]
    [InlineData("Health<20&!CarryingTech|Enemies>2", 1, "Health<20|Enemies>2")]
    public void Remove_term_keeps_the_brackets_balanced_and_the_or_with_its_own_term(string before, int j, string after) =>
        Assert.Equal(after, Text(ShipAiModel.RemoveTerm(Parse(before), j)));

    [Fact]
    public void Picking_an_entry_keeps_what_still_fits()
    {
        var health = new CompareTerm(new Operand(Value.Health), Op.Greater, null, 75);
        // Same unit: the comparison and the number stay.
        Assert.Equal(health with { Left = new Operand(Value.Shields) }, ShipAiModel.Picked(health, Value.Shields, "steel"));
        // Another unit: the unit's default, "<".
        Assert.Equal(new CompareTerm(new Operand(Value.NearestEnemy), Op.Less, null, ShipAiRegistry.Default(Unit.Distance)), ShipAiModel.Picked(health, Value.NearestEnemy, "steel"));
        // A value with arguments starts at slot 1 and the given resource.
        Assert.Equal(new Operand(Value.Cost, 1, "coil"), ((CompareTerm)ShipAiModel.Picked(health, Value.Cost, "coil")).Left);
        // A fact keeps its parameter only when the kind matches.
        Assert.Equal(new FactTerm(Cond.CanAffordRefit, 2), ShipAiModel.Picked(new FactTerm(Cond.CanAffordDesign, 2), Cond.CanAffordRefit));
        Assert.Equal(new FactTerm(Cond.PartLost, 0), ShipAiModel.Picked(new FactTerm(Cond.Flag, 3), Cond.PartLost));
        Assert.Equal(Op.Greater, ShipAiModel.NextOp(Op.Less));
        Assert.Equal(Op.Less, ShipAiModel.NextOp(Op.Equal));
    }

    [Fact]
    public void Matched_flag_rows_set_and_clear_flags_in_row_order()
    {
        TacticRow[] rows = [Row(Always, Act.SetFlag, 1), Row(Always, Act.SetFlag, 3), Row(Always, Act.ClearFlag, 1), Row(Always, Act.Kite)];
        Assert.Equal(0b100, ShipAiModel.ApplyFlags(rows, [true, true, true, true], 0));
        Assert.Equal(0b101, ShipAiModel.ApplyFlags(rows, [false, true, false, true], 0b001));
        Assert.Equal(ActKind.Flag, ShipAiModel.KindOf(Act.SetFlag));
        // Flag rows do not take a kind slot: a move row below still runs.
        Assert.Equal(3, ShipAiModel.Evaluate(rows, new TestFacts(), _ => true).MoveRow);
    }

    [Fact]
    public void Weapon_act_takes_the_first_matched_row_of_the_weapons_groups_or_all_and_disruptor_rows_only_for_disruptors()
    {
        TacticRow[] rows = [Row(Always, Act.HoldFire, 2), Row(Always, Act.FireDisruptors, 0), Row(Always, Act.TargetEngines, 0)];
        bool[] matched = [true, true, true];
        Assert.Equal(Act.HoldFire, ShipAiModel.WeaponAct(rows, matched, new HashSet<int> { 2 }));
        Assert.Equal(Act.TargetEngines, ShipAiModel.WeaponAct(rows, matched, new HashSet<int> { 3 }));
        Assert.Equal(Act.FireDisruptors, ShipAiModel.WeaponAct(rows, matched, new HashSet<int> { 3 }, disruptor: true));
        Assert.Equal(Act.FireAtWill, ShipAiModel.WeaponAct(rows, [false, false, false], new HashSet<int>()));
    }

    [Fact]
    public void First_matching_facing_trade_and_build_rows_win_and_a_build_row_does_not_take_the_trade_slot()
    {
        TacticRow[] rows = [Row(Always, Act.Face, (int)Side.Stern), Row(Always, Act.Face, (int)Side.Nose), Row(Always, Act.BuildShip, 1), Row(Always, Act.Sell), Row(Always, Act.RefitShip, 2)];
        var d = ShipAiModel.Evaluate(rows, new TestFacts(), _ => true);
        Assert.Equal(0, d.FacingRow);
        Assert.Equal(3, d.TradeRow);
        Assert.Equal(2, d.BuildRow);
    }

    [Fact]
    public void A_target_row_picks_within_its_nearest_enemy_range()
    {
        Assert.Equal(1000f * 1.1f, ShipAiModel.EngageRange(Parse("CarryingTech&NearestEnemy<1000&NearestEnemy<2000"), 0.1f));
        Assert.Null(ShipAiModel.EngageRange(Parse("NearestEnemy<1000|CarryingTech"), 0.1f));
        Assert.Null(ShipAiModel.EngageRange(Parse("!NearestEnemy<1000"), 0.1f));
        Assert.Null(ShipAiModel.EngageRange(Parse("NearestEnemy>1000"), 0.1f));
    }

    [Fact]
    public void No_built_in_preset_hunts_an_enemy_anywhere_on_radar()
    {
        var anywhere = new[] { Act.TargetNearest, Act.TargetWeakest, Act.TargetStrongest };
        foreach (var preset in Enum.GetValues<Preset>())
            foreach (var row in ShipAiModel.PresetRows(preset).Where(r => anywhere.Contains(r.Act)))
                Assert.True(ShipAiModel.EngageRange(row, 0f) is not null, $"{preset}: {row.Act} has no engage range");
    }

    [Fact]
    public void Design_slots_and_resources_of_a_row_are_checked()
    {
        var rows = ShipAiModel.Decode("HC2:CanAffordRefit=2&ShipsOf[3]<1&Resource[gold]>Cost[1,steel]->RefitShip=2");
        Assert.Equal([2, 3, 1, 2], ShipAiModel.DesignSlots(rows[0]).ToArray());
        Assert.Null(ShipAiModel.DesignError(rows, _ => true));
        Assert.NotNull(ShipAiModel.DesignError(rows, slot => slot != 3));
        Assert.Null(ShipAiModel.ResourceError(rows, _ => true));
        Assert.NotNull(ShipAiModel.ResourceError(rows, r => r != "gold"));
    }

    [Fact]
    public void Heat_stage_numbers_above_the_number_of_stages_are_an_error()
    {
        Assert.Null(ShipAiModel.StageError(ShipAiModel.Decode("HC2:HeatStage>2->Kite"), 3));
        Assert.NotNull(ShipAiModel.StageError(ShipAiModel.Decode("HC2:HeatStage>4->Kite"), 3));
        Assert.Equal([0, 1, 2, 3], ShipAiRegistry.Choices(Unit.Stage, 3));
    }

    [Fact]
    public void Preset_file_round_trips_and_skips_bad_lines()
    {
        string file = ShipAiModel.FormatPresets([("Mine\tA", "HC2:Always->Kite"), ("B", ShipAiModel.Encode(ShipAiModel.PresetRows(Preset.Looter)))]);
        var (presets, skipped) = ShipAiModel.ParsePresets(file + "\nold\tHC1:Always>Kite\n\tHC2:Always->Kite\nno tab");
        Assert.Equal([("Mine A", "HC2:Always->Kite"), ("B", ShipAiModel.Encode(ShipAiModel.PresetRows(Preset.Looter)))], presets);
        Assert.Equal(3, skipped);
    }

    [Fact]
    public void Card_state_is_off_mixed_or_on_by_how_many_ships_have_auto()
    {
        Assert.Equal(CardState.Off, ShipAiModel.CardStateOf(0, 3));
        Assert.Equal(CardState.Mixed, ShipAiModel.CardStateOf(1, 3));
        Assert.Equal(CardState.On, ShipAiModel.CardStateOf(3, 3));
        Assert.Throws<ArgumentOutOfRangeException>(() => ShipAiModel.CardStateOf(4, 3));
    }

    [Fact]
    public void Preset_of_a_ship_is_the_built_in_one_first_then_a_personal_one_with_the_same_rows()
    {
        string looter = ShipAiModel.Encode(ShipAiModel.PresetRows(Preset.Looter));
        Assert.Equal((Preset.Looter, (string?)null), ShipAiModel.PresetOf(looter, [("Mine", looter)]));
        Assert.Equal(((Preset?)null, "Mine"), ShipAiModel.PresetOf("HC2:Always->Kite", [("Mine", "HC2:Always->Kite")]));
        Assert.Equal(((Preset?)null, (string?)null), ShipAiModel.PresetOf("", []));
    }
}

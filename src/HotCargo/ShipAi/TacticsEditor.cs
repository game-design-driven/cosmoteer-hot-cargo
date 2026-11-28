using System.Globalization;
using Cosmoteer;
using Cosmoteer.Game.Gui;
using Cosmoteer.Gui;
using Cosmoteer.Localization;
using Cosmoteer.Resources;
using Cosmoteer.Ships;
using Halfling;
using Halfling.Geometry;
using Halfling.Graphics.Text;
using Halfling.Gui;
using Halfling.Input;
using HotCargo.Core;

namespace HotCargo.ShipAi;

/// <summary>
/// Spec 410, 419 to 422. The tactics editor of one ship: Auto switch, presets (built-in and personal), copy and paste, apply to the
/// selected ships, and one block per row: one line per condition term, then the action and its parameter. Ctrl+K opens it for the selected ship (or the first own ship). It only reads sim state and sends TacticsInput:
/// each edit sends the whole row list, and the window shows the sim's rows again when another player changes them.
/// </summary>
internal sealed class TacticsEditor : WindowBox
{
    static TacticsEditor? _open;
    /// <summary>In-game copy buffer, for platforms without a text clipboard (headless test games).</summary>
    static string? _copied;

    readonly Run _run;
    readonly Ship _ship;
    readonly ToggleButton _autoToggle;
    readonly DropList _presets;
    readonly TextEditField _presetName;
    readonly Button _deletePreset;
    readonly Button _toSelected;
    /// <summary>The entries of the preset list: the built-ins, then the personal presets (Personal = their name).</summary>
    readonly List<(string Text, string? Personal)> _presetEntries = [];
    readonly Label _status;
    readonly LayoutBox _list;
    readonly Button _add;
    readonly List<Label> _markers = [];
    /// <summary>The condition and action lists and the parameter lists of the shown rules, for the fit check.</summary>
    readonly List<DropList> _choiceLists = [];
    readonly List<DropList> _paramLists = [];
    readonly SimpleBox _cargoBar;
    readonly SimpleBox _designBar;
    CargoPicker? _picker;

    List<TacticRow> _rows = [];
    bool _auto;
    /// <summary>Spec 1062, 1064: the cargo settings and the return switch of the ship.</summary>
    Autonomy.CargoSettings _cargo = Autonomy.CargoSettings.Default;
    bool _recall;
    /// <summary>The whole state that the window shows and last sent: rows, Auto, cargo text and return switch (StateKey).</summary>
    string _shown = "";
    string? _sent;
    double _sentAt;
    bool _dirty = true;
    bool _updating;
    double _nextRefresh;
    string? _note;
    double _noteUntil;

    /// <summary>Real seconds that a sent edit waits for the sim before the window takes the sim's rows again. Longer than MP input delay.</summary>
    const double SendWaitSeconds = 3.0;

    static string Text(string key) => Strings.GetText("HotCargoShipAi/" + key);

    static string Format(string key, params object?[] args) => Strings.FormatText("HotCargoShipAi/" + key, args);

    static string AText(string key) => Strings.GetText("HotCargoAutonomy/" + key);

    static string AFormat(string key, params object?[] args) => Strings.FormatText("HotCargoAutonomy/" + key, args);

    static string StateKey(string text, bool auto, string cargo, bool recall) => $"{text}\n{auto}\n{cargo}\n{recall}";

    static string StateKey(ShipTactics? t) => StateKey(t?.Text ?? "", t?.Auto ?? false, t?.CargoText ?? "", t?.Recall ?? false);

    internal static bool IsOpen => _open?.Parent is not null;

    /// <summary>Frame hook: the hotkey, and the refresh of the open window.</summary>
    internal static void OnFrame(Run run)
    {
        if (App.Keyboard.HotkeyPressed(ViKey.Control, ViKey.K))
        {
            if (IsOpen) Close();
            else if (DefaultShip(run) is { } ship) Open(run, ship);
            else Ux.UxKit.CenterText(run.Game, Text("NoShip"));
        }
        if (_open is null) return;
        if (_open.Parent is null || _open._run != run || !_open._ship.ExistsInSim(run.Game.Sim, falseIfNoParts: false)) { Close(); return; }
        _open.Refresh();
    }

    /// <summary>The first selected ship that the local player commands, else the local player's first ship.</summary>
    static Ship? DefaultShip(Run run)
    {
        var sim = run.Game.Sim;
        return sim.PlayerInput.SelectedCommandableShips.OrderBy(s => s.UniqueID).FirstOrDefault()
            ?? sim.Ships.HumanPlayerShips.Where(s => s.Commands.IsCommandableByLocalPlayer).OrderBy(s => s.UniqueID).FirstOrDefault();
    }

    internal static void Open(Run run, Ship ship)
    {
        Close();
        _open = new TacticsEditor(run, ship);
        run.Game.Gui.FloatingWindows.AddChild(_open);
        Ux.UxKit.FitToScreen(_open, run.Game.Gui.RootWidget.ScreenRect);
    }

    internal static void Close()
    {
        _open?._picker?.Unparent();
        _open?.Unparent();
        _open = null;
    }

    /// <summary>
    /// Autotest: does what a click does. open | close | auto on|off | apply &lt;preset or personal name&gt; | add | remove &lt;row&gt; |
    /// up &lt;row&gt; | cond &lt;row&gt; &lt;Cond or Value&gt; [term] | param &lt;row&gt; &lt;n&gt; [term] | num &lt;row&gt; &lt;n&gt; [term] |
    /// op &lt;row&gt; &lt;|&gt;|= [term] | arg &lt;row&gt; &lt;slot&gt;|&lt;resource&gt;|&lt;slot,resource&gt; [term] | act &lt;row&gt; &lt;Act&gt; | actparam &lt;row&gt; &lt;n&gt; |
    /// addterm &lt;row&gt; | removeterm|insertterm|bracket|unbracket &lt;row&gt; &lt;term&gt; | not &lt;row&gt; &lt;term&gt; | or &lt;row&gt; &lt;term&gt; | copy | paste [text] |
    /// savepreset &lt;name&gt; | deletepreset &lt;name&gt; | toselected | loot v&lt;n&gt;|&lt;id,id&gt; | sell|mine|buy &lt;id,id&gt;|none |
    /// recall on|off | design &lt;slot&gt; &lt;test design&gt;|clear. Rows and terms count from 1, like the window shows them.
    /// </summary>
    internal static void TestCommand(Run run, Ship ship, string arg)
    {
        var a = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (a.Length == 0) throw new InvalidOperationException("tactics_editor needs a verb");
        if (a[0] == "open") { Open(run, ship); return; }
        if (a[0] == "close") { Close(); return; }
        var e = _open ?? throw new InvalidOperationException("the tactics editor is not open");
        int Num(int i) => int.Parse(a[i], CultureInfo.InvariantCulture);
        int Row() => Num(1) - 1;
        int TermAt(int i) => a.Length > i ? Num(i) - 1 : 0;
        string Rest() => string.Join(' ', a.Skip(1));
        switch (a[0])
        {
            case "auto": e._autoToggle.IsSelected = a[1] == "on"; break;
            case "apply":
                int index = e._presetEntries.FindIndex(p => p.Personal == Rest());
                if (index < 0) index = Array.IndexOf(Enum.GetValues<Preset>(), Enum.Parse<Preset>(a[1]));
                e._presets.SelectedWidget = e._presets.ListBox.Children[index];
                e.ApplyPreset();
                break;
            case "add": e.AddRow(); break;
            case "remove": e._rows.RemoveAt(Row()); e.SendRows(); break;
            case "up": e.Swap(Row(), Row() - 1); break;
            case "cond": e.EditBody(Row(), TermAt(3), b => Enum.TryParse<Cond>(a[2], out var c) ? ShipAiModel.Picked(b, c) : e.PickValue(b, Enum.Parse<Value>(a[2]))); break;
            case "param": e.EditBody(Row(), TermAt(3), b => (FactTerm)b with { Param = Num(2) }); break;
            case "num": e.EditBody(Row(), TermAt(3), b => (CompareTerm)b with { Right = null, Number = Num(2) }); break;
            case "op": e.EditBody(Row(), TermAt(3), b => (CompareTerm)b with { Op = a[2] switch { "<" => Op.Less, ">" => Op.Greater, "=" => Op.Equal, _ => throw new InvalidOperationException($"unknown comparison '{a[2]}'") } }); break;
            case "arg": e.EditBody(Row(), TermAt(3), b => (CompareTerm)b with { Left = Arg(((CompareTerm)b).Left, a[2]) }); break;
            case "not": e.EditTerm(Row(), TermAt(2), t => t with { Not = !t.Not }); break;
            case "or": e.EditTerm(Row(), TermAt(2), t => t with { Or = !t.Or }); break;
            case "addterm": e.Edit(Row(), ShipAiModel.AddTerm(e._rows[Row()])); break;
            case "removeterm": e.Edit(Row(), ShipAiModel.RemoveTerm(e._rows[Row()], TermAt(2))); break;
            case "insertterm": e.Edit(Row(), ShipAiModel.InsertTerm(e._rows[Row()], TermAt(2))); break;
            case "bracket": e.Edit(Row(), ShipAiModel.Bracket(e._rows[Row()], TermAt(2))); break;
            case "unbracket": e.Edit(Row(), ShipAiModel.Unbracket(e._rows[Row()], TermAt(2))); break;
            case "act": e.Edit(Row(), ShipAiModel.WithAct(e._rows[Row()], Enum.Parse<Act>(a[2]))); break;
            case "actparam": e.Edit(Row(), e._rows[Row()] with { ActParam = Num(2) }); break;
            case "copy": e.Copy(); break;
            case "paste":
                if (a.Length > 1) _copied = a[1];
                e.Paste();
                break;
            case "savepreset": e._presetName.Text = Rest(); e.SavePreset(); break;
            case "deletepreset":
                e._presets.SelectedWidget = e._presets.ListBox.Children[e._presetEntries.FindIndex(p => p.Personal == Rest())];
                e.DeletePreset();
                break;
            case "toselected": e.ApplyToSelected(); break;
            // Spec 419: the smallest window, then (after a frame) every condition and action name in its list without wrap or clip.
            case "minsize": e.Size = e.ResizeController.MinSize; break;
            case "fit": e.CheckFit(); break;
            case "loot":
                e.EditCargo(a[1].StartsWith('v')
                    ? e._cargo with { LootMinValue = int.Parse(a[1][1..], CultureInfo.InvariantCulture), LootTypes = [] }
                    : e._cargo with { LootTypes = a[1].Split(',') });
                break;
            case "sell": e.EditCargo(e._cargo with { Sell = List(a[1]) }); break;
            case "mine": e.EditCargo(e._cargo with { Mine = List(a[1]) }); break;
            case "buy": e.EditCargo(e._cargo with { Buy = List(a[1]) }); break;
            case "recall": e._recall = a[1] == "on"; e.Send(); break;
            // design <slot> <test design name>|clear: picks a design for the slot, as the ship library would (spec 1084).
            case "design":
                if (a[2] == "clear") e.SetDesign(Num(1), null);
                else e.PickedDesign(Num(1), ShipAiArea.DesignPath(a[2]));
                break;
            default: throw new InvalidOperationException($"unknown tactics_editor verb '{a[0]}'");
        }
    }

    /// <summary>
    /// Spec 419: every condition and action name fits the text area of a shown condition list on one line, and every value of every
    /// parameter kind fits the text area of a shown parameter list, in the lists' own font. Throws with the texts that do not fit.
    /// Limit: a headless game loads no glyph metrics (every character is as wide as the font size), so there it only logs that it
    /// cannot measure. Revisit when test slots draw.
    /// </summary>
    void CheckFit()
    {
        var choice = _choiceLists.FirstOrDefault() ?? throw new InvalidOperationException("the editor shows no rule");
        var param = _paramLists.FirstOrDefault() ?? throw new InvalidOperationException("the editor shows no rule with a parameter");
        if (NaturalWidth(choice.StateNormalTextRenderer, "iiii") >= NaturalWidth(choice.StateNormalTextRenderer, "WWWW"))
        {
            Log.Info("AUTOTEST tactics editor fit: no glyph metrics in this game (headless); the fit is not measured");
            return;
        }
        int stages = _run.T.Heat.Stages.Length;
        var names = Enum.GetValues<Cond>().Select(c => Text("Cond_" + c)).Concat(Enum.GetValues<Value>().Select(v => Text("Value_" + v)))
            .Concat(Enum.GetValues<Act>().Select(a => Text("Act_" + a)));
        var values = Enum.GetValues<ParamKind>().Where(k => k != ParamKind.None).SelectMany(k => ShipAiRegistry.Choices(k).Select(v => ParamName(k, v)))
            .Concat(Enum.GetValues<Unit>().SelectMany(u => ShipAiRegistry.Choices(u, stages).Select(n => NumberName(u, n))));
        var bad = new List<string>();
        float Check(DropList drop, IEnumerable<string> texts)
        {
            var text = drop.StateNormalTextRenderer;
            float area = text.RectProvider.ScreenRect.Width, widest = 0f;
            if (area <= 0f) throw new InvalidOperationException("a list has no text area");
            foreach (string t in texts.Distinct())
            {
                float w = NaturalWidth(text, t);
                if (w <= 0f) throw new InvalidOperationException($"no width measured for '{t}'");
                widest = Math.Max(widest, w);
                if (w > area) bad.Add($"'{t}' (needs {w:0}, has {area:0})");
            }
            return widest;
        }
        float widestName = Check(choice, names), widestValue = Check(param, values);
        Log.Info($"AUTOTEST tactics editor fit at window {Size.X:0}x{Size.Y:0}: widest name {widestName:0} in " +
                 $"{choice.StateNormalTextRenderer.RectProvider.ScreenRect.Width:0}, widest value {widestValue:0} in " +
                 $"{param.StateNormalTextRenderer.RectProvider.ScreenRect.Width:0}, {bad.Count} do not fit");
        if (bad.Count > 0) throw new InvalidOperationException("texts do not fit: " + string.Join(", ", bad));
    }

    /// <summary>The width of a text on one line, in the font and size of a widget text.</summary>
    static float NaturalWidth(Halfling.Gui.Components.Graphics.WidgetTextRenderer like, string text)
    {
        var tr = new Halfling.Graphics.Text.TextRenderer(text, new IntRect(0, 0, 100000, 1000), Halfling.Graphics.PerformanceMode.Dynamic)
        {
            Font = like.Font,
            FontSize = like.FontSize,
        };
        return tr.BoundingRect.Width;
    }

    static string[] List(string arg) => arg == "none" ? [] : arg.Split(',');

    /// <summary>Test verb "arg": a slot number, a resource ID, or "slot,resource".</summary>
    static Operand Arg(Operand o, string arg) => ShipAiRegistry.Of(o.Value).Arg switch
    {
        ArgKind.Resource => o with { Resource = arg },
        ArgKind.Design => o with { Slot = int.Parse(arg, CultureInfo.InvariantCulture) },
        ArgKind.DesignResource => o with { Slot = int.Parse(arg.Split(',')[0], CultureInfo.InvariantCulture), Resource = arg.Split(',')[1] },
        _ => throw new InvalidOperationException($"{o.Value} takes no argument"),
    };

    /// <summary>The own ships that "Apply to selected" changes: the selected ships that the local player commands, without this one.</summary>
    IEnumerable<Ship> SelectedOthers() =>
        _run.Game.Sim.PlayerInput.SelectedCommandableShips.Where(s => s != _ship && s.IsHumanPlayerShip).OrderBy(s => s.UniqueID);

    TacticsEditor(Run run, Ship ship)
    {
        _run = run;
        _ship = ship;
        TitleText = Format("TitleFmt", ship.Metadata.PlainShipName);
        TitleLabel.TextRenderer.HAlignment = HAlignment.Left;
        TitleLabel.TextRenderer.Padding = new Borders(10f, 0f, 0f, 0f);
        Size = new Vector2(1400f, 760f);
        ResizeController.MinSize = new Vector2(1300f, 420f);

        var page = new ScrollBox();
        page.AnchorRect = AnchorPresets.Fill;
        page.NineSlice.Flags = NineSliceFlags.None;
        page.ScrollBarController.ShowHorizontal = ScrollBarShowMode.Never;
        page.Children.LayoutAlgorithm = LayoutAlgorithms.StretchTopToBottom;
        page.Children.BorderPadding = new Borders(16f, 12f, 16f, 12f);
        page.Children.WidgetPadding = new Vector2(8f);
        AddChild(page);

        var bar = Strip(40f);
        _autoToggle = new ToggleButton(ToggleButton.Style.Check) { Text = Text("Auto") };
        _autoToggle.ToolTips.TextProvider = new Ux.LiveText(() => Text("AutoTip"));
        _autoToggle.SelectionChanged += (_, _) =>
        {
            if (_updating) return;
            _auto = _autoToggle.IsSelected;
            Send();
        };
        Place(bar, _autoToggle, 0f, 26f);
        _presets = new DropList();
        _presets.ToolTips.TextProvider = new Ux.LiveText(() => Text("PresetTip"));
        _presets.ListBox.SelectionManager.WidgetSelected += (_, _) => UpdatePresetButtons();
        Place(bar, _presets, 26f, 22f);
        Place(bar, Ui.FlatButton.New(Text("Apply"), ApplyPreset), 48f, 9f);
        _deletePreset = Ui.FlatButton.New(Text("DeletePreset"), DeletePreset);
        Place(bar, _deletePreset, 57f, 9f);
        Place(bar, Ui.FlatButton.New(Text("Copy"), Copy), 66f, 11f);
        Place(bar, Ui.FlatButton.New(Text("Paste"), Paste), 77f, 11f);
        Place(bar, Ui.FlatButton.New(Text("Clear"), () => { _rows.Clear(); SendRows(); }), 88f, 12f);
        page.Children.Add(bar);

        var bar2 = Strip(36f);
        var nameLabel = new Label { Text = Text("PresetName") };
        nameLabel.TextRenderer.HAlignment = HAlignment.Right;
        Place(bar2, nameLabel, 0f, 26f);
        _presetName = new TextEditField(TextEditField.Style.SingleLine) { MaxTextLength = ShipAiModel.MaxPresetName };
        _presetName.ToolTips.TextProvider = new Ux.LiveText(() => Text("PresetNameTip"));
        Place(bar2, _presetName, 26f, 22f);
        Place(bar2, Ui.FlatButton.New(Text("SavePreset"), SavePreset), 48f, 18f);
        _toSelected = Ui.FlatButton.New(Text("ToSelected"), ApplyToSelected);
        _toSelected.ToolTips.TextProvider = new Ux.LiveText(() => Text("ToSelectedTip"));
        Place(bar2, _toSelected, 66f, 34f);
        page.Children.Add(bar2);
        FillPresets(null);

        _cargoBar = Strip(36f);
        page.Children.Add(_cargoBar);
        _designBar = Strip(36f);
        page.Children.Add(_designBar);

        _status = NewLabel(18);
        page.Children.Add(_status);

        _list = new LayoutBox();
        _list.NineSlice.Flags = NineSliceFlags.None;
        _list.PercentileWidth = 100f;
        _list.AutoSize.AutoHeightMode = AutoSizeMode.Enable;
        _list.Children.LayoutAlgorithm = LayoutAlgorithms.StretchTopToBottom;
        // Spec 419: a small gap between the framed rules.
        _list.Children.WidgetPadding = new Vector2(8f);
        page.Children.Add(_list);

        _add = Ui.FlatButton.New(Text("AddRow"), AddRow);
        _add.Height = 34f;
        _add.PercentileWidth = 30f;
        page.Children.Add(_add);

        var help = NewLabel(15);
        help.Text = Text("Help");
        page.Children.Add(help);

        Parented += (_, _) =>
        {
            var parent = Parent ?? throw new InvalidOperationException("tactics editor parented without a parent");
            ScreenRect = Rect.CenteredOn(parent.RectProvider.ScreenRect.Size / 2f, Size);
            BoundsProvider = parent.RectProvider;
        };
        Unparenting += (_, _) => BoundsProvider = null;
        WidgetRules.AddDialogTweens(this);
        WidgetRules.AddBlur(this);
        LoadFromSim();
    }

    // ---- Widgets

    static SimpleBox Strip(float height)
    {
        var box = new SimpleBox();
        box.NineSlice.Flags = NineSliceFlags.None;
        box.PercentileWidth = 100f;
        box.Height = height;
        return box;
    }

    static void Place(SimpleBox box, Widget widget, float left, float width)
    {
        widget.PercentileRect = new Rect(left, 0f, width, 100f);
        widget.Right = -6f;
        box.AddChild(widget);
    }

    static Label NewLabel(int fontSize)
    {
        var label = new Label();
        label.NineSlice.Flags = NineSliceFlags.None;
        label.PercentileWidth = 100f;
        label.AutoSize.AutoHeightMode = AutoSizeMode.Enable;
        label.TextRenderer.XmlFormatting = true;
        label.TextRenderer.FontSize = fontSize;
        label.TextRenderer.HAlignment = HAlignment.Left;
        return label;
    }

    /// <summary>A drop list of values with display texts. The current value is selected; a value outside the list is added first.</summary>
    DropList Choices(IEnumerable<int> values, int current, Func<int, string> name, Action<int> picked)
    {
        var list = values.ToList();
        if (!list.Contains(current)) list.Insert(0, current);
        var drop = new DropList();
        foreach (int v in list) drop.ListBox.Children.Add(new ListItem { Text = name(v) });
        drop.SelectedWidget = drop.ListBox.Children[list.IndexOf(current)];
        drop.ListBox.SelectionManager.WidgetSelected += (_, _) =>
        {
            if (_updating || drop.SelectedWidget is not { } w) return;
            int v = list[drop.ListBox.Children.IndexOf(w)];
            if (v != current) Errors.Guard("tactics editor pick", () => picked(v));
        };
        return drop;
    }

    /// <summary>A fact or action parameter as the lists show it.</summary>
    string ParamName(ParamKind kind, int value) => kind switch
    {
        ParamKind.PartKind => Text("PartKind_" + (PartKind)value),
        ParamKind.ShipClass => Text("ShipClass_" + (ShipClass)value),
        ParamKind.GuardWho => Text("GuardWho_" + (GuardWho)value),
        ParamKind.Side => Text("Side_" + (Side)value),
        ParamKind.Station => Text("Station_" + (StationPick)value),
        ParamKind.WeaponGroup => value == 0 ? Text("GroupAll") : Format("GroupFmt", value),
        ParamKind.Spacing => Format("DistanceFmt", value),
        ParamKind.Design => SlotName(value),
        ParamKind.Flag => Format("FlagFmt", value),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    /// <summary>Spec 2323: a number of a unit as the lists show it.</summary>
    static string NumberName(Unit unit, int n) => Format("Unit" + unit + "Fmt", n.ToString("N0", CultureInfo.InvariantCulture));

    /// <summary>A value with its arguments, as the other-side list and the status show it.</summary>
    string OperandName(Operand o) => ShipAiRegistry.Of(o.Value).Arg switch
    {
        ArgKind.None => Text("Value_" + o.Value),
        ArgKind.Resource => Format("ArgFmt", Text("Value_" + o.Value), ResourceName(o.Resource)),
        ArgKind.Design => Format("ArgFmt", Text("Value_" + o.Value), SlotName(o.Slot)),
        ArgKind.DesignResource => Format("Arg2Fmt", Text("Value_" + o.Value), SlotName(o.Slot), ResourceName(o.Resource)),
        _ => throw new ArgumentOutOfRangeException(nameof(o), o.Value, null),
    };

    /// <summary>The other side of a comparison: like OperandName, without the resource, which the left side shows.</summary>
    string RightName(Operand o) => ShipAiRegistry.Of(o.Value).Arg switch
    {
        ArgKind.Resource => Text("Value_" + o.Value),
        ArgKind.DesignResource => Format("ArgFmt", Text("Value_" + o.Value), SlotName(o.Slot)),
        _ => OperandName(o),
    };

    static string ResourceName(string id) => new Cosmoteer.Data.ID<Cosmoteer.Resources.ResourceRules>(id).GetResourceRules().NameKey.Text;

    /// <summary>Spec 2322: a design slot as the lists show it: its number and its design name, or that it is empty.</summary>
    string SlotName(int slot) => _cargo.Design(slot) is { } d ? Format("DesignFmt", slot, d.Name) : Format("DesignEmptyFmt", slot);

    void BuildRows()
    {
        _updating = true;
        try
        {
            _list.Children.Clear();
            _markers.Clear();
            _choiceLists.Clear();
            _paramLists.Clear();
            for (int i = 0; i < _rows.Count; i++) _list.Children.Add(BuildRow(i));
            if (_rows.Count == 0)
            {
                var empty = NewLabel(16);
                empty.Text = Text("NoRows");
                _list.Children.Add(empty);
            }
            _add.SelfInputActive = _rows.Count < ShipAiModel.MaxRows;
            BuildCargo();
            BuildDesigns();
        }
        finally { _updating = false; }
    }

    /// <summary>Spec 1062, 1064: the loot filter, the three type lists and the return switch.</summary>
    void BuildCargo()
    {
        _cargoBar.Children.Clear();
        var title = new Label { Text = AText("Cargo") };
        title.ToolTips.TextProvider = new Ux.LiveText(() => AText("CargoTip"));
        Place(_cargoBar, title, 0f, 6f);
        // The loot mode list: the value steps, then "these types" (-1).
        var values = Autonomy.AutonomyModel.LootValueChoices.Append(-1);
        int current = _cargo.LootTypes.Length > 0 ? -1 : _cargo.LootMinValue;
        var loot = Choices(values, current, v => v < 0 ? AText("LootTypes") : v == 0 ? AText("LootAll") : AFormat("LootValueFmt", v),
            v => EditCargo(v < 0 ? _cargo with { LootTypes = Offered(false).Take(1).Select(r => r.ID.ToString()).ToArray() } : _cargo with { LootMinValue = v, LootTypes = [] }));
        loot.ToolTips.TextProvider = new Ux.LiveText(() => AText("LootTip"));
        Place(_cargoBar, loot, 6f, 16f);
        if (_cargo.LootTypes.Length > 0)
            Place(_cargoBar, Ui.FlatButton.New(AFormat("LootTypesFmt", Count(_cargo.LootTypes, "ListNone")),
                () => Pick(AText("LootTypes"), false, _cargo.LootTypes, l => _cargo with { LootTypes = l.Length > 0 ? l : _cargo.LootTypes })), 22f, 14f);
        Place(_cargoBar, Ui.FlatButton.New(AFormat("SellFmt", Count(_cargo.Sell, "ListAll")), () => Pick(AFormat("SellFmt", ""), false, _cargo.Sell, l => _cargo with { Sell = l })), 36f, 14f);
        Place(_cargoBar, Ui.FlatButton.New(AFormat("MineFmt", Count(_cargo.Mine, "ListAny")), () => Pick(AFormat("MineFmt", ""), true, _cargo.Mine, l => _cargo with { Mine = l })), 50f, 14f);
        Place(_cargoBar, Ui.FlatButton.New(AFormat("BuyFmt", Count(_cargo.Buy, "ListNone")), () => Pick(AFormat("BuyFmt", ""), false, _cargo.Buy, l => _cargo with { Buy = l })), 64f, 14f);
        // Spec 1064: only an autonomous ship needs a recall switch; the player gives a crewed ship orders.
        if (!Autonomy.Autonomous.IsAutonomous(_ship)) return;
        var recall = Ui.FlatButton.New(AText(_recall ? "ReturnOn" : "ReturnOff"), () => { _recall = !_recall; Send(); _dirty = true; }, AText("ReturnTip"));
        Place(_cargoBar, recall, 78f, 22f);
    }

    /// <summary>Spec 1084: a button per design slot that picks a saved design from the vanilla ship library, and one that clears it.</summary>
    void BuildDesigns()
    {
        _designBar.Children.Clear();
        var title = new Label { Text = AText("Designs") };
        title.ToolTips.TextProvider = new Ux.LiveText(() => AText("DesignsTip"));
        Place(_designBar, title, 0f, 6f);
        for (int slot = 1; slot <= Autonomy.BuildModel.MaxDesigns; slot++)
        {
            int s = slot;
            float left = 6f + (s - 1) * 31f;
            string text = _cargo.Design(s) is { } d ? AFormat("DesignSlotFmt", s, d.Name) : AFormat("DesignPickFmt", s);
            Place(_designBar, Ui.FlatButton.New(text, () => ShowLibrary(s), AText("DesignPickTip")), left, 26f);
            if (_cargo.Design(s) is not null) Place(_designBar, Ui.FlatButton.New(AText("DesignClear"), () => SetDesign(s, null), AText("DesignClearTip")), left + 26f, 5f);
        }
    }

    async void ShowLibrary(int slot)
    {
        // async void: an exception here would end the game, so it reports instead.
        try
        {
            string? path = await ShipLibraryDialog.Show(ShipDialogMode.Load);
            if (path is not null && _open == this) PickedDesign(slot, path);
        }
        catch (Exception ex) { Errors.Report("tactics editor design pick", ex.Message, ex); }
    }

    /// <summary>Spec 1084: copies the design file into the slot. A design of another ship class is refused, as vanilla refuses it.</summary>
    void PickedDesign(int slot, string path)
    {
        Autonomy.BuildDesign d;
        try { d = Autonomy.Build.FromFile(path); }
        catch (FormatException ex) { Note(Format("PasteBadFmt", ex.Message)); return; }
        if (Autonomy.Build.InfoOf(d).Rules != _ship.Rules.ID) { Note(AText("DesignWrongClass")); return; }
        SetDesign(slot, d);
    }

    /// <summary>A slot that a row names cannot be cleared: the tactics input would refuse the rows (spec 2322).</summary>
    void SetDesign(int slot, Autonomy.BuildDesign? d)
    {
        var designs = (Autonomy.BuildDesign?[])_cargo.Designs.Clone();
        designs[slot - 1] = d;
        if (d is null && _rows.Any(r => ShipAiModel.DesignSlots(r).Contains(slot))) { Note(AText("DesignInUse")); return; }
        EditCargo(_cargo with { Designs = designs });
    }

    static IReadOnlyList<Cosmoteer.Resources.ResourceRules> Offered(bool mine) => CargoPicker.Offered(mine);

    /// <summary>A list as the button shows it: one name, "n types", or the word for an empty list.</summary>
    static string Count(string[] ids, string emptyKey) => ids.Length switch
    {
        0 => AText(emptyKey),
        1 => new Cosmoteer.Data.ID<Cosmoteer.Resources.ResourceRules>(ids[0]).GetResourceRules().NameKey.Text,
        _ => AFormat("ListCountFmt", ids.Length),
    };

    void Pick(string title, bool mine, string[] current, Func<string[], Autonomy.CargoSettings> change)
    {
        _picker?.Unparent();
        _picker = new CargoPicker(AFormat("PickTitleFmt", title), mine, current, list => EditCargo(change(list)));
        _run.Game.Gui.FloatingWindows.AddChild(_picker);
    }

    void EditCargo(Autonomy.CargoSettings cargo)
    {
        _cargo = cargo;
        _dirty = true;
        Send();
    }

    /// <summary>
    /// Spec 419. One framed block per rule (the vanilla box style): a line per condition term (the IF part), then the THEN line
    /// with the action, its parameter and the rule buttons, indented under the conditions.
    /// </summary>
    LayoutBox BuildRow(int i)
    {
        var row = _rows[i];
        var block = new LayoutBox();
        block.PercentileWidth = 100f;
        block.AutoSize.AutoHeightMode = AutoSizeMode.Enable;
        block.Children.LayoutAlgorithm = LayoutAlgorithms.StretchTopToBottom;
        block.Children.BorderPadding = new Borders(6f);
        block.Children.WidgetPadding = new Vector2(2f);
        var depths = ShipAiModel.Depths(row.Terms);
        for (int j = 0; j < row.Terms.Length; j++)
        {
            var line = Strip(34f);
            BuildTerm(line, i, j, depths[j] - row.Terms[j].Open);
            block.Children.Add(line);
        }
        var then = Strip(34f);
        BuildAction(then, i);
        block.Children.Add(then);
        return block;
    }

    // Spec 419: column starts and widths in percent of the rule line, at the 1,300 minimum width. A term line: the group list, the entry
    // list, then for a fact its parameter, for a value its argument, the comparison, the other side and its switch.
    const float ColMarker = 0f, ColJoin = 3f, ColNot = 8f, ColGroup = 13f, GroupWidth = 9f, ColEntry = 22f, EntryWidth = 19f,
        ColArg = 41f, ArgWidth = 12f, ColOp = 53f, OpWidth = 4f, ColRight = 57f, RightWidth = 15f, ColSwitch = 72f, SwitchWidth = 6f, ColEnd = 78f;

    // Spec 2321: each bracket level indents the term line by IndentStep. Limit: the indent stops at MaxIndent levels, so the
    // group list keeps room for its longest name; deeper levels show only their brackets. Revisit if players nest deeper.
    const float IndentStep = 1.5f, BracketWidth = 2f;
    const int MaxIndent = 4;

    /// <summary>Spec 419: the groups of the condition list, in list order, and the facts and values of each, in registry order.</summary>
    static readonly Group[] TermGroups = [Group.Always, Group.Ship, Group.Enemies, Group.Fleet, Group.World, Group.Run, Group.Cargo, Group.Flags];

    static readonly Group[] ActGroups = [Group.Target, Group.Move, Group.Fire, Group.Facing, Group.Trade, Group.Build, Group.Flags];

    static Group GroupOf(TermBody body) => body switch
    {
        FactTerm f => ShipAiRegistry.Of(f.Cond).Group,
        CompareTerm c => ShipAiRegistry.Of(c.Left.Value).Group,
        _ => throw new ArgumentOutOfRangeException(nameof(body), body, null),
    };

    /// <summary>The entries of a group: its facts, then its values. An entry is a Cond or a Value.</summary>
    static List<Enum> EntriesOf(Group g) =>
        ShipAiRegistry.Facts.Where(f => f.Group == g).Select(f => (Enum)f.Cond).Concat(ShipAiRegistry.Values.Where(v => v.Group == g).Select(v => (Enum)v.Value)).ToList();

    static string EntryName(Enum e) => e is Cond c ? Text("Cond_" + c) : Text("Value_" + (Value)e);

    static Enum EntryOf(TermBody body) => body switch
    {
        FactTerm f => f.Cond,
        CompareTerm c => c.Left.Value,
        _ => throw new ArgumentOutOfRangeException(nameof(body), body, null),
    };

    /// <summary>The body that picking an entry gives (ShipAiModel.Picked); a value with a resource starts with the first offered resource.</summary>
    TermBody Pick(TermBody old, Enum entry) => entry is Cond c ? ShipAiModel.Picked(old, c) : PickValue(old, (Value)entry);

    TermBody PickValue(TermBody old, Value v) =>
        ShipAiModel.Picked(old, v, old is CompareTerm { Left.Resource.Length: > 0 } c ? c.Left.Resource : Offered(false).First().ID.ToString());

    /// <summary>A drop list of entries with names and a tooltip that describes the selected entry (strings "Desc_" plus its name).</summary>
    DropList EntryList<T>(IReadOnlyList<T> entries, T current, Func<T, string> name, Func<T, string> desc, Action<T> picked) where T : notnull
    {
        var drop = Choices(Enumerable.Range(0, entries.Count), Math.Max(0, entries.ToList().IndexOf(current)), i => name(entries[i]), i => picked(entries[i]));
        drop.ToolTips.TextProvider = new Ux.LiveText(() => desc(current));
        return drop;
    }

    static string Desc(string key) => Text("Desc_" + key);

    /// <summary>Spec 419, 2321. outer: the bracket depth before the brackets that this term opens.</summary>
    void BuildTerm(SimpleBox line, int i, int j, int outer)
    {
        var row = _rows[i];
        var term = row.Terms[j];
        float indent = Math.Min(outer, MaxIndent) * IndentStep;
        if (j == 0)
        {
            var marker = new Label();
            marker.TextRenderer.XmlFormatting = true;
            marker.TextRenderer.FontSize = 18;
            marker.TextRenderer.HAlignment = HAlignment.Center;
            _markers.Add(marker);
            Place(line, marker, ColMarker, ColJoin - ColMarker);
            var when = new Label { Text = Text("If") };
            when.TextRenderer.HAlignment = HAlignment.Center;
            Place(line, when, ColJoin, ColNot - ColJoin);
        }
        else Place(line, Ui.FlatButton.New(Text(term.Or ? "Or" : "And"), () => EditTerm(i, j, t => t with { Or = !t.Or }), Text("ConnectorTip")), ColJoin + indent, ColNot - ColJoin);
        // The opening brackets stand before NOT, as in the text form: a NOT belongs to its term, never to the bracket.
        if (term.Open > 0) Place(line, Bracket(new string('(', term.Open)), ColNot + indent, BracketWidth);
        Place(line, Ui.FlatButton.New(Text(term.Not ? "NotOn" : "NotOff"), () => EditTerm(i, j, t => t with { Not = !t.Not }), Text("NotTip")), ColNot + indent + BracketWidth, ColGroup - ColNot - BracketWidth);
        var group = GroupOf(term.Body);
        var groupList = EntryList(TermGroups, group, g => Text("Group_" + g), g => Text("GroupTip"),
            g => EditBody(i, j, b => Pick(b, EntriesOf(g)[0])));
        _choiceLists.Add(groupList);
        Place(line, groupList, ColGroup + indent, GroupWidth);
        var entries = EntriesOf(group);
        // The Always group has one entry: the group list says it all.
        if (entries.Count > 1)
        {
            var entryList = EntryList(entries, EntryOf(term.Body), EntryName, e => Desc(e.ToString()), e => EditBody(i, j, b => Pick(b, e)));
            _choiceLists.Add(entryList);
            Place(line, entryList, ColEntry + indent, EntryWidth - indent);
        }
        switch (term.Body)
        {
            case FactTerm f when ShipAiModel.ParamOf(f.Cond) is var kind and not ParamKind.None:
                var param = Choices(ShipAiRegistry.Choices(kind), f.Param, v => ParamName(kind, v), v => EditBody(i, j, _ => f with { Param = v }));
                _paramLists.Add(param);
                // A group with one entry shows no entry list: its parameter takes that place.
                if (entries.Count > 1) Place(line, param, ColArg, ArgWidth);
                else Place(line, param, ColEntry + indent, EntryWidth - indent);
                break;
            case CompareTerm c:
                BuildCompare(line, i, j, c);
                break;
        }
        if (term.Close > 0) Place(line, Bracket(new string(')', term.Close)), ColEnd, BracketWidth);
        Place(line, TipButton("InsertTerm", "InsertTermTip", () => Edit(i, ShipAiModel.InsertTerm(_rows[i], j))), 80f, 4f);
        Place(line, TipButton("BracketTerm", "BracketTermTip", () => Edit(i, ShipAiModel.Bracket(_rows[i], j))), 84f, 5f);
        if (term.Open > 0) Place(line, TipButton("Unbracket", "UnbracketTip", () => Edit(i, ShipAiModel.Unbracket(_rows[i], j))), 89f, 6f);
        if (row.Terms.Length > 1) Place(line, Ui.FlatButton.New(Text("RemoveTerm"), () => Edit(i, ShipAiModel.RemoveTerm(_rows[i], j))), 95f, 5f);
    }

    /// <summary>Spec 419, 2323: the argument list, the comparison button, the other side (numbers or values) and its switch.</summary>
    void BuildCompare(SimpleBox line, int i, int j, CompareTerm c)
    {
        var unit = ShipAiModel.UnitOf(c.Left.Value);
        var args = ArgChoices(c.Left);
        if (args.Count > 0)
        {
            var argList = EntryList(args, c.Left, OperandArgName, _ => "", o => EditBody(i, j, _ => c with { Left = o }));
            _paramLists.Add(argList);
            Place(line, argList, ColArg, ArgWidth);
        }
        Place(line, Ui.FlatButton.New(ShipAiModel.OpChar(c.Op).ToString(), () => EditBody(i, j, _ => c with { Op = ShipAiModel.NextOp(c.Op) }), Text("OpTip")), ColOp, OpWidth);
        if (c.Right is { } right)
        {
            var values = RightChoices(c.Left);
            if (!values.Contains(right)) values.Insert(0, right);
            var rightList = EntryList(values, right, RightName, o => Desc(o.Value.ToString()), o => EditBody(i, j, _ => c with { Right = o }));
            _paramLists.Add(rightList);
            Place(line, rightList, ColRight, RightWidth);
        }
        else
        {
            var numbers = Choices(ShipAiRegistry.Choices(unit, _run.T.Heat.Stages.Length), c.Number, n => NumberName(unit, n), n => EditBody(i, j, _ => c with { Number = n }));
            _paramLists.Add(numbers);
            Place(line, numbers, ColRight, RightWidth);
        }
        // The switch: a number on the other side, or a value of the same unit (when there is one to offer).
        if (c.Right is not null || RightChoices(c.Left).Count > 0)
            Place(line, Ui.FlatButton.New(Text(c.Right is null ? "RightValue" : "RightNumber"),
                () => EditBody(i, j, _ => c.Right is null ? c with { Right = RightChoices(c.Left)[0] } : c with { Right = null }),
                Text(c.Right is null ? "RightValueTip" : "RightNumberTip")), ColSwitch, SwitchWidth);
    }

    /// <summary>Spec 2322: the arguments that a value can take: every offered resource, every design slot, or each filled slot with
    /// each resource of its design's cost.</summary>
    List<Operand> ArgChoices(Operand o) => ShipAiRegistry.Of(o.Value).Arg switch
    {
        ArgKind.None => [],
        ArgKind.Resource => Offered(false).Select(r => o with { Resource = r.ID.ToString() }).ToList(),
        ArgKind.Design => Enumerable.Range(1, Autonomy.BuildModel.MaxDesigns).Select(slot => o with { Slot = slot }).ToList(),
        ArgKind.DesignResource => Enumerable.Range(1, Autonomy.BuildModel.MaxDesigns).Where(slot => _cargo.Design(slot) is not null)
            .SelectMany(slot => Autonomy.Build.InfoOf(_cargo.Design(slot)!).Cost.Keys.Select(res => o with { Slot = slot, Resource = res })).DefaultIfEmpty(o).ToList(),
        _ => throw new ArgumentOutOfRangeException(nameof(o), o.Value, null),
    };

    string OperandArgName(Operand o) => ShipAiRegistry.Of(o.Value).Arg switch
    {
        ArgKind.Resource => ResourceName(o.Resource),
        ArgKind.Design => SlotName(o.Slot),
        ArgKind.DesignResource => Format("ArgPairFmt", SlotName(o.Slot), o.Resource.Length > 0 ? ResourceName(o.Resource) : "-"),
        _ => throw new ArgumentOutOfRangeException(nameof(o), o.Value, null),
    };

    /// <summary>
    /// Spec 419: the values that the other side can be: every value of the left side's unit except the left side itself, with its
    /// arguments filled in: the left side's resource, and each filled design slot.
    /// </summary>
    List<Operand> RightChoices(Operand left)
    {
        var unit = ShipAiModel.UnitOf(left.Value);
        string res = left.Resource.Length > 0 ? left.Resource : Offered(false).First().ID.ToString();
        var slots = Enumerable.Range(1, Autonomy.BuildModel.MaxDesigns).Where(slot => _cargo.Design(slot) is not null).ToList();
        return ShipAiRegistry.Values.Where(v => v.Unit == unit).SelectMany(v => v.Arg switch
        {
            ArgKind.None => [new Operand(v.Value)],
            ArgKind.Resource => [new Operand(v.Value, Resource: res)],
            ArgKind.Design => slots.Select(slot => new Operand(v.Value, slot)),
            ArgKind.DesignResource => slots.Select(slot => new Operand(v.Value, slot, res)),
            _ => throw new ArgumentOutOfRangeException(nameof(v), v.Arg, null),
        }).Where(o => o != left).ToList();
    }

    /// <summary>The brackets of a term line, in the size of the AND/OR text.</summary>
    static Label Bracket(string text)
    {
        var label = new Label { Text = text };
        label.TextRenderer.FontSize = 22;
        label.TextRenderer.HAlignment = HAlignment.Center;
        return label;
    }

    static Button TipButton(string key, string tipKey, Action click)
    {
        var b = Ui.FlatButton.New(Text(key), click);
        b.ToolTips.TextProvider = new Ux.LiveText(() => Text(tipKey));
        return b;
    }

    void BuildAction(SimpleBox line, int i)
    {
        var row = _rows[i];
        // Indented under the conditions: THEN sits in the NOT column, the action in the group and entry columns.
        var then = new Label { Text = Text("Then") };
        then.TextRenderer.HAlignment = HAlignment.Center;
        Place(line, then, ColNot, ColGroup - ColNot);
        var group = ShipAiRegistry.Of(row.Act).Group;
        var groupList = EntryList(ActGroups, group, g => Text("Group_" + g), _ => Text("ActGroupTip"),
            g => Edit(i, ShipAiModel.WithAct(_rows[i], ShipAiRegistry.Acts.First(a => a.Group == g).Act)));
        _choiceLists.Add(groupList);
        Place(line, groupList, ColGroup, GroupWidth);
        var acts = ShipAiRegistry.Acts.Where(a => a.Group == group).Select(a => a.Act).ToList();
        var act = EntryList(acts, row.Act, a => Text("Act_" + a), a => Desc(a.ToString()), a => Edit(i, ShipAiModel.WithAct(_rows[i], a)));
        _choiceLists.Add(act);
        Place(line, act, ColEntry, EntryWidth);
        var actKind = ShipAiModel.ParamOf(row.Act);
        if (actKind != ParamKind.None)
        {
            var param = Choices(ShipAiRegistry.Choices(actKind), row.ActParam, v => ParamName(actKind, v), v => Edit(i, _rows[i] with { ActParam = v }));
            _paramLists.Add(param);
            Place(line, param, ColArg, ArgWidth);
        }
        Place(line, TipButton("AddTerm", "AddTermTip", () => Edit(i, ShipAiModel.AddTerm(_rows[i]))), ColEnd, 7f);
        var up = Ui.FlatButton.New(Text("Up"), () => Swap(i, i - 1));
        up.SelfInputActive = i > 0;
        Place(line, up, 84f, 5f);
        var down = Ui.FlatButton.New(Text("Down"), () => Swap(i, i + 1));
        down.SelfInputActive = i < _rows.Count - 1;
        Place(line, down, 89f, 6f);
        Place(line, Ui.FlatButton.New(Text("Remove"), () => { _rows.RemoveAt(i); SendRows(); }), 95f, 5f);
    }

    // ---- Edits

    void AddRow()
    {
        if (_rows.Count >= ShipAiModel.MaxRows) return;
        _rows.Add(new TacticRow(Term.Fact(Cond.Always), Act.TargetNearest));
        SendRows();
    }

    void EditTerm(int i, int j, Func<Term, Term> change) => Edit(i, ShipAiModel.WithTerm(_rows[i], j, change(_rows[i].Terms[j])));

    void EditBody(int i, int j, Func<TermBody, TermBody> change) => Edit(i, ShipAiModel.WithBody(_rows[i], j, change(_rows[i].Terms[j].Body)));

    void Edit(int i, TacticRow row)
    {
        _rows[i] = row;
        SendRows();
    }

    void Swap(int a, int b)
    {
        if (b < 0 || b >= _rows.Count) return;
        (_rows[a], _rows[b]) = (_rows[b], _rows[a]);
        SendRows();
    }

    void SendRows()
    {
        _dirty = true;
        Send();
    }

    /// <summary>
    /// Spec 2322: rows that name an empty design slot or an unknown resource are not sent; the sim would refuse them. The note says
    /// why, and the window shows the sim's rows again.
    /// </summary>
    void Send()
    {
        if ((ShipAiModel.DesignError(_rows, slot => _cargo.Design(slot) is not null) ?? ShipAiModel.ResourceError(_rows, Autonomy.Cargo.IsCargoResource)) is { } error)
        {
            Note(Format("PasteBadFmt", error));
            LoadFromSim();
            return;
        }
        string text = ShipAiModel.Encode(_rows), cargo = Autonomy.AutonomyModel.Encode(_cargo);
        _sent = _shown = StateKey(text, _auto, cargo, _recall);
        _sentAt = App.Clock.Time.Seconds;
        _run.Game.NetManager.EnqueueInput(new TacticsInput(_ship, text, _auto, cargo, _recall));
    }

    /// <summary>
    /// Autotests never touch the system clipboard: test games run on a PC that a person uses. They use the in-game buffer only.
    /// </summary>
    static bool UseSystemClipboard => !Dev.Autotest.IsActive;

    /// <summary>Spec 1063: the rows, and the cargo text on a second line when the ship has cargo settings.</summary>
    void Copy()
    {
        string cargo = Autonomy.AutonomyModel.Encode(_cargo);
        _copied = ShipAiModel.Encode(_rows) + (cargo.Length > 0 ? "\n" + cargo : "");
        if (UseSystemClipboard) App.Platform.SetClipboardText(_copied);
        Note(Text("Copied"));
    }

    /// <summary>Takes tactics text from the system clipboard, else from the in-game buffer (the last copy in this game).</summary>
    void Paste()
    {
        string? text = UseSystemClipboard ? App.Platform.GetClipboardText()?.Trim() : null;
        if (text is null || !text.StartsWith(ShipAiModel.Prefix, StringComparison.Ordinal)) text = _copied;
        if (text is null) { Note(Text("PasteEmpty")); return; }
        var lines = text.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        try
        {
            var rows = ShipAiModel.Decode(lines[0]);
            var cargo = lines.Length > 1 ? Autonomy.Cargo.Decode(lines[1]) : _cargo;
            if (ShipAiModel.DesignError(rows, slot => cargo.Design(slot) is not null) is { } error) throw new FormatException(error);
            (_rows, _cargo) = (rows, cargo);
        }
        catch (FormatException ex) { Note(Format("PasteBadFmt", ex.Message)); return; }
        SendRows();
        Note(Text("Pasted"));
    }

    // ---- Presets (spec 406, 422) and fleet apply (spec 421)

    /// <summary>Fills the preset list: the built-ins, then the personal presets. select: the personal preset to select, else the first entry.</summary>
    void FillPresets(string? select)
    {
        _presetEntries.Clear();
        _presets.ListBox.Children.Clear();
        foreach (var p in Enum.GetValues<Preset>())
        {
            _presetEntries.Add((ShipAiModel.Encode(ShipAiModel.PresetRows(p)), null));
            _presets.ListBox.Children.Add(new ListItem { Text = Text("Preset_" + p) });
        }
        foreach (var (name, text) in TacticsPresets.Load())
        {
            _presetEntries.Add((text, name));
            _presets.ListBox.Children.Add(new ListItem { Text = Format("PersonalFmt", name) });
        }
        int index = Math.Max(0, _presetEntries.FindIndex(p => p.Personal is not null && p.Personal == select));
        _presets.SelectedWidget = _presets.ListBox.Children[index];
        UpdatePresetButtons();
    }

    (string Text, string? Personal) SelectedPreset =>
        _presetEntries[_presets.ListBox.Children.IndexOf(_presets.SelectedWidget ?? throw new InvalidOperationException("no preset selected"))];

    void UpdatePresetButtons()
    {
        if (_deletePreset is null || _presets.SelectedWidget is null) return;
        _deletePreset.SelfInputActive = SelectedPreset.Personal is not null;
    }

    void ApplyPreset()
    {
        _rows = ShipAiModel.Decode(SelectedPreset.Text);
        SendRows();
    }

    void SavePreset()
    {
        if (_rows.Count == 0) { Note(Text("PresetNoRows")); return; }
        if (ShipAiModel.CleanPresetName(_presetName.Text).Length == 0) { Note(Text("PresetNoName")); return; }
        string name = TacticsPresets.Save(_presetName.Text, ShipAiModel.Encode(_rows));
        FillPresets(name);
        Note(Format("PresetSavedFmt", name));
    }

    void DeletePreset()
    {
        if (SelectedPreset.Personal is not { } name) return;
        TacticsPresets.Delete(name);
        FillPresets(null);
        Note(Format("PresetDeletedFmt", name));
    }

    /// <summary>Spec 421: one tactics input per selected own ship, with this window's rows and Auto switch. The sim checks permission.</summary>
    void ApplyToSelected()
    {
        var ships = SelectedOthers().ToList();
        if (ships.Count == 0) { Note(Text("ToSelectedNone")); return; }
        string text = ShipAiModel.Encode(_rows), cargo = Autonomy.AutonomyModel.Encode(_cargo);
        foreach (var ship in ships) _run.Game.NetManager.EnqueueInput(new TacticsInput(ship, text, _auto, cargo, ShipTactics.Of(ship)?.Recall ?? false));
        Note(Format("ToSelectedFmt", ships.Count));
    }

    void Note(string text) => (_note, _noteUntil) = (text, App.Clock.Time.Seconds + 3.0);

    // ---- Sim state

    void LoadFromSim()
    {
        var t = ShipTactics.Of(_ship);
        _shown = StateKey(t);
        _rows = t?.Rows.ToList() ?? [];
        _auto = t?.Auto ?? false;
        _cargo = t?.Cargo ?? Autonomy.CargoSettings.Default;
        _recall = t?.Recall ?? false;
        _updating = true;
        _autoToggle.IsSelected = _auto;
        _updating = false;
        _dirty = true;
        // The preset list shows the preset that the rows are, if they are one.
        string text = ShipAiModel.Encode(_rows);
        int preset = _presetEntries.FindIndex(p => p.Text == text);
        if (preset >= 0) _presets.SelectedWidget = _presets.ListBox.Children[preset];
    }

    void Refresh()
    {
        double now = App.Clock.Time.Seconds;
        if (now >= _nextRefresh)
        {
            _nextRefresh = now + 0.25;
            var t = ShipTactics.Of(_ship);
            string sim = StateKey(t);
            if (_sent is not null && (sim == _sent || now > _sentAt + SendWaitSeconds)) _sent = null;
            if (_sent is null && sim != _shown) LoadFromSim();
            bool commandable = _ship.Commands.IsCommandableByLocalPlayer;
            if (_list.SelfInputActive != commandable) { _list.SelfInputActive = commandable; _autoToggle.SelfInputActive = commandable; _cargoBar.SelfInputActive = commandable; _designBar.SelfInputActive = commandable; }
            int others = SelectedOthers().Count();
            _toSelected.Text = Format("ToSelectedButtonFmt", others);
            _toSelected.SelfInputActive = others > 0;
            if (!_dirty) UpdateStatus(t);
        }
        if (_dirty)
        {
            _dirty = false;
            BuildRows();
            UpdateStatus(ShipTactics.Of(_ship));
        }
    }

    /// <summary>Spec 410: Auto is on; the target and the move action in effect now, with its row number.</summary>
    string ActiveStatus(ShipTactics t)
    {
        var parts = new List<string> { Text(Autonomy.Autonomous.IsAutonomous(_ship) ? "StatusAutonomous" : "StatusActive") };
        if (t.Target is { } id && _run.Game.Sim[id] is Ship target) parts.Add(Format("StatusTargetFmt", target.Metadata.XmlShipName));
        if (t.Recall) parts.Add(AText("ReturnOn"));
        else if (t.MoveRow >= 0 && t.MoveRow < t.Rows.Count) parts.Add(Format("StatusMoveFmt", t.MoveRow + 1, Text("Act_" + t.Rows[t.MoveRow].Act)));
        // Spec 2326: the flags that are set.
        var flags = Enumerable.Range(1, ShipAiModel.MaxFlags).Where(n => (t.Flags & (1 << (n - 1))) != 0).Select(n => Format("FlagFmt", n)).ToList();
        if (flags.Count > 0) parts.Add(Format("StatusFlagsFmt", string.Join(", ", flags)));
        if (parts.Count == 1 && t.Rows.Count > 0) parts.Add(Text("StatusIdle"));
        return string.Join(" ", parts);
    }

    void UpdateStatus(ShipTactics? t)
    {
        string status;
        if (_note is not null && App.Clock.Time.Seconds < _noteUntil) status = _note;
        else if (!_run.T.ShipAi.Enabled) status = Text("StatusDisabled");
        else if (t is not { Auto: true }) status = Text("StatusOff");
        else if (t.Waiting) status = Text("StatusWaiting");
        else status = ActiveStatus(t);
        _status.Text = status;
        // Row markers: the rows in effect now (the target row, the move row and the matched weapons rows) are bright.
        bool live = t is { Auto: true, Waiting: false } && t.Text == ShipAiModel.Encode(_rows);
        for (int i = 0; i < _markers.Count; i++)
        {
            bool on = live && t is not null && (i == t.TargetRow || i == t.MoveRow || i == t.FacingRow || i == t.TradeRow || i == t.BuildRow ||
                ((t.Matched & (1 << i)) != 0 && ShipAiModel.KindOf(_rows[i].Act) == ActKind.Weapons));
            _markers[i].Text = on ? $"<good>{i + 1}</good>" : $"<gray>{i + 1}</gray>";
        }
    }
}

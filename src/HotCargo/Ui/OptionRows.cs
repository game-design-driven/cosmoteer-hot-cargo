using Cosmoteer;
using Cosmoteer.Gui;
using Halfling.Graphics.Text;
using Halfling.Gui;
using Halfling.Gui.Components.Rects;

namespace HotCargo.Ui;

/// <summary>
/// What one option row shows. Toggle rows belong to a multiselect page (selected or not); other rows are action buttons.
/// The slider, when there is one, belongs to the option: it sits in the same row and works only when the option can use it.
/// </summary>
public sealed record OptionRowView(string Key, string Text, bool Active, string? Tooltip, bool Toggle, bool Selected, SliderSpec? Slider, int Value, bool SliderActive);

/// <summary>
/// A reusable list of option rows for dialogs (spec 591): each row is a button with its own slider on the right.
/// UI only: it raises <see cref="Clicked"/> and <see cref="ValueChanged"/>, and the dialog sends the inputs.
/// </summary>
public sealed class OptionRows
{
    sealed class Row
    {
        public required LayoutBox Box;
        public required ImageButton Button;
        public required SliderRow Slider;
        public OptionRowView? View;
        /// <summary>The icon shown now: null for the dialogue icon, else the check box state. Show runs every frame; the sprite changes only on a change.</summary>
        public (bool Selected, bool Active)? Check;
    }

    readonly List<Row> _rows = [];
    /// <summary>The smallest row: two lines of option text.</summary>
    const float RowHeight = 72f;
    /// <summary>The tallest row: a safety cap; a longer text still wraps, and the page scrolls.</summary>
    const float MaxRowHeight = 200f;

    public LayoutBox Box { get; }
    public event Action<OptionRowView>? Clicked;
    public event Action<OptionRowView, int>? ValueChanged;

    /// <summary>The button of each shown row, in order. The dialog hands them to the shared choice panel for hover markers.</summary>
    public IEnumerable<(ImageButton Button, OptionRowView View)> Shown =>
        _rows.Where(r => r.View is not null && r.Box.SelfActive).Select(r => (r.Button, r.View!));

    public OptionRows()
    {
        Box = new LayoutBox { PercentileWidth = 100f };
        Box.NineSlice.Flags = NineSliceFlags.None;
        Box.AutoSize.AutoHeightMode = AutoSizeMode.Enable;
        Box.Children.LayoutAlgorithm = LayoutAlgorithms.StretchTopToBottom;
        Box.Children.WidgetPadding = new Halfling.Geometry.Vector2(ChoiceButton.Gap);
        Box.Children.BorderPadding = new Borders(ChoiceButton.Indent, 0f, 0f, 0f);
    }

    /// <summary>Shows the views, reusing rows. Call each frame.</summary>
    public void Show(IReadOnlyList<OptionRowView> views)
    {
        while (_rows.Count < views.Count) _rows.Add(NewRow());
        for (int i = 0; i < _rows.Count; i++)
        {
            var r = _rows[i];
            if (i >= views.Count) { r.Box.SelfActive = false; r.View = null; continue; }
            var v = views[i];
            r.View = v;
            r.Box.SelfActive = true;
            (bool, bool)? check = v.Toggle ? (v.Selected, v.Active) : null;
            if (check != r.Check)
            {
                if (check is { } c) ChoiceButton.ShowCheck(r.Button, c.Item1, c.Item2);
                else ChoiceButton.ShowDialogueIcon(r.Button);
                r.Check = check;
            }
            r.Button.Text = v.Text;
            FitHeight(r);
            r.Button.ToolTips.Text = v.Tooltip;
            r.Button.SelfInputActive = v.Active;
            r.Button.PercentileWidth = v.Slider is null ? 100f : 62f;
            r.Slider.Row.SelfActive = v.Slider is not null;
            if (v.Slider is { } spec)
            {
                r.Slider.Enabled = v.SliderActive;
                r.Slider.Show(spec, v.Value);
            }
        }
    }

    /// <summary>
    /// The row grows with its wrapped option text, from RowHeight up to MaxRowHeight (playtest 2026-11-23: a friendly ship's help
    /// row spilled out of its box at a small window). Only once the button has a width: text wrapped at width 0 would ask for a huge
    /// height. Safe every frame: it changes the height only when it differs.
    /// </summary>
    static void FitHeight(Row r)
    {
        if (r.Button.ScreenRect.Width <= 0f || r.Box.Height <= 0f || r.Box.ScreenRect.Height <= 0f) return;
        float scale = r.Box.ScreenRect.Height / r.Box.Height;
        float text = ((Halfling.Gui.Components.AutoSizing.IDesiredSizeProvider)r.Button.StateNormalTextRenderer).DesiredScreenHeight / scale;
        float height = Math.Clamp(MathF.Ceiling(text), RowHeight, MaxRowHeight);
        if (MathF.Abs(height - r.Box.Height) >= 1f) r.Box.Height = height;
    }

    /// <summary>
    /// Layout check (autotests): the shown rows whose wrapped option text is taller than their button, so it spills out of its box.
    /// Logs each one. Rows without a width yet are skipped.
    /// </summary>
    public int Overflowing()
    {
        int n = 0;
        foreach (var r in _rows.Where(r => r.View is not null && r.Box.SelfActive && r.Button.ScreenRect.Width > 0f))
        {
            float text = ((Halfling.Gui.Components.AutoSizing.IDesiredSizeProvider)r.Button.StateNormalTextRenderer).DesiredScreenHeight;
            if (text <= r.Button.ScreenRect.Height + 0.5f) continue;
            n++;
            Log.Info($"ux: option row text {text:0} high in a {r.Button.ScreenRect.Height:0} high button ({r.Button.ScreenRect.Width:0} wide): {r.View!.Text}");
        }
        return n;
    }

    /// <summary>Clicks shown row <paramref name="index"/> (0-based) as a player would. False when there is no such row or it takes no input.</summary>
    public bool Click(int index)
    {
        var shown = Shown.ToList();
        if (index < 0 || index >= shown.Count || !shown[index].Button.IsInputActive) return false;
        Clicked?.Invoke(shown[index].View);
        return true;
    }

    /// <summary>Moves the slider of the row with this key as a player would. For autotests and screenshots.</summary>
    public void SetValue(string key, int value) => _rows.FirstOrDefault(r => r.View?.Key == key)?.Slider.Set(value);

    Row NewRow()
    {
        // The height follows the wrapped text (FitHeight), not the auto size: an auto height grew without limit when a row first
        // showed before the window had its width (the text wrapped at width 0).
        var box = new LayoutBox { PercentileWidth = 100f, Height = RowHeight };
        box.NineSlice.Flags = NineSliceFlags.None;
        box.Children.LayoutAlgorithm = LayoutAlgorithms.StretchLeftToRight;
        box.Children.WidgetPadding = new Halfling.Geometry.Vector2(10f);
        var button = ChoiceButton.New();
        // The row sets its height (FitHeight), so the button fills it instead of sizing to its text.
        button.AutoSize.AutoHeightMode = AutoSizeMode.Disable;
        var slider = new SliderRow(labelPercent: 0f) { };
        slider.Row.PercentileWidth = 38f;
        box.AddChild(button);
        box.AddChild(slider.Row);
        Box.AddChild(box);
        var row = new Row { Box = box, Button = button, Slider = slider };
        button.Clicked += (_, _) => { if (row.View is { Active: true } v) Clicked?.Invoke(v); };
        slider.Changed += value => { if (row.View is { } v) ValueChanged?.Invoke(v, value); };
        return row;
    }
}

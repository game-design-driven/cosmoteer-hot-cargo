using Cosmoteer.Gui;
using Halfling.Geometry;
using Halfling.Graphics.Text;
using Halfling.Gui;
using Halfling.Gui.Components.Rects;

namespace HotCargo.Ui;

/// <summary>
/// An option button in the game's comms dialogue style (vanilla CommDialogueTab): a FlatButton, left-aligned XML text at size 20,
/// at least 60 high, with the dialogue line icon on the left. Choice pages, option rows and the event window all use it, so the mod's
/// options look like the game's own dialogue buttons.
/// </summary>
static class ChoiceButton
{
    const int FontSize = 20;
    const float MinHeight = 60f;
    /// <summary>The space between dialogue buttons in a list (vanilla CommDialogueTab button box).</summary>
    public const float Gap = 20f;
    /// <summary>The left indent of a button list under the dialogue text (vanilla CommDialogueTab button box).</summary>
    public const float Indent = 50f;
    /// <summary>The size of the check box icon: the height of a vanilla check toggle.</summary>
    const float CheckSize = 32f;

    public static ImageButton New()
    {
        var b = new ImageButton();
        b.CopySettingsFrom(WidgetRules.Instance.FlatButton);
        b.AutoSize.AutoHeightMode = AutoSizeMode.Enable;
        b.AutoSize.MinHeight = MinHeight;
        foreach (var r in new[] { b.StateNormalTextRenderer, b.StateHighlightedTextRenderer, b.StatePressedTextRenderer, b.StateDisabledTextRenderer })
        {
            r.XmlFormatting = true;
            r.FontSize = FontSize;
            r.HAlignment = HAlignment.Left;
            r.Padding = new Borders(10f);
        }
        b.ImageRectController.InnerRectMode = InnerRectMode.Left;
        b.ImageRectController.BorderPadding = new Borders(10f);
        ShowDialogueIcon(b);
        return b;
    }

    /// <summary>The dialogue line icon: an action option.</summary>
    public static void ShowDialogueIcon(ImageButton b)
    {
        b.ImageSprite.CopySpriteFrom(Cosmoteer.GameApp.Rules.CareerMode.Gui.Comms.DefaultDialogueLineIcon);
        b.ImageRectController.InnerRectSize = b.ImageSprite.DesiredSize;
    }

    /// <summary>The game's check box (the ToggleCheckButton sprites) as the icon: a multiselect option, selected or not, greyed when it takes no input.</summary>
    public static void ShowCheck(ImageButton b, bool selected, bool active)
    {
        var check = WidgetRules.Instance.ToggleCheckButton;
        var slice = (selected, active) switch
        {
            (true, true) => check.SelectedNineSlice,
            (false, true) => check.DeselectedNineSlice,
            (true, false) => check.SelectedDisabledNineSlice,
            (false, false) => check.DeselectedDisabledNineSlice,
        };
        b.ImageSprite.CopySpriteFrom(slice.CenterSprite);
        // A nine-slice part has no size of its own, and a sprite with desired size 0 draws nothing: give it the check box size.
        b.ImageSprite.DesiredSize = new Vector2(CheckSize, CheckSize);
        b.ImageRectController.InnerRectSize = new Vector2(CheckSize, CheckSize);
    }
}

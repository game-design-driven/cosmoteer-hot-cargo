using Cosmoteer.Gui;
using Halfling.Gui;
using HotCargo.Core;

namespace HotCargo.Ui;

/// <summary>
/// A text button in the game's FlatButton style, with XML text in every state (also disabled). The one helper for the mod's
/// dialog buttons (tactics editor, ship card, cargo picker), so they match each other and the game.
/// </summary>
static class FlatButton
{
    public static Button New(string text, Action click, string? tip = null)
    {
        var b = new Button();
        b.CopySettingsFrom(WidgetRules.Instance.FlatButton);
        foreach (var r in new[] { b.StateNormalTextRenderer, b.StateHighlightedTextRenderer, b.StatePressedTextRenderer, b.StateDisabledTextRenderer })
            r.XmlFormatting = true;
        b.Text = text;
        if (tip is not null) b.ToolTips.TextProvider = new Ux.LiveText(() => tip);
        b.Clicked += (_, _) => Errors.Guard("button " + text, click);
        return b;
    }
}

using System.Text;
using Cosmoteer.Localization;
using HotCargo.Core;

namespace HotCargo.Ux;

/// <summary>
/// Spec 2581: a breakdown as tooltip XML: a title, one line for each step that changes the number, the total, then the hints.
/// Texts come from mod/ux/strings.en.rules (section HotCargoBreakdown).
/// </summary>
public static class BreakdownText
{
    /// <summary>higherIsGood: a step that raises the number shows green (a chance) or red (heat, a price).</summary>
    public static string Tooltip(string title, Breakdown b, bool higherIsGood)
    {
        var view = BreakdownTextModel.View(b, UxKit.Switches?.BreakdownFactors ?? FactorStyle.Percent, higherIsGood);
        var sb = new StringBuilder();
        sb.Append("<b>").Append(title).Append("</b>");
        foreach (var line in view.Lines)
            sb.Append('\n').Append(Label(line.Key, line.Arg)).Append(": ").Append(Toned(line.Value, line.Tone));
        sb.Append("\n<b>").Append(Format("TotalFmt", view.Total)).Append("</b>");
        foreach (var hint in view.Hints)
            sb.Append("\n<gray>").Append(Text(hint.Key)).Append(":</gray> ").Append(Toned(hint.Change, hint.Tone));
        return sb.ToString();
    }

    static string Label(string key, string? arg) => arg is null ? Text(key) : Format(key + "Fmt", arg);

    static string Toned(string value, Tone tone) => tone switch
    {
        Tone.Good => $"<good>{value}</good>",
        Tone.Bad => $"<bad>{value}</bad>",
        Tone.Gray => $"<gray>{value}</gray>",
        _ => value,
    };

    public static string Text(string key) => Strings.GetText("HotCargoBreakdown/" + key);

    public static string Format(string key, params object?[] args) => Strings.FormatText("HotCargoBreakdown/" + key, args);
}

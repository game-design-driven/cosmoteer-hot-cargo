using System.Globalization;
using System.Text;

namespace HotCargo.Autonomy;

/// <summary>Spec 1084: one design slot: a name and a whole ship design in the vanilla design format, as base64.</summary>
public sealed record BuildDesign(string Name, string Data);

/// <summary>Build ships (spec 1084 to 1089). Pure: no game types, so the unit tests cover it.</summary>
public static class BuildModel
{
    public const int MaxDesigns = 3;
    public const int MaxName = 32;

    /// <summary>A design name without the separators of the cargo text and without control characters, trimmed and cut to MaxName.</summary>
    public static string CleanName(string name)
    {
        var sb = new StringBuilder();
        foreach (char c in name) sb.Append(char.IsControl(c) || c is '|' or ',' or ':' ? ' ' : c);
        string clean = sb.ToString().Trim();
        return clean.Length > MaxName ? clean[..MaxName].TrimEnd() : clean;
    }

    /// <summary>Spec 1085: the value of the "build" part: slots joined by ',', each "name:base64" or empty. Trailing empty slots are left out.</summary>
    public static string Encode(IReadOnlyList<BuildDesign?> slots)
    {
        int last = slots.Count - 1;
        while (last >= 0 && slots[last] is null) last--;
        return string.Join(',', slots.Take(last + 1).Select(d => d is null ? "" : d.Name + ":" + d.Data));
    }

    /// <summary>Parses Encode text into MaxDesigns slots. Throws FormatException on too many slots, a bad name or data that is not base64.</summary>
    public static BuildDesign?[] Decode(string text)
    {
        var slots = new BuildDesign?[MaxDesigns];
        if (text.Length == 0) return slots;
        var parts = text.Split(',');
        if (parts.Length > MaxDesigns) throw new FormatException($"build: {parts.Length} designs; at most {MaxDesigns}");
        for (int i = 0; i < parts.Length; i++)
        {
            if (parts[i].Length == 0) continue;
            int colon = parts[i].IndexOf(':');
            if (colon < 0) throw new FormatException($"build design {i + 1} has no ':'");
            string name = parts[i][..colon], data = parts[i][(colon + 1)..];
            if (name.Length == 0 || name != CleanName(name)) throw new FormatException($"build design {i + 1}: bad name '{name}'");
            if (data.Length == 0 || data.Length % 4 != 0 || !data.All(IsBase64Char)) throw new FormatException($"build design {i + 1} ('{name}'): the data is not base64");
            slots[i] = new BuildDesign(name, data);
        }
        return slots;
    }

    static bool IsBase64Char(char c) => c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '+' or '/' or '=';

    /// <summary>Spec 1086: every resource type of the cost is available at least in that amount.</summary>
    public static bool Affords(IEnumerable<KeyValuePair<string, int>> cost, Func<string, int> available) =>
        cost.All(c => available(c.Key) >= c.Value);
}

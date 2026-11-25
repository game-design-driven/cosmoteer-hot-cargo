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
    /// <summary>Spec 2322: "Fewer than N ships of design X" packs the slot and N into one parameter: slot × CountBase + N.</summary>
    public const int CountBase = 100;
    public const int MaxCount = CountBase - 1;

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

    public static int Pack(int slot, int count) => slot * CountBase + count;

    public static (int Slot, int Count) Unpack(int param) => (param / CountBase, param % CountBase);

    /// <summary>Spec 2322: "&lt;slot&gt;x&lt;N&gt;".</summary>
    public static string CountText(int param) =>
        $"{(param / CountBase).ToString(CultureInfo.InvariantCulture)}x{(param % CountBase).ToString(CultureInfo.InvariantCulture)}";

    /// <summary>Parses CountText. Throws FormatException on any other text or a slot or count out of range.</summary>
    public static int ParseCount(string text)
    {
        int x = text.IndexOf('x');
        if (x <= 0 || !int.TryParse(text[..x], NumberStyles.None, CultureInfo.InvariantCulture, out int slot)
            || !int.TryParse(text[(x + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out int count))
            throw new FormatException($"'{text}' is not <slot>x<count>");
        if (slot is < 1 or > MaxDesigns) throw new FormatException($"design slot {slot} is outside 1 to {MaxDesigns}");
        if (count is < 1 or > MaxCount) throw new FormatException($"count {count} is outside 1 to {MaxCount}");
        return Pack(slot, count);
    }

    /// <summary>Spec 1086: every resource type of the cost is available at least in that amount.</summary>
    public static bool Affords(IEnumerable<KeyValuePair<string, int>> cost, Func<string, int> available) =>
        cost.All(c => available(c.Key) >= c.Value);
}

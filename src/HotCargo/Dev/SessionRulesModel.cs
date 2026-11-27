namespace HotCargo.Dev;

/// <summary>
/// Load-time rules of a co-op session script (the host's script: it has "mphost"). Pure: command names and arguments only.
/// </summary>
public static class SessionRulesModel
{
    /// <summary>
    /// #410: before its first step, a co-op session turns the player shield on ("mp shieldplayers on", TestShield: no part damage,
    /// no crew deaths) or states why not ("noshield &lt;reason&gt;"). godmode alone lets crew die, and a ship without crew is derelict:
    /// that lost the client's only ship in coop-core (CI 2026-11-17) and coop-combat (queue run 45999757528).
    /// A step that needs damage turns the shield off and on again itself. Null when the rule holds, else what is wrong.
    /// </summary>
    public static string? ShieldProblem(IEnumerable<(string Name, string Arg)> commands)
    {
        var list = commands.ToList();
        if (!list.Any(c => c.Name == "mphost")) return null;
        int firstStep = list.FindIndex(c => c.Name == "step");
        if (firstStep < 0) return null;
        foreach (var (name, arg) in list.Take(firstStep))
        {
            if (name == "mp" && arg == "shieldplayers on") return null;
            if (name == "noshield") return arg.Length > 0 ? null : "\"noshield\" needs a reason";
        }
        return "a co-op session turns the player shield on before its first step (\"mp shieldplayers on\"), or states why not (\"noshield <reason>\")";
    }
}

using Halfling.Serialization;

namespace HotCargo.Ai;

/// <summary>Spec 1922, 1923: the habits that each personality group (a faction ID, "pirates" or "hunters") reported, and their age.</summary>
[ReflectiveSerialization(Explicit = true)]
public sealed class AiReportsState
{
    [Serialize(Optional = true)] public List<GroupReport> Groups = [];
    /// <summary>Spec 1925: the groups whose prepared notice showed in this system.</summary>
    [Serialize(Optional = true)] public List<string> Noticed = [];

    public GroupReport? For(string group) => Groups.FirstOrDefault(g => g.Group == group);
}

[ReflectiveSerialization(Explicit = true)]
public sealed class GroupReport
{
    [Serialize] public string Group = "";
    [Serialize(Optional = true)] public bool Kite;
    [Serialize(Optional = true)] public bool Cloud;
    /// <summary>The FocusKind, or -1 for none.</summary>
    [Serialize(Optional = true)] public int Focus = -1;
    /// <summary>Jumps since the last report of the group.</summary>
    [Serialize(Optional = true)] public int Age;

    public Habits Habits => new(Kite, Cloud, Focus < 0 ? null : (FocusKind)Focus);
}

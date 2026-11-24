using Halfling.Serialization;

namespace HotCargo.Debris;

/// <summary>Debris decay (spec 1240 to 1246): old loose junk glows, then burns up. Salvage and ore drops (spec 1247 to 1249).</summary>
[ReflectiveSerialization(Explicit = true)]
public sealed class DebrisTunables
{
    /// <summary>Spec 1240: old loose junk glows and then burns up. Off: junk stays, as in vanilla.</summary>
    [Serialize(Optional = true)] public bool Enabled = true;
    /// <summary>Spec 1242: game seconds from the time a junk piece can decay until it burns up.</summary>
    [Serialize(Optional = true)] public float DecaySeconds = 240f;
    /// <summary>Spec 1243: the last game seconds before the burn-up, in which the piece glows.</summary>
    [Serialize(Optional = true)] public float GlowSeconds = 60f;
    /// <summary>Spec 1241: a junk piece with more parts than this is a wreck worth salvage, and it stays.</summary>
    [Serialize(Optional = true)] public int MaxParts = 20;
    /// <summary>Spec 1244: game seconds between two checks of the junk in the system.</summary>
    [Serialize(Optional = true)] public float CheckSeconds = 2f;
    /// <summary>Spec 1241: junk this far beyond the reach of a player's tractor beam also stays, so a pull that starts never loses its target.</summary>
    [Serialize(Optional = true)] public float TractorMargin = 100f;
    /// <summary>Spec 1247: the loose-resource drop chance of a part that breaks on a ship that the players did not build × this. Cheap resources only (spec 1832).</summary>
    [Serialize(Optional = true)] public float SalvageDropFactor = 0.6f;
    /// <summary>Spec 1832: the same for expensive components (processor, diamond, enriched uranium): a mild cut, so they drop only a bit less.</summary>
    [Serialize(Optional = true)] public float SalvageComponentDropFactor = 0.85f;
    /// <summary>Spec 1832: a resource whose base price (BuyPrice in its rules) is at least this is an expensive component. Vanilla: gold 500, enriched uranium 2,000.</summary>
    [Serialize(Optional = true)] public int SalvageComponentPrice = 1000;
    /// <summary>Spec 1834: a part that a salvage or mining laser breaks drops this share of its resources for sure (no roll), on ships that the players did not build. Not for the resources in LaserBaseResources.</summary>
    [Serialize(Optional = true)] public float LaserDropShare = 0.9f;
    /// <summary>Spec 1834: resources that keep the chance roll on a laser break, with LaserBaseDropFactor in place of SalvageDropFactor.</summary>
    [Serialize(Optional = true)] public string[] LaserBaseResources = ["steel", "coil"];
    /// <summary>Spec 1834: the drop chance of LaserBaseResources on a laser break × this (a weapon break: × SalvageDropFactor).</summary>
    [Serialize(Optional = true)] public float LaserBaseDropFactor = 0.75f;
    /// <summary>Spec 1248: the ore that an asteroid part drops when it breaks × this, for a break by any weapon or a collision.</summary>
    [Serialize(Optional = true)] public float MiningYieldFactor = 1.2f;
    /// <summary>Spec 1248: the same for a break by salvage damage (a mining laser). Parts hit by both get a mix by the salvage share.</summary>
    [Serialize(Optional = true)] public float LaserMiningYieldFactor = 1.3f;

    public void Validate(Action<bool, string> require)
    {
        require(DecaySeconds > 0, "Debris.DecaySeconds must be above 0");
        require(GlowSeconds >= 0 && GlowSeconds <= DecaySeconds, "Debris.GlowSeconds must be 0 to DecaySeconds");
        require(MaxParts >= 1, "Debris.MaxParts must be 1 or more");
        require(CheckSeconds > 0, "Debris.CheckSeconds must be above 0");
        require(TractorMargin >= 0, "Debris.TractorMargin must not be negative");
        require(SalvageDropFactor >= 0, "Debris.SalvageDropFactor must not be negative");
        require(SalvageComponentDropFactor >= 0, "Debris.SalvageComponentDropFactor must not be negative");
        require(SalvageComponentPrice >= 1, "Debris.SalvageComponentPrice must be 1 or more");
        require(LaserDropShare is >= 0f and <= 1f, "Debris.LaserDropShare must be 0 to 1");
        require(LaserBaseDropFactor >= 0, "Debris.LaserBaseDropFactor must not be negative");
        require(LaserBaseResources.All(r => !string.IsNullOrWhiteSpace(r)), "Debris.LaserBaseResources must not hold an empty name");
        require(MiningYieldFactor >= 0 && LaserMiningYieldFactor >= 0, "Debris.MiningYieldFactor and LaserMiningYieldFactor must not be negative");
    }
}

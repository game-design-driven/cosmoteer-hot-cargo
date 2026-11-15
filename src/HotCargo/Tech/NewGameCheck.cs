using Cosmoteer.Localization;
using Cosmoteer.Ships;
using HotCargo.Core;

namespace HotCargo.Tech;

/// <summary>
/// Spec 1010-1013, 1552: the start check of a new career. A ship whose vaults hold less than the starting tech may start; the tech that
/// finds no vault starts in normal storage and decays. The ship pick warns first (FitVaults), and the run start warns in the world (StartingVault).
/// </summary>
static class NewGameCheck
{
    /// <summary>Test only (autotest "newgamecheck off"): no Fit Tech Vaults offer, to build the preset designs (spec 1017).</summary>
    internal static bool Off;

    /// <summary>Tech units that a new run puts on one starter ship at most: all of StartingTechUnits (a single player gets all of it).</summary>
    internal static int RequiredUnits(bool coop) =>
        coop ? new TechTunables().StartingTechUnits : TunablesFile.Read()?.Values.Tech.StartingTechUnits ?? new TechTunables().StartingTechUnits;

    /// <summary>Tech units that one vault holds, from the part rules of the ship.</summary>
    internal static int PerVault(Ship ship) => StartingVault.Capacity(ship.Rules.PartsByID[Vaults.VaultId]);

    /// <summary>Tech units that the vaults of the design hold.</summary>
    internal static int VaultCapacity(Ship ship) => ship.BlueprintParts.Count(p => p.Rules.ID == Vaults.VaultId) * PerVault(ship);

    /// <summary>Spec 1552: the warning of the ship pick for a design whose vaults hold have units (too few), with the decay rule in one line.</summary>
    internal static string Warning(int have, bool coop)
    {
        string head = Strings.GetText(have == 0 ? "HotCargoTech/FitWarnNoVault" : "HotCargoTech/FitWarnShort");
        return head + "\n" + Strings.FormatText("HotCargoTech/DecayRuleFmt", LossTimer.FormatTime(DecaySeconds(coop)));
    }

    /// <summary>The decay time of a new run: the local tunables in single player. Co-op starts with the defaults (spec 1011).</summary>
    static float DecaySeconds(bool coop) =>
        coop ? new TechTunables().DecaySeconds : TunablesFile.Read()?.Values.Tech.DecaySeconds ?? new TechTunables().DecaySeconds;
}

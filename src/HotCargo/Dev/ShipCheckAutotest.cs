using Cosmoteer;
using Cosmoteer.Ships;
using HotCargo.Core;

namespace HotCargo.Dev;

/// <summary>
/// Autotest command shipcheck_all: runs the ship editor checks (<see cref="ShipCheck"/>) over every ship design that Hot Cargo makes or
/// changes: the built designs (scavenger, thieves of every tier, Ore Tender), the career starter presets in mod/tech/starters, and each
/// vanilla starter after Fit Tech Vaults and with the Prototype Core on its hull. It works on the main menu. One log line per ship; field ship_failures counts the ships with problems.
/// </summary>
static class ShipCheckAutotest
{
    static int _failures = -1;

    internal static void Register()
    {
        Autotest.Commands["shipcheck_all"] = _ =>
        {
            _failures = 0;
            foreach (var (name, load) in Designs())
            {
                string result;
                try
                {
                    using var ship = load();
                    var problems = ShipCheck.Problems(ship);
                    result = problems.Count == 0 ? "ok" : $"FAIL {string.Join("; ", problems)}";
                }
                catch (Exception ex) { result = $"FAIL {ex.Message}"; }
                if (result != "ok") _failures++;
                Log.Info($"AUTOTEST shipcheck {name}: {result}");
            }
        };
        Autotest.Fields["ship_failures"] = () => _failures;
    }

    static IEnumerable<(string Name, Func<Ship> Load)> Designs()
    {
        yield return ("Scavenger", () => Blueprints(Scavenge.ScavengerShip.DesignBytes(), "Scavenger"));
        yield return ("Ore Tender", () => Blueprints(Missions.Mechanics.MinerShip.Design(), "Ore Tender"));
        for (int tier = 1; tier <= Thieves.ThiefShip.TierCount; tier++)
        {
            int t = tier;
            yield return ($"Thief tier {t}", () => Blueprints(Thieves.ThiefShip.Bytes(t), $"Thief {t}"));
        }
        var starters = GameApp.Rules.CareerMode.StarterShips;
        int units = Tech.NewGameCheck.RequiredUnits(coop: false);
        for (int i = 0; i < starters.Length; i++)
        {
            var preset = starters[i].Ship;
            yield return ($"preset {preset.FilePath}", () => preset.LoadShipBlueprints());
            string vanilla = Tech.VaultDesigns.VanillaStarter(i);
            yield return ($"fitted {Path.GetFileName(vanilla)}", () => Tech.FitVaults.FitShip(vanilla, units, out _));
            yield return ($"prototype core on {Path.GetFileName(vanilla)}", () => WithPrototypeCore(vanilla));
        }
    }

    /// <summary>A vanilla design with the Prototype Core put on its hull like a carrier's (PrototypeObjective.Install). It must add no problem.</summary>
    static Ship WithPrototypeCore(string file)
    {
        var ship = Ship.Load(file, blueprintsOnly: true);
        try
        {
            var before = ShipCheck.Problems(ship);
            Missions.Mechanics.PrototypeObjective.PutCore(ship);
            ShipCheck.RequireNoNew(ship, before, "the prototype core");
            return ship;
        }
        catch { ship.Dispose(); throw; }
    }

    static Ship Blueprints(byte[] design, string name) => Ship.Load(design, name, blueprintsOnly: true);
}

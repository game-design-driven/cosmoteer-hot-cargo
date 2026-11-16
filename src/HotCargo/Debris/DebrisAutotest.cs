using Cosmoteer;
using System.Globalization;
using Cosmoteer.Data;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Parts;
using Halfling.Geometry;
using HotCargo.Core;
using HotCargo.Dev;

namespace HotCargo.Debris;

/// <summary>Test commands and fields of the Debris area. Commands that change the sim run through "mp" in co-op.</summary>
static class DebrisAutotest
{
    static Run R() => Autotest.RequireRun();

    const string TestRockName = "Test rock";
    static readonly ID<PartRules> IronDeposit = new("cosmoteer.deposit_iron_1x");
    static long _looseMark;
    static int _broken;

    static long LooseUnits() => R().Game.Sim.Nuggets.Where(n => n.ResourceType != Tech.Vaults.TechId).Sum(n => (long)n.Quantity);

    internal static void Register()
    {
        Autotest.Commands["debris_set"] = arg => Autotest.SetField(R().T.Debris, arg);
        // debris_spawn <count> [parts] [speed]: loose junk pieces near the acting player's first ship, or near the first human ship by ID
        // when a fight left that player without a ship (godmode cannot stop a one-hit kill). Each piece is a row of [parts] armor blocks
        // (default 3), with no crew and no resources. [speed] (default 0) sends them drifting and spinning, like pieces of a destroyed ship.
        // Fixed seeds and sim positions, so every co-op machine makes the same pieces.
        Autotest.Commands["debris_spawn"] = arg =>
        {
            var p = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            int count = int.Parse(p[0], CultureInfo.InvariantCulture);
            int parts = p.Length > 1 ? int.Parse(p[1], CultureInfo.InvariantCulture) : 3;
            float speed = p.Length > 2 ? float.Parse(p[2], CultureInfo.InvariantCulture) : 0f;
            var sim = R().Game.Sim;
            var home = Autotest.ActingShip(sim) ?? sim.Ships.HumanPlayerShips.OrderBy(s => s.UniqueID).FirstOrDefault()
                ?? throw new InvalidOperationException("no human ship");
            var armor = home.Rules.PartsByID[new ID<PartRules>("cosmoteer.armor")];
            for (int i = 0; i < count; i++)
            {
                var piece = new Ship(home.Rules, "Test debris", -3, randSeed: 7700UL + (ulong)i);
                using (var sui = ShipUpdateInfo.Alloc(piece))
                {
                    for (int x = 0; x < parts; x++)
                        if (!sui.TryAddBlueprintPart(armor, new IntVector2(x, 0), 0, flipX: false, mirrorPhysical: false,
                                allowOverwrite: false, addDoors: false, hireCrew: false, out var reasons, force: true))
                            throw new InvalidOperationException($"cannot add armor: {reasons}");
                    sui.Apply();
                }
                piece.MatchPhysicalToBlueprints();
                var near = home.DetWorldCenter + Vector2.FromAngle(i * 2.4f) * (home.DetWorldBoundingCircle.Radius + 300f + 40f * i);
                piece.WorldCenter = Autotest.ClearSpotNear(sim, near, 0f, 60f);
                sim.Ships.Add(piece);
                piece.Physics.PhysicsWrapper.LinearVelocity = Vector2.FromAngle(i * 1.7f) * speed;
                piece.Physics.PhysicsWrapper.AngularVelocity = speed > 0 ? 0.3f + 0.1f * (i % 5) : 0f;
            }
            Log.Info($"AUTOTEST debris_spawn: {count} pieces of {parts} parts, speed {speed}");
        };
        // debris_focus: test only. The camera follows the acting player's ship and zooms out to the 3 nearest tracked junk pieces,
        // like "tech focus" (for screenshots).
        Autotest.Commands["debris_focus"] = _ =>
        {
            var run = R();
            var sim = run.Game.Sim;
            var home = Autotest.ActingShip(sim) ?? throw new InvalidOperationException("no ship of the acting player");
            var near = run.State.Debris.Since.Keys.Select(id => sim.Ships[id]).OfType<Ship>()
                .Select(s => s.DetWorldCenter.DistanceTo(home.DetWorldCenter)).Order().Take(3).ToList();
            if (near.Count == 0) throw new InvalidOperationException("no tracked junk piece");
            home.FocusCamera(playSound: false);
            Autotest.FitCamera(sim, (near.Max() + 120f) * 2f);
        };
        // debris_rock <count>: a test asteroid of <count> iron deposits in a row (1 iron each) near the acting player's first ship.
        Autotest.Commands["debris_rock"] = arg =>
        {
            int count = int.Parse(arg.Trim(), CultureInfo.InvariantCulture);
            var sim = R().Game.Sim;
            var home = Autotest.ActingShip(sim) ?? throw new InvalidOperationException("no ship of the acting player");
            var rules = GameApp.Rules.Ships.First(r => r.IsAsteroid && r.PartsByID.ContainsKey(IronDeposit));
            var rock = new Ship(rules, TestRockName, -3, randSeed: 7800UL);
            using (var sui = ShipUpdateInfo.Alloc(rock))
            {
                for (int x = 0; x < count; x++)
                    if (!sui.TryAddBlueprintPart(rules.PartsByID[IronDeposit], new IntVector2(x, 0), 0, flipX: false, mirrorPhysical: false,
                            allowOverwrite: false, addDoors: false, hireCrew: false, out var reasons, force: true))
                        throw new InvalidOperationException($"cannot add an iron deposit: {reasons}");
                sui.Apply();
            }
            rock.MatchPhysicalToBlueprints();
            rock.WorldCenter = Autotest.ClearSpotNear(sim, home.DetWorldCenter, home.DetWorldBoundingCircle.Radius + 400f, 40f * count);
            sim.Ships.Add(rock);
            Log.Info($"AUTOTEST debris_rock: {count} iron deposits");
        };
        // debris_break <debris|rock> <salvage|weapon>: breaks every part of the test junk (debris_spawn) or test asteroid (debris_rock)
        // with salvage damage (a mining laser) or plain weapon damage. Marks the loose resources first, for debris_loose_gain.
        Autotest.Commands["debris_break"] = arg =>
        {
            var p = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            string name = p[0] switch { "debris" => "Test debris", "rock" => TestRockName, _ => throw new ArgumentException($"debris_break: debris or rock, not {p[0]}") };
            bool salvage = p[1] switch { "salvage" => true, "weapon" => false, _ => throw new ArgumentException($"debris_break: salvage or weapon, not {p[1]}") };
            var sim = R().Game.Sim;
            var parts = sim.Ships.Where(s => s.Metadata.PlainShipName == name).SelectMany(s => s.Parts).ToList();
            if (parts.Count == 0) throw new InvalidOperationException($"no part of '{name}' to break");
            _looseMark = LooseUnits();
            _broken = parts.Count;
            // Salvage damage marks the part salvaged (a laser kill). A weapon break sets the health to 0, like "kill": a plain hit
            // on fresh junk did not break it in the test.
            foreach (var part in parts)
                if (salvage) part.OnHit(part.Rules.MaxHealth * 10, DamageType.Salvage);
                else part.Health = 0;
            Log.Info($"AUTOTEST debris_break: {parts.Count} parts of {name} by {p[1]}");
        };
        // Loose resource units in the system (tech crates left out) since the last debris_break, and the parts that it broke.
        Autotest.Fields["debris_loose_gain"] = () => LooseUnits() - _looseMark;
        Autotest.Fields["debris_broken"] = () => _broken;
        // Parts of the test junk and test asteroids that are still there: 0 after a break that destroyed them all.
        Autotest.Fields["debris_parts_left"] = () => R().Game.Sim.Ships.Where(s => s.Metadata.PlainShipName is "Test debris" or TestRockName).Sum(s => s.Parts.Count);
        Autotest.Fields["junk"] = () => R().Game.Sim.Ships.Count(s => s.IsJunk && !s.Rules.IsAsteroid);
        Autotest.Fields["debris_tracked"] = () => R().State.Debris.Since.Count;
        Autotest.Fields["debris_removed"] = () => R().State.Debris.Removed;
        Autotest.Fields["debris_glowing"] = () =>
        {
            var run = R();
            var t = run.T.Debris;
            return run.State.Debris.Since.Values.Count(s => DebrisModel.Phase(run.State.GameSeconds - s, t.DecaySeconds, t.GlowSeconds) == DebrisPhase.Glowing);
        };
        // Junk pieces that may decay now (spec 1241), for checks that a protection holds.
        Autotest.Fields["debris_candidates"] = () =>
        {
            var run = R();
            var sim = run.Game.Sim;
            var reach = DebrisArea.TractorReach(sim, run.T.Debris.TractorMargin);
            return sim.Ships.Count(s => DebrisArea.CanDecay(run, s, run.T.Debris, reach));
        };
    }
}

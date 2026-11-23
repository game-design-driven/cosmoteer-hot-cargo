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
    /// <summary>Loose units of a few resources at the last debris_break, for the debris_gain_* fields (spec 1832).</summary>
    static readonly Dictionary<string, long> _looseMarkOf = [];

    static long LooseUnits() => R().Game.Sim.Nuggets.Where(n => n.ResourceType != Tech.Vaults.TechId).Sum(n => (long)n.Quantity);

    static long LooseUnits(string resource) => R().Game.Sim.Nuggets.Where(n => n.ResourceType == new ID<Cosmoteer.Resources.ResourceRules>(resource)).Sum(n => (long)n.Quantity);

    static readonly string[] GainResources = ["steel", "processor"];

    internal static void Register()
    {
        Autotest.Commands["debris_set"] = arg => Autotest.SetField(R().T.Debris, arg);
        // debris_collect_check: spec 1833. For each resource that can lie loose (a mod item, or a chance above 0 to drop from crew or a
        // part), one loose unit and one steel next to the selected player ship, then the vanilla collect tool's plan over both
        // (SalvageCollectState.GetNuggetsToCollect, which draws its overlay). It must not throw. A resource without a stack size must
        // never drop. Logs each checked resource.
        Autotest.Commands["debris_collect_check"] = _ =>
        {
            var game = R().Game;
            var sim = game.Sim;
            var ship = sim.Ships.LocalPlayerShips.FirstOrDefault() ?? throw new InvalidOperationException("no own ship");
            sim.PlayerInput.SelectSingleShip(ship);
            var steel = new ID<Cosmoteer.Resources.ResourceRules>("steel");
            var tool = new Cosmoteer.Game.SelectionStates.SalvageCollectState(game);
            int checkedCount = 0;
            foreach (var rr in Cosmoteer.GameApp.Rules.Resources.OrderBy(r => r.ID.ToString(), StringComparer.Ordinal))
            {
                bool drops = new[] { Cosmoteer.Modes.NuggetDropSource.Crew, Cosmoteer.Modes.NuggetDropSource.Part }
                    .Any(src => sim.Mode.GetNuggetDropChance(rr, src, 0f, isPlayer: false, null) > 0f || sim.Mode.GetNuggetDropChance(rr, src, 0f, isPlayer: true, null) > 0f);
                if (!SalvageDrops.IsCargo(rr))
                {
                    Autotest.Check(!drops, $"{rr.ID} has no stack size, so it must never drop loose");
                    continue;
                }
                bool modItem = rr.ID.ToString().StartsWith("yarden_zamir.", StringComparison.Ordinal);
                if (!(modItem || drops) || rr.MaxPerNugget <= 0) continue;
                var at = ship.DetWorldCenter + new Vector2(ship.WorldBoundingRadius + 80f, 0f);
                var rect = new RotRect(at, new Vector2(40f, 40f), default);
                int before = sim.Nuggets.Count;
                sim.Nuggets.DropNuggets(rr.ID, 1, rect, Vector2.Zero, randomVelocity: false);
                sim.Nuggets.DropNuggets(steel, 1, rect, Vector2.Zero, randomVelocity: false);
                var added = Enumerable.Range(before, sim.Nuggets.Count - before).Select(i => sim.Nuggets[i]).ToList();
                Autotest.Check(added.Count == 2, $"{rr.ID} and steel lie loose ({added.Count} crates)");
                var plan = new Dictionary<Cosmoteer.Resources.Nugget, Halfling.Pooling.TempList<(Ship Ship, int Amount, float Dist)>>();
                try { tool.GetNuggetsToCollect(plan, new Dictionary<ID<Cosmoteer.Resources.ResourceRules>, int>(), new RotRect(at, new Vector2(400f, 400f), default)); }
                finally
                {
                    foreach (var list in plan.Values) list.Dispose();
                    foreach (var n in added) sim.Nuggets.RemoveNugget(n);
                }
                checkedCount++;
                Log.Info($"AUTOTEST debris_collect_check {rr.ID}: planned {plan.Count} crates");
            }
            Log.Info($"AUTOTEST debris_collect_check: {checkedCount} resources");
        };
        // debris_loose <resource> <units>: loose units of a resource next to the first own ship, with no drop roll. Test only.
        Autotest.Commands["debris_loose"] = arg =>
        {
            var p = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var sim = R().Game.Sim;
            var ship = sim.Ships.LocalPlayerShips.FirstOrDefault() ?? throw new InvalidOperationException("no own ship");
            sim.Nuggets.DropNuggets(new ID<Cosmoteer.Resources.ResourceRules>(p[0]), int.Parse(p[1], CultureInfo.InvariantCulture),
                new RotRect(ship.DetWorldCenter + new Vector2(ship.WorldBoundingRadius + 80f, 0f), new Vector2(40f, 40f), default), Vector2.Zero, randomVelocity: false);
        };
        Autotest.Fields["debris_loose_battery"] = () => R().Game.Sim.Nuggets.Where(n => n.ResourceType == new ID<Cosmoteer.Resources.ResourceRules>("battery")).Sum(n => n.Quantity);
        // debris_split: logs the salvage cut of every resource in the loaded rules (spec 1832): its base price and its factor.
        Autotest.Commands["debris_split"] = _ =>
        {
            var t = R().T.Debris;
            foreach (var rr in Cosmoteer.GameApp.Rules.Resources.OrderBy(r => r.BuyPrice).ThenBy(r => r.ID.ToString(), StringComparer.Ordinal))
                Log.Info($"AUTOTEST debris_split {rr.ID} price={rr.BuyPrice} factor={DebrisModel.SalvageFactor(rr.BuyPrice, t.SalvageDropFactor, t.SalvageComponentDropFactor, t.SalvageComponentPrice)}");
        };
        // debris_spawn <count> [parts] [speed] [part id]: loose junk pieces near the acting player's first ship, or near the first human ship by ID
        // when a fight left that player without a ship (godmode cannot stop a one-hit kill). Each piece is a row of [parts] parts
        // (default 3) of [part id] (default cosmoteer.armor), with no crew and no resources. [speed] (default 0) sends them drifting and
        // spinning, like pieces of a destroyed ship.
        // Fixed seeds and sim positions, so every co-op machine makes the same pieces.
        Autotest.Commands["debris_spawn"] = arg =>
        {
            var p = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            int count = int.Parse(p[0], CultureInfo.InvariantCulture);
            int parts = p.Length > 1 ? int.Parse(p[1], CultureInfo.InvariantCulture) : 3;
            float speed = p.Length > 2 ? float.Parse(p[2], CultureInfo.InvariantCulture) : 0f;
            string partId = p.Length > 3 ? p[3] : "cosmoteer.armor";
            var sim = R().Game.Sim;
            var home = Autotest.ActingShip(sim) ?? sim.Ships.HumanPlayerShips.OrderBy(s => s.UniqueID).FirstOrDefault()
                ?? throw new InvalidOperationException("no human ship");
            var block = home.Rules.PartsByID[new ID<PartRules>(partId)];
            for (int i = 0; i < count; i++)
            {
                var piece = new Ship(home.Rules, "Test debris", -3, randSeed: 7700UL + (ulong)i);
                using (var sui = ShipUpdateInfo.Alloc(piece))
                {
                    for (int x = 0; x < parts; x++)
                        if (!sui.TryAddBlueprintPart(block, new IntVector2(x * block.Size.X, 0), 0, flipX: false, mirrorPhysical: false,
                                allowOverwrite: false, addDoors: false, hireCrew: false, out var reasons, force: true))
                            throw new InvalidOperationException($"cannot add {partId}: {reasons}");
                    sui.Apply();
                }
                piece.MatchPhysicalToBlueprints();
                var near = home.DetWorldCenter + Vector2.FromAngle(i * 2.4f) * (home.DetWorldBoundingCircle.Radius + 300f + 40f * i);
                piece.WorldCenter = Autotest.ClearSpotNear(sim, near, 0f, 60f);
                sim.Ships.Add(piece);
                piece.Physics.PhysicsWrapper.LinearVelocity = Vector2.FromAngle(i * 1.7f) * speed;
                piece.Physics.PhysicsWrapper.AngularVelocity = speed > 0 ? 0.3f + 0.1f * (i % 5) : 0f;
            }
            Log.Info($"AUTOTEST debris_spawn: {count} pieces of {parts} {partId}, speed {speed}");
        };
        // debris_cargo <units>: one junk piece (named like debris_spawn's) made of a storage bay that holds this much steel (spec 1814).
        Autotest.Commands["debris_cargo"] = arg =>
        {
            int units = int.Parse(arg, CultureInfo.InvariantCulture);
            var sim = R().Game.Sim;
            var home = sim.Ships.HumanPlayerShips.OrderBy(s => s.UniqueID).FirstOrDefault() ?? throw new InvalidOperationException("no human ship");
            var piece = new Ship(home.Rules, "Test debris", -3, randSeed: 7800UL);
            using (var sui = ShipUpdateInfo.Alloc(piece))
            {
                if (!sui.TryAddBlueprintPart(home.Rules.PartsByID[new ID<PartRules>("cosmoteer.storage_2x2")], new IntVector2(0, 0), 0, flipX: false,
                        mirrorPhysical: false, allowOverwrite: false, addDoors: false, hireCrew: false, out var reasons, force: true))
                    throw new InvalidOperationException($"cannot add a storage bay: {reasons}");
                sui.Apply();
            }
            piece.MatchPhysicalToBlueprints();
            piece.WorldCenter = Autotest.ClearSpotNear(sim, home.DetWorldCenter, home.DetWorldBoundingCircle.Radius + 300f, 60f);
            sim.Ships.Add(piece);
            int added = piece.Resources.AddResources(new ID<Cosmoteer.Resources.ResourceRules>("steel"), units, excludeConstructionSinks: true);
            Autotest.Check(added == units, $"{units} steel fit into the test storage bay (added {added})");
        };
        // debris_focus [piece]: test only. The camera follows the acting player's ship and zooms out to the 3 nearest tracked junk pieces,
        // like "tech focus" (for screenshots). "piece": a close-up of the nearest tracked piece, where its part outline (the glow) shows.
        Autotest.Commands["debris_focus"] = arg =>
        {
            var run = R();
            var sim = run.Game.Sim;
            var home = Autotest.ActingShip(sim) ?? throw new InvalidOperationException("no ship of the acting player");
            if (arg.Trim() == "piece")
            {
                var piece = run.State.Debris.Since.Keys.Select(id => sim.Ships[id]).OfType<Ship>()
                    .MinBy(s => s.DetWorldCenter.DistanceTo(home.DetWorldCenter)) ?? throw new InvalidOperationException("no tracked junk piece");
                // The same moves as the "camera" command, with an instant zoom: the screenshot scene is paused.
                var at = piece.DetWorldCenter;
                Autotest.Commands["camera"](string.Create(CultureInfo.InvariantCulture, $"{at.X} {at.Y} {piece.DetWorldBoundingCircle.Radius * 2f + 60f}"));
                return;
            }
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
            foreach (string r in GainResources) _looseMarkOf[r] = LooseUnits(r);
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
        // debris_gain_<resource>: the same for one resource. A cheap and an expensive one (spec 1832).
        foreach (string r in GainResources) Autotest.Fields["debris_gain_" + r] = () => LooseUnits(r) - _looseMarkOf[r];
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

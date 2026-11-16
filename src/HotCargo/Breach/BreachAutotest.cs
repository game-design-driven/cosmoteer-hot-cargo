using Cosmoteer.Game;
using Cosmoteer.Game.Multiplayer.MPInputs;
using Cosmoteer.Ships;
using Cosmoteer.Simulation;
using HotCargo.Core;
using HotCargo.Dev;

namespace HotCargo.Breach;

/// <summary>Test commands of the crew breach (spec 1617). The ships: "me" (the first player ship at br_mark) and "hulk" (the vanilla abandoned ship).</summary>
static class BreachAutotest
{
    static ObjectID? _me, _hulk;

    static SimRoot Sim() => Autotest.RequireRun().Game.Sim;
    static Ship Get(string name) => (name switch { "me" => _me, "hulk" => _hulk, _ => throw new InvalidOperationException($"unknown ship '{name}' (me or hulk)") }) is { } id
        ? Sim().Ships[id] ?? throw new InvalidOperationException($"the {name} ship is gone") : throw new InvalidOperationException("br_mark first");
    static int BreachParts(Ship ship) => ship.Parts.Count(p => p.Rules.ID == BreachArea.PartId);

    internal static void Register()
    {
        // br_mark: notes the first player ship and the vanilla abandoned ship (ev_abandoned first).
        Autotest.Commands["br_mark"] = _ =>
        {
            _me = Sim().Ships.HumanPlayerSignificantShips.OrderBy(s => s.UniqueID).First().UniqueID;
            _hulk = Sim().Ships.Where(s => s.HasLabel(Events.Abandoned.Label)).OrderBy(s => s.UniqueID).First().UniqueID;
        };
        // br_noairlocks me|hulk: destroys every airlock of the ship.
        Autotest.Commands["br_noairlocks"] = arg =>
        {
            var ship = Get(arg.Trim());
            foreach (var part in ship.Parts.Where(p => p.Components.Any(c => c is Cosmoteer.Ships.Parts.Crew.Airlock)).ToList()) part.Health = 0;
        };
        // br_send <n> <from> <to>: n crew of the from ship get the to ship as their home ship, as the crew menu does (MPSetCrewHomeShipInput).
        Autotest.Commands["br_send"] = arg =>
        {
            var p = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (p.Length != 3) throw new InvalidOperationException("br_send needs: n from to");
            var from = Get(p[1]);
            var crew = from.Crew.Assigned.OrderBy(c => c.UniqueID).Take(int.Parse(p[0])).ToList();
            Autotest.RequireRun().Game.NetManager.EnqueueInput(new MPSetCrewHomeShipInput(crew, Get(p[2])));
            Log.Info($"AUTOTEST breach: {crew.Count} crew of {from.Metadata.PlainShipName} go to {Get(p[2]).Metadata.PlainShipName}");
        };
        // br_camera <ship> <size>: the camera on the ship, this many world units across (screenshots).
        Autotest.Commands["br_camera"] = arg =>
        {
            var p = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var at = Get(p[0]).WorldCenter;
            Autotest.Commands["camera"](string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{at.X} {at.Y} {p[1]}"));
        };
        Autotest.Fields["br_airlocks_me"] = () => BreachArea.HasWorkingAirlock(Get("me")) ? 1 : 0;
        Autotest.Fields["br_airlocks_hulk"] = () => BreachArea.HasWorkingAirlock(Get("hulk")) ? 1 : 0;
        Autotest.Fields["br_aboard_me"] = () => Get("me").Crew.OnShip.Count;
        Autotest.Fields["br_aboard_hulk"] = () => Get("hulk").Crew.OnShip.Count;
        Autotest.Fields["br_out_me"] = () => Sim().Crew.InSpace.Count(c => c.Soul.HomeShip == Get("me"));
        Autotest.Fields["br_out_hulk"] = () => Sim().Crew.InSpace.Count(c => c.Soul.HomeShip == Get("hulk"));
        Autotest.Fields["br_parts_me"] = () => BreachParts(Get("me"));
        Autotest.Fields["br_parts_hulk"] = () => BreachParts(Get("hulk"));
        Autotest.Fields["br_cutting"] = () => Autotest.RequireRun().State.Breach.Cuts.Count;
        Autotest.Fields["br_made"] = () => Autotest.RequireRun().State.Breach.Made;
        Autotest.Fields["br_hulk_junk"] = () => Get("hulk").IsJunk ? 1 : 0;
    }
}

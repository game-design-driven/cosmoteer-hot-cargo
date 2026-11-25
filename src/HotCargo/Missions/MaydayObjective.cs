using Cosmoteer;
using Cosmoteer.Data;
using Cosmoteer.GalaxyMap;
using Cosmoteer.Game;
using Cosmoteer.Game.Multiplayer;
using Cosmoteer.Localization;
using Cosmoteer.Modes.Career.Missions;
using Cosmoteer.Modes.Career.Missions.Objectives;
using Cosmoteer.Ships;
using Cosmoteer.Ships.AI;
using Cosmoteer.Simulation;
using Cosmoteer.Simulation.Stasis;
using Halfling;
using Halfling.Geometry;
using Halfling.Serialization;
using Halfling.Serialization.Generic;
using HotCargo.Core;

namespace HotCargo.Missions;

/// <summary>
/// Spec 2431 to 2433: the live distress fight. It completes when every attacker (one allegiance) is dead and the victim lives, and fails
/// when the victim dies or the players leave its system. The victim runs to the nearest player ship. A red ring follows it.
/// Made in code by <see cref="Mayday.Start"/>, never by rules.
/// </summary>
[ReflectiveSerialization(Explicit = true)]
[SerialDerivedType(TypeName = "HotCargoMayday")]
internal sealed class MaydayObjective : Objective, IMissionMarkers
{
    [Serialize] public ObjectiveTargetShip Victim { get; private set; }
    /// <summary>The attackers' allegiance: every ship of it, live or in stasis, is an attacker of this mayday.</summary>
    [Serialize] public int Attackers { get; private set; }
    [Serialize] public int Tier { get; private set; }
    /// <summary>The red ring that follows the victim (spec 2433).</summary>
    [Serialize(Optional = true)] public ObjectID? RingId { get; private set; }

    IEnumerable<ObjectID> IMissionMarkers.MarkerIds => RingId is { } id ? [id] : [];

    public override string ScriptTypeName => "HotCargoMayday";

    public MaydayObjective(ObjectiveTargetShip victim, int attackers, int tier)
    {
        Victim = victim ?? throw new ArgumentNullException(nameof(victim));
        Attackers = attackers;
        Tier = tier;
    }

    [GenericConstructor]
    MaydayObjective(GenericSerialReader reader)
    {
        Victim = null!;
        reader.ReflectiveRead(this);
    }

    public override void GetIntegrityHash(IntegrityHash iHash)
    {
        base.GetIntegrityHash(iHash);
        Victim.GetIntegrityHash(iHash);
        Attackers.GetIntegrityHash(iHash);
    }

    public override Range<int>? GetTierRange(SimRoot curSim) => Tier;

    public override MapNode? GetFirstMapNode(GameRoot game) => Victim.MapNode;

    public override Circle? GetAreaLocation(GameRoot game) =>
        Completion == CompletionState.InProgress && game.Sim?.MapNode?.UniqueID == Victim.MapNodeID && Victim.DetLocation() is { } at
            ? new Circle(at, MissionArea.TunablesFor(game).EscortMarkerRadius) : null;

    public override bool IsShipAssociated(MetaShip ship) => Completion == CompletionState.InProgress && Victim.TargetMetaShip == ship;

    public override bool CheckExpired(GameRoot game) => Completion == CompletionState.Failed || !Victim.IsAlive;

    public override void GetStringArgs(Dictionary<string, object?> args, GameRoot game)
    {
        base.GetStringArgs(args, game);
        args["target_ship"] = Victim.GetXmlTargetName(game.Sim);
    }

    /// <summary>Spec 2292: the vanilla target focus, else the victim's place.</summary>
    public override bool CanFocusDisplayLine(GameRoot game) => MissionFocus.CanFocus(game, Victim);

    public override void OnDisplayLineFocused(GameRoot game) => MissionFocus.Focus(game, Victim);

    /// <summary>Attackers of this mayday in this system, live or in stasis.</summary>
    internal int AttackersLeft(SimRoot sim)
    {
        int live = 0;
        for (int i = 0; i < sim.Ships.Count; i++)
            if (sim.Ships[i] is { IsJunk: false } s && s.Metadata.PlayerIndex == Attackers) live++;
        return live + sim.Stasis.OfType<StasisShipSpawner>().Count(s => s.ObjectAllegiance == Attackers);
    }

    /// <summary>The victim's hull share in percent (its parts' health), or null when it is not live.</summary>
    internal int? HullPercent() => Victim.TargetShip is { } ship ? (int)Math.Round(100f * Hunters.HunterOrders.HealthFraction(ship)) : null;

    (int Left, int? Hull) _shown = (-1, null);

    public override void RefreshDisplayLines(GameRoot game)
    {
        if (game.Sim is not { } sim || sim.NodeId() != Victim.MapNodeID) return;
        var now = (AttackersLeft(sim), HullPercent());
        if (now == _shown) return;
        _shown = now;
        OnDisplayLinesChanged();
    }

    public override void GetDisplayLinesImpl(List<DisplayLine> lines, GameRoot game, CompletionState parentState)
    {
        string name = Victim.GetXmlTargetName(game.Sim);
        var text = Completion != CompletionState.InProgress || _shown.Left < 0
            ? Strings.KeyStringFmt("HotCargoObjectives/MaydayFmt", name)
            : Strings.KeyStringFmt("HotCargoObjectives/MaydayFightFmt", name, _shown.Left, _shown.Hull?.ToString() ?? "?");
        lines.Add(new DisplayLine { Text = text, State = Completion, CanFocus = CanFocusDisplayLine, OnFocused = OnDisplayLineFocused });
    }

    public override void RegisterEvents(GameRoot game, bool isLoading)
    {
        Victim.Killed += OnVictimKilled;
        game.EventHub.Tick += OnTick;
        if (!isLoading && !Victim.IsAlive) OnVictimKilled(null, EventArgs.Empty);
    }

    public override void UnregisterEvents(GameRoot game)
    {
        Victim.Killed -= OnVictimKilled;
        game.EventHub.Tick -= OnTick;
    }

    void OnVictimKilled(object? sender, EventArgs e) => End(fail: true, "the victim died");

    void End(bool fail, string why)
    {
        if (Completion != CompletionState.InProgress) return;
        Log.Info($"mayday {(fail ? "failed" : "won")}: {why}");
        if (Mission?.Game.Sim is { } sim && sim.NodeId() == Victim.MapNodeID) RingId = EscortGuard.WarningRing(sim, RingId, null);
        if (fail) Fail();
        else Complete();
    }

    /// <summary>
    /// Spec 2433: the red ring on the victim, moved to it each tick. Mayday.Start shows it at once too, so the fight starts with its ring
    /// (#336: a check right after the start found no ring on the PC, where the script ran before the next sim tick).
    /// </summary>
    internal void ShowRing(SimRoot sim) => RingId = EscortGuard.WarningRing(sim, RingId, Victim.DetLocation());

    /// <summary>A moving player ship changes the victim's goal a little each tick. Its AI origin follows only past this distance.</summary>
    const float FollowSlack = 300f;

    void OnTick(object? sender, SimRoot sim)
    {
        if (Mission is null || Completion != CompletionState.InProgress || sim != Mission.Game.Sim) return;
        // Spec 2432: the players left the system: the fight goes on without them, and the mission fails.
        if (sim.NodeId() != Victim.MapNodeID)
        {
            End(fail: true, "the players left the system");
            return;
        }
        if (!Victim.IsAlive)
        {
            OnVictimKilled(null, EventArgs.Empty);
            return;
        }
        ShowRing(sim);
        if (AttackersLeft(sim) == 0)
        {
            Ux.UxKit.ChatLine(sim.RequireGame(), Strings.FormatText("HotCargoMissions/MaydayThanksFmt", Victim.GetXmlTargetName(sim)));
            End(fail: false, "every attacker is dead");
            return;
        }
        // Spec 2431: the victim runs to the nearest player ship.
        if (Victim.TargetShip is { AI: { } ai } ship
            && sim.Ships.HumanPlayerSignificantShips.OrderBy(h => h.DetWorldCenter.DistanceSquaredTo(ship.DetWorldCenter)).ThenBy(h => h.UniqueID).FirstOrDefault() is { } lead
            && (ai.Parameters?.PatrolOrigin is not { } origin || origin.DistanceTo(lead.DetWorldCenter) > FollowSlack))
        {
            AIParameters p = ai.Parameters?.Clone() ?? new AIParameters();
            p.PatrolOrigin = lead.DetWorldCenter;
            ai.Parameters = p;
        }
    }

    public override int GetTypeIntegrityHash() => 0x4843_4D59; // "HCMY"
}

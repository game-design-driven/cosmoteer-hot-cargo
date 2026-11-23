using System.Runtime.CompilerServices;
using Cosmoteer;
using Cosmoteer.Data;
using Cosmoteer.Resources;
using Cosmoteer.Ships;
using Cosmoteer.Simulation;
using Halfling.Geometry;
using Halfling.Graphics;
using Halfling.Scene2D;
using HotCargo.Core;

namespace HotCargo.Events;

/// <summary>
/// Spec 1874: while a hand-over step reads "Recover", its source ship and each loose unit of its item get a vanilla object indicator
/// (the blip of the zoomed-out map and the off-screen arrow) with the item's icon and name. Vanilla shows no indicator on a junk ship
/// or on a crate. GUI only: the indicators never change the sim.
/// </summary>
internal static class RecoverMarkers
{
    /// <summary>Nodes that have an indicator or have one queued, so the tick adds each one once.</summary>
    static readonly ConditionalWeakTable<SceneNode, ItemIndicator> Marked = new();

    /// <summary>Sim tick (<see cref="MissionCargo"/>), for a recovering hand-over: marks the live source while it holds the item, and every loose unit.</summary>
    internal static void Mark(SimRoot sim, HandOverObjective o)
    {
        var item = o.ResourceType ?? throw new InvalidOperationException("a recovering hand-over has no item");
        if (o.LiveSource(sim) is { } ship && HoldsItem(ship, item)) Add(sim, ship, item);
        foreach (var loose in o.LooseUnits(sim)) Add(sim, loose, item);
    }

    internal static bool HoldsItem(Ship ship, ID<ResourceRules> item) =>
        HandOverObjective.Forms(item).Any(f => ship.Resources.GetResourceTotal(f, includeStored: true, includeCarried: true, includeOffShipCarried: false) > 0);

    static void Add(SimRoot sim, SceneNode node, ID<ResourceRules> item)
    {
        if (Marked.TryGetValue(node, out _)) return;
        var indicator = new ItemIndicator(item);
        Marked.Add(node, indicator);
        // Vanilla adds indicator components through the non-deterministic queue too (TechMarkers.CrateAdded, CrewSoul distance warnings).
        sim.EnqueueNonDeterministic(() => node.Components.Add(indicator));
    }

    /// <summary>True while a recovering hand-over in the current run still points at this node: its source holding the item, or a loose unit.</summary>
    static bool StillWanted(SimRoot? sim, SceneNode? node, ID<ResourceRules> item)
    {
        if (Run.Current is not { } run || sim is null || node is null || sim != run.Game.Sim) return false;
        foreach (var o in HandOverObjective.Open(run))
        {
            if (!o.Recovering || o.ResourceType != item) continue;
            if (node is Ship ship && o.LiveSource(sim) == ship && HoldsItem(ship, item)) return true;
            if (node is Nugget) return true;
        }
        return false;
    }

    internal sealed class ItemIndicator(ID<ResourceRules> item) : ObjectIndicatorSource
    {
        static readonly Color Gold = new(1f, 0.78f, 0.2f);

        public override Color Color => Gold;
        public override int Sort => 1;
        public override bool AllowInteractions => false;
        public override Vector2 WorldLocation => Node is Ship ship ? ship.WorldCenter : base.WorldLocation;
        public override float WorldRadius => Node is Ship ship ? ship.WorldBoundingRadius : 0f;

        public override bool? ShouldShowA(out bool allowOffScreen, out bool isPingable)
        {
            allowOffScreen = true;
            isPingable = false;
            // Every marker runs this in every frame, so it has no closure (spec 872). A failure logs once, like Errors.Guard.
            try { return StillWanted(Sim, Node, item); }
            catch (Exception ex) { Errors.Report("recover marker", ex.Message, ex); return false; }
        }

        public override void GetIcons(out ISprite normal, out ISprite highlighted, out ISprite pressed)
        {
            ISprite icon = item.GetResourceRules().Icon ?? throw new InvalidOperationException($"{item} has no icon");
            normal = highlighted = pressed = icon;
        }

        public override string? GetNonMouseoverText() => item.GetResourceRules().NameKey.Text;
    }
}

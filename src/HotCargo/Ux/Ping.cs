using Cosmoteer;
using Cosmoteer.Game;
using Cosmoteer.Game.Gui;
using Cosmoteer.Game.Multiplayer;
using Cosmoteer.Input;
using Cosmoteer.Modes.Career;
using Cosmoteer.Simulation;
using Cosmoteer.Simulation.Cameras;
using Halfling;
using Halfling.Events;
using Halfling.Geometry;
using Halfling.Graphics;
using Halfling.Gui;
using Halfling.Gui.Components.Input;
using Halfling.Input;
using Halfling.Network;
using Halfling.Serialization;
using HarmonyLib;
using HotCargo.Core;
using Num = System.Numerics.Vector2;

namespace HotCargo.Ux;

/// <summary>
/// Co-op ping (spec 1500 to 1509). Alt + left click in space or on the minimap marks a point, or the ship, station or wreck under the
/// mouse, for every player: a pulsing marker with the player's name and a short sound. The click goes through the HotCargoPing MP
/// input so that every machine gets it, but the ping is UI state: it never changes sim state, the sim hash or a save.
/// The marker is a vanilla indicator (like the crew distance warning) on the sim root, so it also shows at the screen edge and on
/// the minimap.
/// </summary>
public static class Ping
{
    static readonly PingBook<MessengerID, ObjectID> Book = new();
    static readonly Dictionary<MessengerID, PingIndicator> Shown = [];
    /// <summary>Pings whose vanilla ping flash and sound are still due (the indicator widget comes a frame after the indicator).</summary>
    static readonly HashSet<MessengerID> Flash = [];
    static SimRoot? _sim;

    /// <summary>Pings received since start, on this machine. For autotests.</summary>
    internal static int Received;

    static bool AltClick => App.Keyboard.HotkeyDown(ViKey.Alt);

    /// <summary>Sends a ping at a world point, on the ship under it when there is one. The vanilla click does not run.</summary>
    static void Send(GameRoot game, Vector2 at, Cosmoteer.Ships.Ship? on)
    {
        if (Run.Current is not { } run || run.T.Ux.PingSeconds <= 0) return;
        game.NetManager.EnqueueInput(new PingInput(at, on?.UniqueID));
    }

    /// <summary>Alt + left click in space. Vanilla calls this only in normal ship control, so build mode and other tools keep Alt.</summary>
    [HarmonyPatch(typeof(PlayerInputHandler), nameof(PlayerInputHandler.InputSelectShipsAndParts))]
    static class SpaceClick
    {
        static bool Prefix(PlayerInputHandler __instance)
        {
            if (!Inputs.Game.Selection.WasPressed || !AltClick || Run.Current is not { } run || run.T.Ux.PingSeconds <= 0) return true;
            var sim = __instance.Sim;
            bool sent = false;
            Errors.Guard("ping click", () =>
            {
                Send(sim.Game, sim.WorldMouseLoc, __instance.GetShipUnderCursor());
                sent = true;
            });
            return !sent;
        }
    }

    /// <summary>Alt + left click on the minimap: a ping at that point instead of the vanilla camera jump.</summary>
    [HarmonyPatch(typeof(Minimap), nameof(Minimap.OnClickCatcherStateChanged))]
    static class MinimapClick
    {
        static bool Prefix(Minimap __instance, ValueChangedEventArgs<InputState> e)
        {
            if (e.NewValue != InputState.Pressed || !AltClick || !(bool)Inputs.General.UIClick || Run.Current is not { } run || run.T.Ux.PingSeconds <= 0) return true;
            bool sent = false;
            Errors.Guard("ping minimap", () =>
            {
                var m = __instance;
                Send(m.Game, Minimap.GetWorldPoint(m.Root.UIMouseLocation, m._minimapWorldRect, m.Children.RectProvider.ScreenRect), null);
                sent = true;
            });
            return !sent;
        }
    }

    /// <summary>Runs in the MP input on every machine: records the ping. UI state only.</summary>
    internal static void Receive(GameRoot game, MessengerID player, Vector2 at, ObjectID? target)
    {
        if (game.Sim is not { } sim) return;
        if (sim != _sim) Reset(sim);
        Book.Place(player, new Num(at.X, at.Y), target, App.Clock.Time.Seconds);
        Flash.Add(player);
        Received++;
        Log.Info($"ping: player {player} at {at}{(target is { } t ? $" on {t}" : "")}");
    }

    static void Reset(SimRoot sim)
    {
        Book.Clear();
        Flash.Clear();
        foreach (var indicator in Shown.Values) Remove(indicator);
        Shown.Clear();
        _sim = sim;
    }

    static void Remove(PingIndicator indicator)
    {
        if (indicator.Sim is { } sim) sim.EnqueueNonDeterministic(() => sim.Components.Remove(indicator));
    }

    static Cosmoteer.Ships.Ship? Live(SimRoot sim, ObjectID id) => sim.Ships[id] is { } s && s.ExistsInSim(sim) ? s : null;

    /// <summary>Each GUI frame: ends old pings, and keeps one indicator for each live ping.</summary>
    internal static void OnFrame(Run run)
    {
        if (run.Game.Sim is not { } sim) return;
        // A jump or a load replaces the sim; its pings end with it.
        if (sim != _sim) Reset(sim);
        foreach (var ended in Book.Prune(App.Clock.Time.Seconds, run.T.Ux.PingSeconds, id => Live(sim, id) is not null))
            if (Shown.Remove(ended, out var indicator)) Remove(indicator);
        foreach (var mark in Book.Marks)
        {
            if (Shown.ContainsKey(mark.Player)) continue;
            var indicator = new PingIndicator(run, mark.Player);
            Shown[mark.Player] = indicator;
            sim.EnqueueNonDeterministic(() => sim.Components.Add(indicator));
        }
        foreach (var player in Flash.ToList())
        {
            if (!Shown.TryGetValue(player, out var indicator)) { Flash.Remove(player); continue; }
            if (run.Game.Gui.GetIndicatorFor(indicator) is not { } widget) continue;
            widget.ShowPing(sound: false, autoSlow: false);
            App.Audio.Play(run.Game.Gui.Rules.ObjectIndicators.PingSound, AudioGroups.Interface);
            Flash.Remove(player);
        }
    }

    /// <summary>Live pings on this machine. For autotests.</summary>
    internal static int Count => Book.Marks.Count;

    /// <summary>Live pings that show on the center of a human ship (they follow it). For autotests.</summary>
    internal static int OnHumanShips(SimRoot sim) =>
        Book.Marks.Count(m => WhereOf(sim, m.Player) is { } at && sim.Ships.HumanPlayerShips.Any(s => s.WorldCenter.DistanceTo(at) < 1f));

    /// <summary>The world point where a player's ping shows now, or null. For autotests.</summary>
    internal static Vector2? WhereOf(SimRoot sim, MessengerID player)
    {
        if (!Book.TryGet(player, out var mark)) return null;
        var at = PingBook<MessengerID, ObjectID>.Where(mark, id => Live(sim, id) is { } s ? new Num(s.WorldCenter.X, s.WorldCenter.Y) : null);
        return new Vector2(at.X, at.Y);
    }

    /// <summary>Sends a ping from this machine like a click: on a ship (it follows the ship), or at a point in space. For autotests.</summary>
    internal static void SendForTest(GameRoot game, Cosmoteer.Ships.Ship? on, Vector2 at) => Send(game, on?.WorldCenter ?? at, on);

    /// <summary>The marker of one player's ping. It reads the book in every frame, so a ping on a ship follows the ship.</summary>
    internal sealed class PingIndicator(Run run, MessengerID player) : ObjectIndicatorSource
    {
        public override Vector2 WorldLocation => Sim is { } sim && WhereOf(sim, player) is { } at ? at : default;
        public override Color Color => SharedChoice.SharedChoicePanel.ColorOf(SharedChoice.SharedChoices.Electorate(run), player);
        public override bool ShowPulse => true;
        public override bool ShowOnMinimap => true;
        // The base class throws when a minimap indicator has no sprite. A ping does not zoom the minimap out to keep itself in view.
        public override ISprite MinimapSprite => Icon;
        public override float? MinimapRadius => null;
        public override int Sort => 2;

        ISprite Icon => run.Mode.Rules.CustomMarkerIcon ?? throw new InvalidOperationException("career rules have no custom marker icon");

        public override bool? ShouldShowA(out bool allowOffScreen, out bool isPingable)
        {
            allowOffScreen = true;
            isPingable = false;
            return Book.TryGet(player, out _);
        }

        public override void GetIcons(out ISprite normal, out ISprite highlighted, out ISprite pressed)
        {
            normal = highlighted = pressed = Icon;
        }

        public override string? GetNonMouseoverText() =>
            run.IsMultiplayer ? SharedChoice.SharedChoices.PlayerName(run, player) : UxKit.Text("PingYou");

        /// <summary>A click on the marker or its edge arrow moves the camera there.</summary>
        public override void OnClicked() =>
            Sim?.Camera.ViewController.FocusOn(new FixedPointFocusTarget(WorldLocation), append: false, playSound: true, cancelOnArrival: true);
    }
}

/// <summary>A player's ping (spec 1500). UI state: Execute only records it on each machine and changes no sim state.</summary>
[ReflectiveSerialization(Explicit = true)]
[SerialDerivedType(TypeName = "HotCargoPing")]
public sealed class PingInput : MPInput
{
    [Serialize] public Vector2 At { get; private set; }
    [Serialize] public bool OnObject { get; private set; }
    [Serialize] public ObjectID Target { get; private set; }

    public PingInput(Vector2 at, ObjectID? target)
    {
        At = at;
        OnObject = target is not null;
        Target = target ?? default;
    }

    public override void Execute(GameRoot game, int playerIndex, MessengerID playerID, bool force = false) =>
        Errors.Guard("ping input", () => Ping.Receive(game, playerID, At, OnObject ? Target : null));
}

[HarmonyPatch(typeof(CareerGameModeManager), nameof(CareerGameModeManager.AllowMPInput))]
static class AllowPingPatch
{
    static void Postfix(MPInput mpInput, ref bool __result)
    {
        if (mpInput is PingInput) __result = true;
    }
}

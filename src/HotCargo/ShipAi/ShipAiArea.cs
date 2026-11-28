using System.Globalization;
using System.Runtime.CompilerServices;
using Cosmoteer.Game;
using Cosmoteer.Game.Gui;
using Cosmoteer.Game.Multiplayer.MPInputs;
using Cosmoteer.Gui;
using Cosmoteer.Localization;
using Cosmoteer.Modes.Career;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Commands;
using Cosmoteer.Simulation;
using Halfling.Geometry;
using Halfling.Graphics;
using Halfling.Graphics.Text;
using Halfling.Gui;
using Halfling.Gui.Components.Rects;
using HarmonyLib;
using HotCargo.Core;
using HotCargo.Dev;
using HotCargo.Hunters;

namespace HotCargo.ShipAi;

/// <summary>Player ship tactics (spec 400 to 412). Mod.Boot calls Register once at startup.</summary>
public static class ShipAiArea
{
    public static void Register() => AddAutotest();

    /// <summary>The tactics run in every career game, also after a win: the career goes on.</summary>
    [HarmonyPatch(typeof(CareerGameModeManager), nameof(CareerGameModeManager.OnGameStarting))]
    static class TickHook
    {
        static EventHandler<SimRoot>? _tick;

        static void Postfix(CareerGameModeManager __instance)
        {
            var game = __instance.Game;
            game.EventHub.Tick += _tick ??= Errors.Guarded<SimRoot>("ship tactics tick", ShipAiController.OnTick);
            game.Popping += (_, _) =>
            {
                game.EventHub.Tick -= _tick;
                TacticsEditor.Close();
            };
        }
    }

    /// <summary>Frame hook of the editor. UI only: it reads sim state and sends inputs.</summary>
    [HarmonyPatch(typeof(CareerGameModeManager), nameof(CareerGameModeManager.Update))]
    static class FrameHook
    {
        static void Postfix(CareerGameModeManager __instance)
        {
            if (Run.Current is not { } run || run.Mode != __instance) return;
            Errors.Guard("tactics editor", () => TacticsEditor.OnFrame(run));
        }
    }

    /// <summary>
    /// Spec 410: the AI button of the ships card, a vanilla command button (ShipGui.SHIP_COMMAND_BUTTON, 50 x 50, as the halt button)
    /// with an icon: green when every selected own ship has Auto on, HUD blue when none has, half and half when some have. One selected
    /// ship: a 50 px slot in the info row, which stretches its children, so the slot centres the button. Several selected ships: vanilla
    /// hides the info row (ShipsCard.UpdateForMultipleShips), so a second button sits in its own box at the card's right edge. A click
    /// opens the editor like Ctrl+K: for the first selected own ship; its "Apply to selected" reaches the others.
    /// </summary>
    [HarmonyPatch(typeof(ShipsCard), MethodType.Constructor, typeof(GameRoot), typeof(GameGui), typeof(IRectProvider))]
    static class CardButton
    {
        internal sealed class Pair(ImageButton single, LayoutBox singleSlot, ImageButton multi, LayoutBox multiBox)
        {
            public ImageButton Single { get; } = single;
            public LayoutBox SingleSlot { get; } = singleSlot;
            public ImageButton Multi { get; } = multi;
            public LayoutBox MultiBox { get; } = multiBox;
            /// <summary>The state that each button shows; null before its first update.</summary>
            public CardState? SingleState { get; set; }
            public CardState? MultiState { get; set; }
        }

        internal static readonly ConditionalWeakTable<ShipsCard, Pair> Buttons = new();
        static Dictionary<CardState, ISprite>? _icons;

        static void Postfix(ShipsCard __instance) => Errors.Guard("tactics card button", () =>
        {
            var single = NewButton();
            var slot = Slot(single);
            slot.Width = Size;
            __instance._infoIconsBox.Children.Add(slot);
            var multi = NewButton();
            var box = Slot(multi);
            box.SelfActive = false;
            box.DockMode = DockMode.Right;
            box.AutoSize.AutoWidthMode = AutoSizeMode.Enable;
            box.Children.BorderPadding = new Borders(10f, 10f, 0f, 0f);
            __instance.DockedChildren.Add(box);
            Buttons.AddOrUpdate(__instance, new Pair(single, slot, multi, box));
        });

        const float Size = 50f;

        /// <summary>A box without a frame that keeps the button at its own size in its middle.</summary>
        static LayoutBox Slot(ImageButton button)
        {
            var box = new LayoutBox();
            box.NineSlice.Flags = NineSliceFlags.None;
            box.Children.LayoutAlgorithm = LayoutAlgorithms.CenterMiddleByColumn;
            box.AddChild(button);
            return box;
        }

        static ImageButton NewButton()
        {
            var b = new ImageButton(Icon(CardState.Off), ShipGui.SHIP_COMMAND_BUTTON);
            // The game reads the tooltip text once while it builds the card, before the card has a sim: the ships come from the run.
            b.ToolTips.TextProvider = new Ux.LiveText(() => Run.Current is { } run ? Tip(Selected(run.Game.Sim)) : "");
            b.Clicked += (_, _) => Errors.Guard("tactics card click", Click);
            return b;
        }

        static ISprite Icon(CardState state)
        {
            _icons ??= Enum.GetValues<CardState>().ToDictionary(s => s, s => Ui.ModSprite.Read("shipai/card_icons.rules", s.ToString()));
            return _icons[state];
        }

        /// <summary>Shows the state on the button: a new icon only when the state changes.</summary>
        internal static CardState Show(ImageButton button, CardState? shown, CardState state)
        {
            if (shown != state) button.ImageSprite.CopySpriteFrom(Icon(state));
            return state;
        }

        /// <summary>Same ship as Ctrl+K: the first selected own ship.</summary>
        internal static void Click()
        {
            if (Run.Current is { } run && Selected(run.Game.Sim).FirstOrDefault() is { } ship) TacticsEditor.Open(run, ship);
        }

        /// <summary>The selected human ships that the local player commands, by ID.</summary>
        internal static List<Ship> Selected(SimRoot sim) =>
            sim.PlayerInput.SelectedCommandableShips.Where(s => s.IsHumanPlayerShip).OrderBy(s => s.UniqueID).ToList();

        /// <summary>The button state for these ships; null hides the button.</summary>
        internal static CardState? State(IReadOnlyList<Ship> ships) =>
            Run.Current is null || ships.Count == 0 ? null : ShipAiModel.CardStateOf(ships.Count(s => ShipTactics.Of(s) is { Auto: true }), ships.Count);

        /// <summary>The tooltip: the state, the preset of each ship, and what a click does.</summary>
        static string Tip(IReadOnlyList<Ship> ships)
        {
            if (State(ships) is not { } state) return "";
            int on = ships.Count(s => ShipTactics.Of(s) is { Auto: true });
            string line = state switch
            {
                CardState.On => Strings.GetText("HotCargoShipAi/CardOn"),
                CardState.Off => Strings.GetText("HotCargoShipAi/CardOff"),
                CardState.Mixed => Strings.FormatText("HotCargoShipAi/CardMixedFmt", on, ships.Count),
                _ => throw new ArgumentOutOfRangeException(nameof(ships), state, null),
            };
            // The personal presets file is small and read only while the tooltip shows.
            var personal = TacticsPresets.Load();
            string presets = string.Join(", ", ships.Select(s => PresetName(s, personal)).Distinct());
            return line + "\n" + Strings.FormatText(ships.Count == 1 ? "HotCargoShipAi/CardTipFmt" : "HotCargoShipAi/CardMultiTipFmt", presets);
        }

        static string PresetName(Ship ship, IEnumerable<(string Name, string Text)> personal)
        {
            string text = ShipTactics.Of(ship)?.Text ?? "";
            var (builtIn, own) = ShipAiModel.PresetOf(text, personal);
            return builtIn is { } p ? Strings.GetText("HotCargoShipAi/Preset_" + p)
                : own ?? Strings.GetText(text.Length == 0 ? "HotCargoShipAi/CardNoRows" : "HotCargoShipAi/CardCustom");
        }
    }

    [HarmonyPatch(typeof(ShipsCard), nameof(ShipsCard.UpdateForSingleShip))]
    static class CardUpdate
    {
        static void Postfix(ShipsCard __instance, Ship ship) => Errors.Guard("tactics card update", () =>
        {
            if (!CardButton.Buttons.TryGetValue(__instance, out var pair)) return;
            bool own = ship.Commands.IsCommandableByLocalPlayer && ship.IsHumanPlayerShip;
            var state = own ? CardButton.State([ship]) : null;
            pair.SingleSlot.SelfActive = state is not null;
            if (state is { } s) pair.SingleState = CardButton.Show(pair.Single, pair.SingleState, s);
            pair.MultiBox.SelfActive = false;
        });
    }

    [HarmonyPatch(typeof(ShipsCard), nameof(ShipsCard.UpdateForMultipleShips))]
    static class CardUpdateMulti
    {
        static void Postfix(ShipsCard __instance) => Errors.Guard("tactics card update", () =>
        {
            if (!CardButton.Buttons.TryGetValue(__instance, out var pair)) return;
            var state = CardButton.State(CardButton.Selected(__instance.Sim));
            pair.MultiBox.SelfActive = state is not null;
            if (state is { } s) pair.MultiState = CardButton.Show(pair.Multi, pair.MultiState, s);
        });
    }

    // ---- Autotest

    static SimRoot Sim() => Autotest.RequireRun().Game.Sim;

    /// <summary>The ship that the test commands act on: tactics_ship picks it by index among the acting player's ships (by ID).</summary>
    static int _shipIndex;
    /// <summary>tactics_ship &lt;name&gt;: the test ship by name (an autonomous test ship), else null.</summary>
    static string? _shipName;

    static Ship TestShip()
    {
        var own = OwnShips();
        // Only the acting player's ships: in co-op every player can have a ship with the same name (the start ship).
        if (_shipName is { } name)
            return own.FirstOrDefault(s => s.Metadata.PlainShipName == name) ?? throw new InvalidOperationException($"no own ship named '{name}'");
        if (_shipIndex >= own.Count) throw new InvalidOperationException($"no own ship {_shipIndex}; the player has {own.Count}");
        return own[_shipIndex];
    }

    /// <summary>The acting player's ships by ID (the order that tactics_ship uses).</summary>
    static List<Ship> OwnShips()
    {
        int player = Autotest.ActingPlayerIndex;
        return Sim().Ships.HumanPlayerShips.Where(s => s.Commands.IsCommandableBy(player)).OrderBy(s => s.UniqueID).ToList();
    }

    static ShipTactics Tactics() => ShipTactics.Of(TestShip()) ?? throw new InvalidOperationException("the test ship has no tactics");

    internal static string DesignPath(string name)
    {
        if (name.Length == 0 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) throw new ArgumentException($"bad design name '{name}'");
        string dir = Path.Combine(Mod.ModRoot, "testships");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, name + ".ship.png");
    }

    static Ship? _loadedDesign;

    static string RowsText(string arg) =>
        arg == "clear" ? "" : Enum.TryParse<Preset>(arg, out var p) && !char.IsDigit(arg[0]) ? ShipAiModel.Encode(ShipAiModel.PresetRows(p)) : arg;

    static void AddAutotest()
    {
        Autotest.Fields["tactics_rows"] = () => ShipTactics.Of(TestShip())?.Rows.Count ?? 0;
        Autotest.Fields["tactics_auto"] = () => ShipTactics.Of(TestShip()) is { Auto: true } ? 1 : 0;
        Autotest.Fields["tactics_hash"] = () => ShipAiModel.StableHash(ShipTactics.Of(TestShip())?.Text ?? "");
        Autotest.Fields["tactics_target_row"] = () => Tactics().TargetRow;
        Autotest.Fields["tactics_move_row"] = () => Tactics().MoveRow;
        Autotest.Fields["tactics_matched"] = () => Tactics().Matched;
        Autotest.Fields["tactics_waiting"] = () => Tactics().Waiting ? 1 : 0;
        Autotest.Fields["tactics_evals"] = () => ShipAiController.Evaluations;
        Autotest.Fields["tactics_held"] = () => TestShip().Weapons.Count(w => !w.HasPermissionToFire());
        Autotest.Fields["tactics_weapons"] = () => TestShip().Weapons.Count();
        // Weapons with a sim-given part target on the tactics target, and how many of them aim at a weapon or thruster part.
        Autotest.Fields["tactics_aimed"] = () => AimedParts().Count;
        Autotest.Fields["tactics_aimed_not_weapons"] = () => AimedParts().Count(k => !k.Contains(PartKind.Weapon));
        Autotest.Fields["tactics_aimed_not_engines"] = () => AimedParts().Count(k => !k.Contains(PartKind.Thruster));
        // Weapons that the rows let fire (not held by HoldFire or PointDefenseOnly).
        Autotest.Fields["tactics_unheld"] = () => TestShip().Weapons.Count() - (Tactics().Held?.Count ?? 0);
        // The test ship's current command: 0 none, 1 attack, 2 move, 3 follow, 4 local FTL jump, 5 other. Negative = a player gave it.
        Autotest.Fields["tactics_cmd"] = () =>
        {
            var c = TestShip().Commands.CurConfirmedCommand;
            int kind = c switch { null => 0, AttackCommand => 1, MoveCommand => 2, FollowCommand => 3, FtlLocalJumpCommand => 4, _ => 5 };
            return c?.UniqueID.PlayerID is not null ? -kind : kind;
        };
        Autotest.Fields["tactics_follow_radius"] = () => (TestShip().Commands.CurConfirmedCommand as AttackCommand)?.EdgeToEdgeFollowRadius ?? -1;
        // The tactics target: 1 hunter with the most health, 2 the least, 0 another ship, -1 none.
        Autotest.Fields["tactics_target"] = () =>
        {
            if (Tactics().Target is not { } id) return -1;
            var hunters = HunterSpawner.Hunters(Sim()).ToList();
            if (hunters.Count < 2) return 0;
            if (hunters.MaxBy(HunterOrders.HealthFraction)!.UniqueID == id) return 1;
            return hunters.MinBy(HunterOrders.HealthFraction)!.UniqueID == id ? 2 : 0;
        };
        Autotest.Fields["tactics_editor"] = () => TacticsEditor.IsOpen ? 1 : 0;
        Autotest.Fields["tactics_facing_row"] = () => Tactics().FacingRow;
        Autotest.Fields["tactics_trade_row"] = () => Tactics().TradeRow;
        // The facing of the tactics attack order: the nose offset from the target (rotation plus FlightDirection) in whole degrees,
        // 0 to 359, while it has a rotation target, else -999.
        Autotest.Fields["tactics_face"] = () => TestShip().Commands.CurConfirmedCommand is AttackCommand { RotationTarget: not null, Rotation: { } r }
            ? ((int)MathF.Round(((float)r + (float)TestShip().FlightDirection) * 180f / MathF.PI) % 360 + 360) % 360 : -999;
        Autotest.Fields["tactics_terms"] = () => ShipTactics.Of(TestShip())?.Rows.Sum(r => r.Terms.Length) ?? 0;
        Autotest.Fields["tactics_presets"] = () => TacticsPresets.Load().Count;
        // tactics_presets_reset: deletes this slot's personal preset file, so a test starts without presets. Test only.
        Autotest.Commands["tactics_presets_reset"] = _ => TacticsPresets.Reset();
        // tactics_select all|none: selects every own ship of the local player, like a drag box, or none. UI only.
        Autotest.Commands["tactics_select"] = arg =>
        {
            var input = Sim().PlayerInput;
            if (arg == "none") input.SelectShips([]);
            else input.SelectShips(Sim().Ships.HumanPlayerShips.Where(s => s.Commands.IsCommandableByLocalPlayer).ToList());
        };
        // The AI buttons of the ships card (spec 410): -1 hidden, else the CardState (0 off, 1 mixed, 2 on). The card updates in UI frames.
        Autotest.Fields["tactics_card_single"] = () => CardField(p => (p.Single, p.SingleState));
        Autotest.Fields["tactics_card_multi"] = () => CardField(p => (p.Multi, p.MultiState));
        // tactics_card_update: one vanilla update of the ships card, as a UI frame does it (ShipsCard.OnUpdatingUIState runs before UI
        // input, and a headless game has no UI input pass). UI only.
        Autotest.Commands["tactics_card_update"] = _ => Autotest.RequireRun().Game.Gui.ShipGui.ShipsCard.OnUpdatingUIState(null, EventArgs.Empty);
        // tactics_card_click: a click on the AI button of the ships card. UI only.
        Autotest.Commands["tactics_card_click"] = _ => CardButton.Click();
        Autotest.Fields["design_rows"] = () => _loadedDesign is null ? -1 : ShipTactics.Of(_loadedDesign)?.Rows.Count ?? 0;
        Autotest.Fields["design_hash"] = () => ShipAiModel.StableHash(_loadedDesign is null ? "" : ShipTactics.Of(_loadedDesign)?.Text ?? "");
        Autotest.Fields["design_auto"] = () => _loadedDesign is not null && ShipTactics.Of(_loadedDesign) is { Auto: true } ? 1 : 0;
        Autotest.Fields["design_parts"] = () => _loadedDesign?.BlueprintParts.Count ?? -1;
        Autotest.Fields["ship_parts"] = () => TestShip().BlueprintParts.Count;

        // tactics_ship <index>|<name>: the test commands act on this own ship (by ID order), or on the own ship with this name. Default 0.
        Autotest.Commands["tactics_ship"] = arg =>
        {
            bool index = int.TryParse(arg, NumberStyles.Integer, CultureInfo.InvariantCulture, out _shipIndex);
            _shipName = index ? null : arg;
        };
        // tactics <preset name|clear|HC1:...>: sets the rows of the test ship through the tactics input, like the editor. Auto keeps its value.
        // A real player action: run it without "mp".
        Autotest.Commands["tactics"] = arg =>
        {
            var ship = TestShip();
            Sim().RequireGame().NetManager.EnqueueInput(TacticsInput.Rows(ship, RowsText(arg), ShipTactics.Of(ship)?.Auto ?? false));
            Autotest.Wait(1);
        };
        // tacticsauto on|off: the Auto switch of the test ship, through the tactics input.
        Autotest.Commands["tacticsauto"] = arg =>
        {
            var ship = TestShip();
            Sim().RequireGame().NetManager.EnqueueInput(TacticsInput.Rows(ship, ShipTactics.Of(ship)?.Text ?? "", arg != "off"));
            Autotest.Wait(1);
        };
        // Human ships with Auto on, of every player (co-op checks).
        Autotest.Fields["tactics_any_auto"] = () => Sim().Ships.HumanPlayerShips.Count(s => ShipTactics.Of(s) is { Auto: true });
        // expecttacticsany <preset name|HC1:...>: some human ship, of any player, has exactly these rows.
        Autotest.Commands["expecttacticsany"] = arg =>
        {
            string want = RowsText(arg);
            var have = Sim().Ships.HumanPlayerShips.OrderBy(s => s.UniqueID).Select(s => ShipTactics.Of(s)?.Text ?? "").ToList();
            Autotest.Check(have.Contains(want), $"a human ship has tactics '{want}' (have: {string.Join(" | ", have)})");
        };
        // tactics_snapshot: records the rows and the Auto switch of every human ship that has tactics. Run it with "mp", so every
        // machine records at the same sim tick. A later resync or load must bring them back (tactics_snapshot_* fields).
        Autotest.Commands["tactics_snapshot"] = _ =>
        {
            _snapshot = Sim().Ships.HumanPlayerShips.Where(s => ShipTactics.Of(s) is not null)
                .ToDictionary(s => s.UniqueID, s => (ShipTactics.Of(s)!.Text, ShipTactics.Of(s)!.Auto));
            Log.Info($"AUTOTEST tactics snapshot: {string.Join(" | ", _snapshot.Select(kv => $"{kv.Key} auto={kv.Value.Auto} {kv.Value.Text}"))}");
        };
        // Ships of the snapshot that are live human ships now with the same rows and Auto switch.
        Autotest.Fields["tactics_snapshot_kept"] = () => SnapshotShips().Count(x => x.Same);
        // Ships of the snapshot that are live human ships now with other rows or another Auto switch. A ship that the fight
        // destroyed or made a wreck is not counted: the check does not depend on which ships survive.
        Autotest.Fields["tactics_snapshot_changed"] = () => SnapshotShips().Count(x => !x.Same);
        // expecttactics <preset name|clear|HC1:...>: the test ship has exactly these rows.
        Autotest.Commands["expecttactics"] = arg =>
        {
            string want = RowsText(arg), have = ShipTactics.Of(TestShip())?.Text ?? "";
            Autotest.Check(want == have, $"tactics are '{want}' (have '{have}')");
        };
        // expectdesign <preset name|clear|HC1:...>: the rows of the design that loaddesign read last.
        Autotest.Commands["expectdesign"] = arg =>
        {
            var design = _loadedDesign ?? throw new InvalidOperationException("no loaddesign yet");
            string want = RowsText(arg), have = ShipTactics.Of(design)?.Text ?? "";
            Autotest.Check(want == have, $"design tactics are '{want}' (have '{have}')");
        };
        // shipai <ShipAiTunables field> <value>: sets one tunable for this run. Test only.
        Autotest.Commands["shipai"] = arg => Autotest.SetField(Autotest.RequireRun().T.ShipAi, arg);
        // tactics_manual: a player move order 400 ahead of the test ship, sent like a right click. Real player action.
        Autotest.Commands["tactics_manual"] = _ =>
        {
            var ship = TestShip();
            var to = ship.DetWorldCenter + Vector2.FromAngle(ship.DetRotation) * 400f;
            var move = new MoveCommand(ship, ship.Commands.AllocLocalCommandID(), to, null, CollisionAvoidanceFlags.All, isTentative: false);
            Sim().RequireGame().NetManager.EnqueueInput(new MPEnqueueCommandsInput(move, append: false, playLocalSound: false));
        };
        // tactics_ally: a copy of the starting design as a second ship of the acting player, near the first one. Single player test only.
        // Formations (spec 423 to 427). The acting player's ships, by ID: ship 0 leads.
        Autotest.Fields["formation_followers"] = () => OwnShips().Count(s => s.Commands.CurConfirmedCommand is FollowCommand { FollowAngle: not null });
        // The smallest edge distance between two of the acting player's ships.
        Autotest.Fields["formation_min_gap"] = () =>
        {
            var own = OwnShips();
            float min = float.PositiveInfinity;
            for (int i = 0; i < own.Count; i++)
                for (int j = i + 1; j < own.Count; j++) min = Math.Min(min, HunterOrders.EdgeDistance(own[i], own[j]));
            return float.IsPositiveInfinity(min) ? -1 : min;
        };
        // formation_block <index>: puts own ship <index> on the line from ship 0 to the nearest enemy, halfway. Single player test only.
        Autotest.Commands["formation_block"] = arg =>
        {
            if (Autotest.RequireRun().IsMultiplayer) throw new InvalidOperationException("formation_block is single player only");
            var own = OwnShips();
            var lead = own[0];
            // The leader's attack target first: the nearest enemy can be another ship than the one that the leader aims at.
            var enemy = (lead.Commands.CurConfirmedCommand as AttackCommand)?.Target
                ?? Sim().Ships.AllShips.Where(e => lead.IsEnemiesWith(e) && !e.IsJunk).MinBy(e => HunterOrders.EdgeDistance(lead, e))
                ?? throw new InvalidOperationException("no enemy to block");
            Autotest.Teleport(own[int.Parse(arg, CultureInfo.InvariantCulture)], (lead.DetWorldCenter + enemy.DetWorldCenter) / 2f);
        };
        // formation_squeeze <a> <b>: puts own ship <b> right next to own ship <a> (a small gap). Single player test only.
        Autotest.Commands["formation_squeeze"] = arg =>
        {
            if (Autotest.RequireRun().IsMultiplayer) throw new InvalidOperationException("formation_squeeze is single player only");
            var p = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(x => int.Parse(x, CultureInfo.InvariantCulture)).ToArray();
            var own = OwnShips();
            var (a, b) = (own[p[0]], own[p[1]]);
            Autotest.Teleport(b, a.DetWorldCenter + new Vector2(a.WorldBoundingRadius + b.WorldBoundingRadius + 10f, 0f));
        };
        Autotest.Commands["tactics_ally"] = _ =>
        {
            var run = Autotest.RequireRun();
            if (run.IsMultiplayer) throw new InvalidOperationException("tactics_ally is single player only");
            var sim = run.Game.Sim;
            var first = sim.Ships.HumanPlayerShips.Where(s => s.IsLocalPlayerShip).MinBy(s => s.UniqueID) ?? throw new InvalidOperationException("no own ship");
            using var stream = new MemoryStream();
            first.SaveDesign(stream);
            var ally = Ship.Load(stream.ToArray(), first.Metadata.PlainShipName + " II", first.Metadata.PlayerIndex, builtByPlayer: true, randSeed: 777UL);
            // Each copy one ring further out: two copies on the same ring can land side by side, closer than the spacing rules.
            int own = sim.Ships.HumanPlayerShips.Count(s => s.IsLocalPlayerShip);
            ally.WorldCenter = Autotest.ClearSpotNear(sim, first.DetWorldCenter, own * (first.WorldBoundingRadius * 2f + 300f), ally.WorldBoundingRadius);
            sim.Ships.Add(ally);
            ally.InitializeToMax();
            Log.Info($"AUTOTEST tactics ally {ally.Metadata.PlainShipName} id={ally.UniqueID} human={ally.IsHumanPlayerShip}");
            Autotest.Wait(1);
        };
        // savedesign <name> [png]: saves the test ship's design. With png, through the game's own Save Ship (needs graphics: main slot).
        // Without png, the same design node goes to the file without the picture.
        Autotest.Commands["savedesign"] = arg =>
        {
            var parts = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            string path = DesignPath(parts[0]);
            var ship = TestShip();
            if (parts.Length > 1 && parts[1] == "png")
            {
                ship.SaveDesignAs(path, dialogOnError: false, out bool error);
                if (error) throw new InvalidOperationException($"SaveDesignAs failed: {path}");
            }
            else
            {
                using var file = File.Create(path);
                ship.SaveDesign(file);
            }
            Log.Info($"AUTOTEST saved design {path} ({new FileInfo(path).Length} bytes)");
        };
        // loaddesign <name> [vanilla]: loads a saved design like the ship library does. With vanilla, the tactics nodes are ignored,
        // like a game without the mod.
        Autotest.Commands["loaddesign"] = arg =>
        {
            var parts = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            _loadedDesign?.Dispose();
            ShipTactics.VanillaRead = parts.Length > 1 && parts[1] == "vanilla";
            try { _loadedDesign = Ship.Load(DesignPath(parts[0]), blueprintsOnly: true); }
            finally { ShipTactics.VanillaRead = false; }
            Log.Info($"AUTOTEST loaded design {parts[0]}: parts={_loadedDesign.BlueprintParts.Count} tactics='{ShipTactics.Of(_loadedDesign)?.Text}'");
        };
        // loadreplace <name>: the build menu's "Load ship (replace)" blueprint copy from a saved design into the test ship. Its tactics follow.
        Autotest.Commands["loadreplace"] = arg =>
        {
            var ship = TestShip();
            using var prototype = Ship.Load(DesignPath(arg), blueprintsOnly: true);
            using var update = ShipUpdateInfo.Alloc(ship);
            update.CopyBlueprintsFromShip(prototype);
            Autotest.Wait(1);
        };
        // tactics_editor open|close|apply <preset>|add|paste <text>: drives the editor window like a click. Real player actions.
        Autotest.Commands["tactics_editor"] = arg => TacticsEditor.TestCommand(Autotest.RequireRun(), TestShip(), arg);
        // tactics_damage <fraction>: sets every part of the test ship to this health fraction. Single player test only.
        Autotest.Commands["tactics_damage"] = arg =>
        {
            if (Autotest.RequireRun().IsMultiplayer) throw new InvalidOperationException("tactics_damage is single player only");
            float f = float.Parse(arg, CultureInfo.InvariantCulture);
            foreach (var part in TestShip().Parts.ToList()) part.Health = Math.Max(1, (int)(part.Rules.MaxHealth * f));
        };
        // tactics_strings: every fact, value, action, group, unit, preset and parameter name has a string, and each entry a tooltip.
        Autotest.Commands["tactics_strings"] = _ =>
        {
            var keys = Enum.GetNames<Cond>().SelectMany(n => new[] { "Cond_" + n, "Desc_" + n })
                .Concat(Enum.GetNames<Value>().SelectMany(n => new[] { "Value_" + n, "Desc_" + n }))
                .Concat(Enum.GetNames<Act>().SelectMany(n => new[] { "Act_" + n, "Desc_" + n }))
                .Concat(Enum.GetNames<Group>().Select(n => "Group_" + n))
                .Concat(Enum.GetNames<Unit>().Select(n => "Unit" + n + "Fmt"))
                .Concat(Enum.GetNames<StationPick>().Select(n => "Station_" + n))
                .Concat(Enum.GetNames<Preset>().Select(n => "Preset_" + n))
                .Concat(Enum.GetNames<PartKind>().Select(n => "PartKind_" + n))
                .Concat(Enum.GetNames<ShipClass>().Select(n => "ShipClass_" + n))
                .Concat(Enum.GetNames<GuardWho>().Select(n => "GuardWho_" + n))
                .Concat(Enum.GetNames<Side>().Select(n => "Side_" + n));
            var missing = keys.Where(k => !Strings.TryGetText("HotCargoShipAi/" + k, out _)).ToList();
            Autotest.Check(missing.Count == 0, $"tactics strings exist (missing: {string.Join(", ", missing)})");
        };
        Autotest.Commands["tactics_log"] = arg =>
        {
            var ship = TestShip();
            var t = ShipTactics.Of(ship);
            Log.Info($"AUTOTEST TACTICS {arg}: ship={ship.Metadata.PlainShipName} auto={t?.Auto} waiting={t?.Waiting} target={t?.TargetRow} move={t?.MoveRow} " +
                     $"matched={t?.Matched} cmd={ship.Commands.CurConfirmedCommand?.GetType().Name} health={HunterOrders.HealthFraction(ship):0.00} text='{t?.Text}'");
        };
    }

    static int CardField(Func<CardButton.Pair, (Button Button, CardState? State)> button)
    {
        var card = Autotest.RequireRun().Game.Gui.ShipGui.ShipsCard;
        if (!CardButton.Buttons.TryGetValue(card, out var pair)) throw new InvalidOperationException("the ships card has no AI buttons");
        var (b, state) = button(pair);
        // IsActive: the button and every parent are active (vanilla hides the info row with several ships).
        if (!b.IsActive) return -1;
        return (int)(state ?? throw new InvalidOperationException("the AI card button shows, but has no state yet"));
    }

    static Dictionary<Cosmoteer.Game.ObjectID, (string Text, bool Auto)>? _snapshot;

    static IEnumerable<(Ship Ship, bool Same)> SnapshotShips()
    {
        var snap = _snapshot ?? throw new InvalidOperationException("no tactics_snapshot yet");
        foreach (var ship in Sim().Ships.HumanPlayerShips.OrderBy(s => s.UniqueID))
            if (snap.TryGetValue(ship.UniqueID, out var want))
            {
                var t = ShipTactics.Of(ship);
                bool same = t is not null && t.Text == want.Text && t.Auto == want.Auto;
                if (!same) Log.Info($"AUTOTEST tactics snapshot: {ship.UniqueID} has auto={t?.Auto} '{t?.Text}', was auto={want.Auto} '{want.Text}'");
                yield return (ship, same);
            }
    }

    static List<HashSet<PartKind>> AimedParts()
    {
        var ship = TestShip();
        if (Tactics().Target is not { } id) return [];
        var result = new List<HashSet<PartKind>>();
        foreach (var w in ship.Weapons)
            if (w.ExplicitTargets.CurConfirmedTarget is { Target: Cosmoteer.Ships.Parts.Part p } c && c.UniqueID.PlayerID is null && p.Ship?.UniqueID == id)
                result.Add(ShipAiController.KindsOf(p.Rules));
        return result;
    }
}

using Cosmoteer;
using Cosmoteer.Game;
using Cosmoteer.Game.Multiplayer;
using Cosmoteer.Localization;
using Cosmoteer.Modes.Career;
using Halfling;
using Halfling.Graphics;
using Halfling.Gui.Dialogs;
using HotCargo.Core;

namespace HotCargo.Telemetry;

/// <summary>
/// Spec 1984 to 1986: a playtest note. A player presses Telemetry.NoteKey, types a short note and presses Enter (Escape cancels).
/// The note becomes a "note" event with what the screen showed at the key press, and a screenshot when the platform has one.
/// Local UI only: no MP input, no sim state. In co-op the game keeps running under the dialog (GameRoot.AlwaysUpdate: the MP
/// manager updates always); in single player it pauses while the dialog is open, like every vanilla dialog. A co-op client's note reaches the host with its telemetry file (spec 1592).
/// Limit: the key also fires while a vanilla text field (the co-op chat) has the focus, which opens a note. Revisit if that bothers.
/// </summary>
static class PlaytestNotes
{
    static TwoButtonTextEntryDialog? _open;

    /// <summary>Test only: the open note dialog.</summary>
    internal static TwoButtonTextEntryDialog? OpenDialog => _open;

    internal static void Register() => Run.Hooks.Frame += Frame;

    static void Frame(Run run)
    {
        if (_open is not null || Session.Current is null || !App.Keyboard.HotkeyPressed(run.T.Telemetry.NoteKey)) return;
        // Only over the game itself: not over the escape menu or another dialog.
        if (App.Director.PeekState() != run.Game) return;
        Open(run);
    }

    /// <summary>Takes the screen state and a screenshot now, then asks for the text.</summary>
    internal static async void Open(Run run)
    {
        try { await OpenAsync(run); }
        catch (Exception ex) { _open = null; Errors.Report("playtest note", ex.Message, ex); }
    }

    static async Task OpenAsync(Run run)
    {
        var game = run.Game;
        var screen = Screen(run);
        double realSeconds = Session.Current?.RealSeconds ?? 0;
        // The capture ends with this frame, before the dialog draws, so the screenshot shows what the player saw.
        var dialog = _open = new TwoButtonTextEntryDialog(TwoButtonTextEntryDialog.Default, Text("NotePrompt"), "", Text("NoteSave"), Text("NoteCancel"));
        string? file = Shot(run) is { } shot ? await shot : null;
        try { await App.Director.PushState(dialog); }
        finally { _open = null; }
        string? note = dialog.Text?.Trim();
        if (string.IsNullOrEmpty(note))
        {
            if (file is not null) File.Delete(Path.Combine(NotesFolder(), file));
            return;
        }
        string player = PlayerName(game);
        string wall = DateTime.UtcNow.ToString("O");
        void Fields(System.Text.Json.Utf8JsonWriter w)
        {
            w.WriteString("text", note);
            w.WriteString("player", player);
            w.WriteNumber("local_player", game.LocalPlayerIndex);
            w.WriteString("wall_utc", wall);
            Tele.Num(w, "pressed_real_s", realSeconds);
            w.WriteStartObject("screen");
            foreach (var (k, v) in screen) w.WriteString(k, v);
            w.WriteEndObject();
            if (file is not null) w.WriteString("screenshot", file); else w.WriteNull("screenshot");
        }
        Tele.Emit(Cat.Run, "note", Fields);
        // Spec 1988: the live notes file, one line per note at once; a co-op client also sends the line to the host at once.
        if (Session.Current is { } s)
        {
            string line = Tele.Json(w =>
            {
                w.WriteStartObject();
                w.WriteString("e", "note");
                Tele.Num(w, "t", s.RealSeconds);
                s.WriteContext(w);
                Fields(w);
                w.WriteEndObject();
            });
            AppendLive(line);
            Relay.SendNote(line);
        }
        Log.Info($"playtest note saved{(file is null ? "" : $" with {file}")}");
        Ux.UxKit.ChatLine(game, Halfling.StringTools.EscapeForXml(Text(file is null ? "NoteSaved" : "NoteSavedShot")));
    }

    static string NotesFolder() => Path.Combine(Path.GetDirectoryName(Session.Current!.FilePath)!, "notes");

    static readonly Lock LiveGate = new();

    /// <summary>Spec 1988: the live notes file of this machine: Mod.ModRoot/telemetry/notes.jsonl. Each line is written and closed at once.</summary>
    internal static string LivePath => Path.Combine(Mod.ModRoot, "telemetry", "notes.jsonl");

    internal static void AppendLive(string line)
    {
        lock (LiveGate)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LivePath)!);
            File.AppendAllText(LivePath, line.Replace('\n', ' ') + "\n");
        }
    }

    static string Text(string key) => Strings.GetText("HotCargoTelemetry/" + key);

    static string PlayerName(GameRoot game) =>
        game.NetManager is BaseMPManager mp && mp.TryGetPlayer(mp.LocalPlayerID, out var p) ? p.Name : GameApp.Instance.GetLocalPlayerName();

    /// <summary>What this player saw: an event page, an open comms window, the build tool, or a dialog over the game.</summary>
    static List<(string Key, string Value)> Screen(Run run)
    {
        var game = run.Game;
        var list = new List<(string, string)>();
        if (run.State.Events.Pending is { } page) list.Add(("event", page.EventId));
        if (game.Sim?.Mode is CareerSimModeManager career)
            foreach (var (callee, callers) in career.OpenComms.OrderBy(kv => kv.Key.UniqueID))
                if (callers.Any(c => c.Metadata.PlayerIndex == game.LocalPlayerIndex)) { list.Add(("comms", callee.Metadata.PlainShipName)); break; }
        if (game.Gui?.ShipGui?.BuildButton?.IsSelected == true) list.Add(("build", "true"));
        if (App.Director.PeekState() is { } top && top != game) list.Add(("dialog", top.GetType().Name));
        return list;
    }

    /// <summary>
    /// The vanilla end-of-frame capture (the path of the game's own screenshot key). Saved as telemetry/notes/&lt;run&gt;__&lt;time&gt;.png.
    /// Null without a graphics device (headless) or when the capture fails; the note is saved without it.
    /// </summary>
    static Task<string?>? Shot(Run run)
    {
        if (GameApp.IsHeadlessMode || App.Graphics is null || Session.Current is not { } s) return null;
        string name = $"{s.RunId}__{DateTime.Now:yyyyMMdd-HHmmss}.png";
        return Capture(Path.Combine(NotesFolder(), name), name);
    }

    static async Task<string?> Capture(string path, string name)
    {
        try
        {
            using TextureData data = await App.Graphics.GetEndOfFrameScreenShot().WaitAsync(TimeSpan.FromSeconds(3));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await Task.Run(() => { using var f = File.Create(path); data.SaveTo(f, ImageFileFormat.Png, 0); });
            return name;
        }
        catch (Exception ex)
        {
            Log.Info($"playtest note: no screenshot ({ex.GetType().Name}: {ex.Message})");
            return null;
        }
    }
}

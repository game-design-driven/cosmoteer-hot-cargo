using Cosmoteer;
using Cosmoteer.Game.Multiplayer;
using Cosmoteer.Modes.Career;
using Halfling;
using Halfling.Network;
using Halfling.Network.Rpc;
using HarmonyLib;
using HotCargo.Core;

namespace HotCargo.Telemetry;

/// <summary>
/// Spec 1592, 1593: co-op clients send their telemetry files to the host, which keeps a copy of each in
/// telemetry/players/&lt;player name&gt;/. The client's own files stay as they are.
/// Transport: a reliable RPC on a sub-channel of the game's MP connection, like the shared choice hovers (Presence), so the
/// files skip the input queue and the lockstep. Telemetry state only: no MP input, no sim state, no save.
/// The client sends one chunk at a time from the length that the host confirmed; the host appends only new bytes (RelayModel).
/// After a new connection (a join, a resync, a load) the client asks the host for its lengths again, so nothing is sent twice
/// and nothing is skipped.
/// Limit: the lines that a client writes after its last confirmed chunk reach the host only at its next join of the same run.
/// A client that quits for good, or crashes, sends the rest once when its game closes, without a confirmation. Revisit if such tails go missing.
/// Spec 1995 to 1997: the same chunks carry the note screenshots (a small JPEG, first in line), and the note lines go as their own
/// RPC that the client sends again until the host confirms it.
/// </summary>
static class Relay
{
    /// <summary>Sub-channel of BaseMPManager._splitter. The game uses 0 to 11; Presence uses 200.</summary>
    const byte ChannelId = 201;
    /// <summary>Bytes per chunk. Steam messages hold up to 512 KB; small chunks keep the game connection free for the lockstep.</summary>
    const int ChunkBytes = 32 * 1024;
    /// <summary>Real seconds without the host's answer before the client sends the chunk again.</summary>
    const double AnswerSeconds = 5;
    /// <summary>Real seconds between two scans of the client's telemetry folder while all is sent.</summary>
    const double ScanSeconds = 1;

    static BaseMPManager? _mp;
    static RpcManager? _rpc;
    static IChannel? _channel;
    /// <summary>A lobby return or a closed connection closes the channel before the telemetry session ends.</summary>
    static bool Connected => _channel is { IsOpen: true };
    static RpcActionInvoker<string, long, byte[]>? _chunk;
    static RpcActionInvoker<string, long>? _answer;
    static RpcActionInvoker<string>? _note, _noteOk;

    // ---- Client state, reset with each connection
    static readonly Dictionary<string, long> Confirmed = [];
    static (string File, double At)? _waiting;
    static double _nextScan;
    static string[] _files = [];

    // ---- Client notes that the host has not confirmed (spec 1996). They stay over connections, so a note waits for the next one.
    static readonly List<string> UnsentNotes = [];
    static double _notesSentAt = double.MinValue;

    // ---- Host: hashes of the note lines that it wrote, so a line that comes again is written once.
    static readonly HashSet<string> WrittenNotes = [];

    /// <summary>Test only: a smaller chunk, so a short session sends many chunks.</summary>
    internal static int? TestChunkBytes;

    /// <summary>For autotests: bytes the host confirmed (client), bytes and chunks received (host).</summary>
    internal static long ConfirmedBytes => Confirmed.Values.Sum();
    internal static long ReceivedBytes, ReceivedChunks;

    static string Folder => Path.Combine(Mod.ModRoot, "telemetry");
    internal static string PlayersFolder => Path.Combine(Folder, "players");
    internal static string NotesFolder => Path.Combine(Folder, "notes");

    /// <summary>Spec 1995: true on a co-op client, which makes a small screenshot of each note for the host.</summary>
    internal static bool IsClient => _mp is { IsHost: false };

    /// <summary>For autotests: note lines that the host has not confirmed.</summary>
    internal static int UnsentNoteCount => UnsentNotes.Count;

    /// <summary>The client's place of a relayed file.</summary>
    static string ClientPath(string name) => Path.Combine(RelayModel.Kind(name) == RelayFile.NoteShot ? NotesFolder : Folder, name);

    [HarmonyPatch(typeof(CareerGameModeManager), nameof(CareerGameModeManager.Update)), HarmonyPriority(Priority.Last)]
    static class FramePatch
    {
        static void Postfix() => Errors.Guard("telemetry relay", Frame);
    }

    static bool On(Run run) => run.IsMultiplayer && run.T.Telemetry.Enabled && run.T.Telemetry.RelayToHost;

    static void Frame()
    {
        // The channel also carries playtest notes (spec 1988), so it opens in every co-op game with telemetry, relay on or off.
        if (Run.Current is not { IsMultiplayer: true } run || !run.T.Telemetry.Enabled || !Attach(run)) return;
        if (_mp!.IsHost || !Connected) return;
        long t0 = Stats.Enter();
        double now = App.Clock.Time.Seconds;
        try
        {
            // Spec 1996: notes also go without a session (a load, a resync) and with RelayToHost off.
            if (UnsentNotes.Count > 0 && now - _notesSentAt >= AnswerSeconds) SendNotes(now);
            if (Session.Current is { } s) SendNext(s, On(run), now);
        }
        catch (InvalidOperationException) when (!Connected) { }
        finally { Stats.Exit(t0); }
    }

    /// <summary>Opens the channel on the current MP connection. A join, a resync or a load makes a new MP manager.</summary>
    static bool Attach(Run run)
    {
        if (run.Game.NetManager is not BaseMPManager mp) return false;
        if (mp == _mp) return true;
        if (_rpc is not null) _rpc.Channel = null;
        Confirmed.Clear();
        _waiting = null;
        _nextScan = 0;
        var splitter = mp._splitter;
        var channel = splitter.IsChannelOpen(ChannelId) ? splitter[ChannelId] : splitter.OpenChannel(ChannelId);
        _channel = channel;
        _rpc = new RpcManager(GameApp.Messenger, channel, mp._serializer, mp.IsHost);
        _chunk = _rpc.RegisterRpc(new RpcAction<string, long, byte[]>(OnChunk), "HotCargoTeleChunk", TransmitMode.Reliable);
        _answer = _rpc.RegisterRpc(new RpcAction<string, long>(OnAnswer), "HotCargoTeleLength", TransmitMode.Reliable);
        _note = _rpc.RegisterRpc(new RpcAction<string>(OnNote), "HotCargoTeleNote", TransmitMode.Reliable);
        _noteOk = _rpc.RegisterRpc(new RpcAction<string>(OnNoteOk), "HotCargoTeleNoteOk", TransmitMode.Reliable);
        _mp = mp;
        // A new connection sends the notes that the last one did not confirm at once.
        _notesSentAt = double.MinValue;
        return true;
    }

    // ---- Client

    /// <summary>
    /// The client's files of the current run, oldest first: the note screenshots for the host first (spec 1995, also with RelayToHost
    /// off), then every session of the run on this machine with its parts (with RelayToHost on).
    /// </summary>
    static string[] RunFiles(Session s, bool sessions) =>
    [
        .. Names(NotesFolder, s.RunId + "__*.jpg"),
        .. sessions ? Names(Folder, s.RunId + "__*.jsonl") : [],
    ];

    static IEnumerable<string> Names(string folder, string pattern) =>
        Directory.Exists(folder) ? Directory.EnumerateFiles(folder, pattern).Select(Path.GetFileName).OfType<string>().Order(StringComparer.Ordinal) : [];

    static void SendNext(Session s, bool sessions, double now)
    {
        if (_waiting is { } w && now - w.At < AnswerSeconds) return;
        _waiting = null;
        if (now >= _nextScan) { _files = RunFiles(s, sessions); _nextScan = now + ScanSeconds; }
        foreach (string name in _files)
        {
            long size = SafeLength(ClientPath(name));
            if (RelayModel.Next(Confirmed.TryGetValue(name, out long c) ? c : null, size, TestChunkBytes ?? ChunkBytes) is not { } next) continue;
            _chunk!(name, next.Offset, Read(ClientPath(name), next.Offset, next.Length), RpcReceivers.Server);
            _waiting = (name, now);
            return;
        }
    }

    static long SafeLength(string path) => File.Exists(path) ? new FileInfo(path).Length : 0;

    static byte[] Read(string path, long offset, int length)
    {
        if (length == 0) return [];
        using var f = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        f.Position = offset;
        var data = new byte[length];
        f.ReadExactly(data);
        return data;
    }

    static void OnAnswer(string file, long length, RpcInfo info)
    {
        if (_mp is null || !_mp.IsPlayerHost(info.SenderID) || RelayModel.Kind(file) is null) return;
        Confirmed[file] = length;
        if (_waiting?.File == file) _waiting = null;
    }

    /// <summary>
    /// The client's game closes: the writer has written its last lines (Session.End), and the rest goes once, without waiting for
    /// answers. Repeated bytes cost the host nothing.
    /// </summary>
    internal static void SendRest(Session s)
    {
        if (_mp is null || _mp.IsHost || _chunk is null || !s.Run.T.Telemetry.Enabled || !Connected) return;
        int chunk = TestChunkBytes ?? ChunkBytes;
        try
        {
            if (UnsentNotes.Count > 0) SendNotes(App.Clock.Time.Seconds);
            foreach (string name in RunFiles(s, On(s.Run)))
            {
                long size = SafeLength(ClientPath(name));
                for (long at = Confirmed.GetValueOrDefault(name); at < size; at += chunk)
                    _chunk(name, at, Read(ClientPath(name), at, (int)Math.Min(chunk, size - at)), RpcReceivers.Server);
            }
        }
        // The connection can close while the game closes. The rest then waits for the next join of the run.
        catch (InvalidOperationException) when (!Connected) { Log.Info("telemetry relay: the connection closed before the rest was sent"); }
    }

    /// <summary>Bytes of the client's run files that the host has not confirmed. A file whose host length is not known yet counts whole.</summary>
    internal static long Pending(Session s, bool closedOnly, RelayFile? only = null) =>
        RunFiles(s, On(s.Run)).Where(name => (!closedOnly || ClientPath(name) != s.WriterPath) && (only is null || RelayModel.Kind(name) == only))
            .Sum(name => Math.Max(0, SafeLength(ClientPath(name)) - Confirmed.GetValueOrDefault(name)));

    /// <summary>
    /// For autotests on the host: the copies of client files (good) and the copies that break the format (bad): a copy must start with
    /// session.start of a client (host false) or a file.part line, and each complete line must be one JSON object.
    /// </summary>
    internal static (int Good, int Bad) CheckCopies()
    {
        if (!Directory.Exists(PlayersFolder)) return (0, 0);
        int good = 0, bad = 0;
        foreach (string path in Directory.EnumerateFiles(PlayersFolder, "*.jsonl", SearchOption.AllDirectories))
        {
            string text;
            using (var f = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)) using (var r = new StreamReader(f)) text = r.ReadToEnd();
            // The last line can still be on its way.
            var lines = text.Split('\n')[..^1];
            bool ok = lines.Length > 0;
            for (int i = 0; ok && i < lines.Length; i++)
            {
                try
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(lines[i]);
                    if (i == 0) ok = doc.RootElement.GetProperty("e").GetString() switch
                    {
                        "session.start" => !doc.RootElement.GetProperty("host").GetBoolean(),
                        "file.part" => true,
                        _ => false,
                    };
                }
                catch (Exception) { ok = false; }
            }
            if (ok) good++; else bad++;
        }
        return (good, bad);
    }

    /// <summary>
    /// Spec 1988, 1996: a co-op client sends a playtest note line to the host at once, outside the file relay, and again every
    /// AnswerSeconds and after each new connection until the host confirms it.
    /// Limit: the list lives in memory, so a client that quits before the host confirms keeps the note only in its own files (and its
    /// session file copy on the host). Revisit if notes go missing that way.
    /// </summary>
    internal static void SendNote(string line)
    {
        if (_mp is not { IsHost: false }) return;
        UnsentNotes.Add(line);
        if (Connected) SendNotes(App.Clock.Time.Seconds);
        else Log.Error("playtest note: no connection to the host now; the note goes when the connection is back");
    }

    static void SendNotes(double now)
    {
        _notesSentAt = now;
        try { foreach (string line in UnsentNotes) _note!(line, RpcReceivers.Server); }
        catch (InvalidOperationException) when (!Connected) { }
    }

    static void OnNoteOk(string hash, RpcInfo info)
    {
        if (_mp is null || !_mp.IsPlayerHost(info.SenderID)) return;
        UnsentNotes.RemoveAll(line => Hash(line) == hash);
    }

    static string Hash(string line) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(line)), 0, 8);

    static void OnNote(string line, RpcInfo info)
    {
        if (_mp is not { IsHost: true } mp || string.IsNullOrWhiteSpace(line) || line.Length > 20_000) return;
        Errors.Guard("telemetry note relay", () =>
        {
            string hash = Hash(line);
            if (WrittenNotes.Add(hash))
            {
                string player = mp.TryGetPlayer(info.SenderID, out var p) ? p.Name : "";
                PlaytestNotes.AppendLive(RelayModel.HostNoteLine(line, player) ?? throw new InvalidOperationException("a client note line is no JSON object"));
            }
            _noteOk!(hash, RpcReceivers.Single(info.SenderID));
        });
    }

    /// <summary>Spec 1996: at the end of a client's game, the notes that the host never confirmed.</summary>
    internal static void ReportUnsent()
    {
        if (UnsentNotes.Count > 0) Log.Error($"playtest note: {UnsentNotes.Count} notes did not reach the host's notes.jsonl; they are in this machine's notes.jsonl and telemetry file");
    }

    // ---- Host

    static void OnChunk(string file, long offset, byte[] data, RpcInfo info)
    {
        if (_mp is not { IsHost: true } mp || Run.Current is not { } run || !run.T.Telemetry.Enabled) return;
        var kind = RelayModel.Kind(file);
        if (kind is null || offset < 0) { Log.Error($"telemetry relay: refused file name '{file}'"); return; }
        // Spec 1995: screenshots also with RelayToHost off, like the note lines; telemetry files only with it on.
        if (kind == RelayFile.Session && !On(run)) return;
        if (kind == RelayFile.NoteShot && offset + (data?.Length ?? 0) > RelayModel.MaxNoteShotBytes) { Log.Error($"telemetry relay: refused screenshot '{file}' past {RelayModel.MaxNoteShotBytes} bytes"); return; }
        long t0 = Stats.Enter();
        try
        {
            string name = mp.TryGetPlayer(info.SenderID, out var player) ? player.Name : "";
            string path = kind == RelayFile.NoteShot
                ? Path.Combine(NotesFolder, RelayModel.HostShotName(name, file))
                : Path.Combine(PlayersFolder, RelayModel.FolderName(name), file);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using var f = new FileStream(path, FileMode.OpenOrCreate, FileAccess.Write, FileShare.Read);
            var (skip, write) = RelayModel.Accept(f.Length, offset, data?.Length ?? 0);
            if (write > 0)
            {
                f.Position = f.Length;
                f.Write(data!, skip, write);
                ReceivedBytes += write;
            }
            ReceivedChunks++;
            _answer!(file, f.Length, RpcReceivers.Single(info.SenderID));
        }
        catch (Exception ex) { Errors.Report("telemetry relay write", ex.Message, ex); }
        finally { Stats.Exit(t0); }
    }
}

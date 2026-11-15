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
/// A client that quits for good sends the rest once when its game closes, without a confirmation. Revisit if such tails go missing.
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
    static RpcActionInvoker<string, long, byte[]>? _chunk;
    static RpcActionInvoker<string, long>? _answer;

    // ---- Client state, reset with each connection
    static readonly Dictionary<string, long> Confirmed = [];
    static (string File, double At)? _waiting;
    static double _nextScan;
    static string[] _files = [];

    /// <summary>Test only: a smaller chunk, so a short session sends many chunks.</summary>
    internal static int? TestChunkBytes;

    /// <summary>For autotests: bytes the host confirmed (client), bytes and chunks received (host).</summary>
    internal static long ConfirmedBytes => Confirmed.Values.Sum();
    internal static long ReceivedBytes, ReceivedChunks;

    static string Folder => Path.Combine(Mod.ModRoot, "telemetry");
    internal static string PlayersFolder => Path.Combine(Folder, "players");

    [HarmonyPatch(typeof(CareerGameModeManager), nameof(CareerGameModeManager.Update)), HarmonyPriority(Priority.Last)]
    static class FramePatch
    {
        static void Postfix() => Errors.Guard("telemetry relay", Frame);
    }

    static bool On(Run run) => run.IsMultiplayer && run.T.Telemetry.Enabled && run.T.Telemetry.RelayToHost;

    static void Frame()
    {
        if (Run.Current is not { } run || !On(run) || !Attach(run) || _mp!.IsHost || Session.Current is not { } s) return;
        long t0 = Stats.Enter();
        try { SendNext(s, App.Clock.Time.Seconds); }
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
        _rpc = new RpcManager(GameApp.Messenger, channel, mp._serializer, mp.IsHost);
        _chunk = _rpc.RegisterRpc(new RpcAction<string, long, byte[]>(OnChunk), "HotCargoTeleChunk", TransmitMode.Reliable);
        _answer = _rpc.RegisterRpc(new RpcAction<string, long>(OnAnswer), "HotCargoTeleLength", TransmitMode.Reliable);
        _mp = mp;
        return true;
    }

    // ---- Client

    /// <summary>The client's files of the current run, oldest first: every session of the run on this machine, with its parts.</summary>
    static string[] RunFiles(Session s) =>
        Directory.Exists(Folder) ? [.. Directory.EnumerateFiles(Folder, s.RunId + "__*.jsonl").Select(Path.GetFileName).OfType<string>().Order(StringComparer.Ordinal)] : [];

    static void SendNext(Session s, double now)
    {
        if (_waiting is { } w && now - w.At < AnswerSeconds) return;
        _waiting = null;
        if (now >= _nextScan) { _files = RunFiles(s); _nextScan = now + ScanSeconds; }
        foreach (string name in _files)
        {
            long size = SafeLength(Path.Combine(Folder, name));
            if (RelayModel.Next(Confirmed.TryGetValue(name, out long c) ? c : null, size, TestChunkBytes ?? ChunkBytes) is not { } next) continue;
            _chunk!(name, next.Offset, Read(Path.Combine(Folder, name), next.Offset, next.Length), RpcReceivers.Server);
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
        if (_mp is null || !_mp.IsPlayerHost(info.SenderID) || !RelayModel.IsFileName(file)) return;
        Confirmed[file] = length;
        if (_waiting?.File == file) _waiting = null;
    }

    /// <summary>
    /// The client's game closes: the writer has written its last lines (Session.End), and the rest goes once, without waiting for
    /// answers. Repeated bytes cost the host nothing.
    /// </summary>
    internal static void SendRest(Session s)
    {
        if (_mp is null || _mp.IsHost || _chunk is null || !On(s.Run)) return;
        int chunk = TestChunkBytes ?? ChunkBytes;
        foreach (string name in RunFiles(s))
        {
            long size = SafeLength(Path.Combine(Folder, name));
            for (long at = Confirmed.GetValueOrDefault(name); at < size; at += chunk)
                _chunk(name, at, Read(Path.Combine(Folder, name), at, (int)Math.Min(chunk, size - at)), RpcReceivers.Server);
        }
    }

    /// <summary>Bytes of the client's run files that the host has not confirmed. A file whose host length is not known yet counts whole.</summary>
    internal static long Pending(Session s) =>
        RunFiles(s).Sum(name => Math.Max(0, SafeLength(Path.Combine(Folder, name)) - Confirmed.GetValueOrDefault(name)));

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

    // ---- Host

    static void OnChunk(string file, long offset, byte[] data, RpcInfo info)
    {
        if (_mp is not { IsHost: true } mp || Run.Current is not { } run || !On(run)) return;
        if (!RelayModel.IsFileName(file) || offset < 0) { Log.Error($"telemetry relay: refused file name '{file}'"); return; }
        long t0 = Stats.Enter();
        try
        {
            string name = mp.TryGetPlayer(info.SenderID, out var player) ? player.Name : "";
            string folder = Path.Combine(PlayersFolder, RelayModel.FolderName(name));
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, file);
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

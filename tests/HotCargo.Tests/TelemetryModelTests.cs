using System.Buffers;
using System.Text.Json;
using HotCargo.Telemetry;
using Xunit;

namespace HotCargo.Tests;

public class TelemetryModelTests
{
    static Dictionary<string, long> D(params (string Side, long Damage)[] d) => d.ToDictionary(x => x.Side, x => x.Damage);

    static JsonElement Json(Action<Utf8JsonWriter> body)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var w = new Utf8JsonWriter(buffer))
        {
            w.WriteStartObject();
            body(w);
            w.WriteEndObject();
        }
        return JsonDocument.Parse(buffer.WrittenMemory).RootElement.Clone();
    }

    [Fact]
    public void Windows_with_short_gaps_join_into_one_fight()
    {
        var j = new FightJoiner(90);
        Assert.Null(j.AddWindow(100, 5, 1, D(("player", 50), ("hostile", 200))));
        Assert.Null(j.AddWindow(105, 5, 1, D(("neutral", 7))));
        Assert.Null(j.AddDestroyed(150, 1, "hostile"));
        Assert.Null(j.AddWindow(180, 5, 1, D(("hunter", 30))));
        j.AddCrewDeath();
        var f = j.Close();
        Assert.NotNull(f);
        Assert.Equal(95, f!.Start);
        Assert.Equal(180, f.End);
        Assert.Equal(50, f.Damage["player"]);
        Assert.Equal(1, f.Destroyed["hostile"]);
        Assert.Equal(1, f.CrewLost);
        Assert.Equal(2, f.Windows);
    }

    [Fact]
    public void A_quiet_gap_or_another_sector_ends_the_fight()
    {
        var j = new FightJoiner(90);
        j.AddWindow(100, 5, 1, D(("player", 10)));
        var ended = j.AddWindow(300, 5, 1, D(("hostile", 10)));
        Assert.Equal(100, ended!.End);
        Assert.Equal(295, j.Current!.Start);
        var bySector = j.AddWindow(305, 5, 2, D(("hostile", 10)));
        Assert.Equal(1, bySector!.Sector);
        Assert.Equal(2, j.Current!.Sector);
        Assert.NotNull(j.Tick(500, 2));
        Assert.Null(j.Current);
    }

    [Fact]
    public void Neutral_damage_or_a_lone_wreck_makes_no_fight()
    {
        var j = new FightJoiner(90);
        j.AddWindow(100, 5, 1, D(("neutral", 500)));
        Assert.Null(j.Current);
        j.AddDestroyed(110, 1, "neutral");
        Assert.Null(j.Current);
        j.AddDestroyed(120, 1, "hostile");
        Assert.Null(j.Close());
    }

    [Fact]
    public void A_ship_in_pieces_counts_once_with_the_root_side()
    {
        var l = new ShipLineage<int, TelemetryModel.ShipInfo>();
        var hunter = new TelemetryModel.ShipInfo("1", "Corporate Arche", 100017, "hunter", "corp", true, 59600, ["hc_hunter"]);
        Assert.Equal((true, (TelemetryModel.ShipInfo?)null), l.Junked(99));
        l.Split(1, [2, 3], () => hunter);
        l.Split(2, [4], () => throw new InvalidOperationException("a piece keeps its root"));
        Assert.Equal("hunter", l.InfoOf(4)!.Side);
        Assert.False(l.Junked(2).Ends);
        Assert.False(l.Junked(4).Ends);
        Assert.False(l.Junked(1).Ends);
        var (ends, root) = l.Junked(3);
        Assert.True(ends);
        Assert.Equal(59600, root!.Value);
        Assert.Null(l.InfoOf(3));
    }

    [Fact]
    public void A_live_piece_keeps_the_ship_alive()
    {
        var l = new ShipLineage<int, TelemetryModel.ShipInfo>();
        l.Split(1, [2], () => new TelemetryModel.ShipInfo("1", "Byte", 100000, "hostile", null, false, 57800, []));
        Assert.False(l.Junked(1).Ends);
        Assert.Equal("hostile", l.InfoOf(2)!.Side);
    }

    [Fact]
    public void Session_numbers_count_the_files_of_the_run()
    {
        string[] files = ["r1__a.jsonl", "r1__a.part1.jsonl", "r1__b.jsonl", "r2__c.jsonl", "r1__x.txt"];
        Assert.Equal(3, TelemetryModel.SessionNumber(1, files, "r1"));
        Assert.Equal(6, TelemetryModel.SessionNumber(5, files, "r1"));
        Assert.Equal(1, TelemetryModel.SessionNumber(0, [], "r1"));
    }

    [Fact]
    public void Scrub_removes_user_folders_and_steam_ids()
    {
        string user = "Some.User";
        string steam = "7656119" + "8000000001";
        string windows = string.Join('\\', "C:", "Users", user, "Saved Games", "Cosmoteer", steam, "Mods");
        string scrubbed = TelemetryModel.Scrub($"writes {windows}\\x.jsonl");
        Assert.DoesNotContain(user, scrubbed);
        Assert.DoesNotContain(steam, scrubbed);
        Assert.Contains("<user>", scrubbed);
        Assert.Contains("<steam id>", scrubbed);
        Assert.Equal("/home/<user>/a and /Users/<user>", TelemetryModel.Scrub("/home/" + user + "/a and /Users/" + user));
        Assert.Equal("tick 76561190 and 12345678901234567890", TelemetryModel.Scrub("tick 76561190 and 12345678901234567890"));
    }

    [Fact]
    public void Capture_how_comes_from_the_caller()
    {
        Assert.Equal("claim", TelemetryModel.CaptureHow(-3, "MPSetCrewHomeShipInput.Execute < X.Y"));
        Assert.Equal("claim", TelemetryModel.CaptureHow(-3, "Unknown.Frame"));
        Assert.Equal("joined", TelemetryModel.CaptureHow(100001, "Join.MakePlayerShip < Apprentice.Tick"));
        Assert.Equal("other", TelemetryModel.CaptureHow(100001, "Unknown.Frame"));
    }

    [Fact]
    public void Jump_ships_show_drive_capacity_and_tech()
    {
        var e = Json(w => TelemetryModel.WriteJumpShips(w, "left",
        [
            new("A1", "Mothership", 0, true, 40, 40, 55, 3, 300),
            new("B2", "Taken", 1, false, 0, 0, null, 0, 80),
        ]));
        var left = e.GetProperty("left");
        Assert.Equal(2, left.GetArrayLength());
        Assert.True(left[0].GetProperty("too_big").GetBoolean());
        Assert.Equal(3, left[0].GetProperty("tech").GetInt32());
        Assert.False(left[1].GetProperty("drive").GetBoolean());
        Assert.Equal(JsonValueKind.Null, left[1].GetProperty("cost").ValueKind);
        Assert.False(left[1].GetProperty("too_big").GetBoolean());
    }

    [Fact]
    public void Capture_and_fight_events_carry_their_key_fields()
    {
        var old = new TelemetryModel.ShipInfo("D4AC46ED", "Corporate Liability", 100018, "hunter", "corp", true, 94525, ["hc_hunter"]);
        var c = Json(w => TelemetryModel.WriteCapture(w, old, 0, "claim", 94800, true, 0));
        Assert.Equal("hunter", c.GetProperty("old_side").GetString());
        Assert.Equal(0, c.GetProperty("player").GetInt32());
        Assert.Equal("claim", c.GetProperty("how").GetString());
        Assert.True(c.GetProperty("drive").GetBoolean());

        var j = new FightJoiner(90);
        j.AddWindow(100, 5, 2, D(("player", 40), ("hunter", 60)));
        j.AddDestroyed(101, 2, "player");
        j.AddDestroyed(102, 2, "hunter");
        j.AddDestroyed(103, 2, "hostile");
        var f = Json(w => TelemetryModel.WriteFight(w, j.Close()!));
        Assert.Equal(1, f.GetProperty("losses").GetInt32());
        Assert.Equal(2, f.GetProperty("kills").GetInt32());
        Assert.Equal(60, f.GetProperty("damage").GetProperty("hunter").GetInt64());
        Assert.Equal(["hostile", "hunter", "player"], f.GetProperty("sides").EnumerateArray().Select(s => s.GetString()!).ToArray());
    }
}

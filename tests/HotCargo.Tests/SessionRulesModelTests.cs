using HotCargo.Dev;
using Xunit;

namespace HotCargo.Tests;

public class SessionRulesModelTests
{
    static (string, string)[] Script(params string[] lines) =>
        [.. lines.Select(l => l.IndexOf(' ') is var i and >= 0 ? (l[..i], l[(i + 1)..]) : (l, ""))];

    [Fact]
    public void A_session_with_the_shield_before_its_first_step_passes() =>
        Assert.Null(SessionRulesModel.ShieldProblem(Script("mphost 1", "mpstart 2", "mp godmode on", "mp shieldplayers on", "step a.b")));

    [Fact]
    public void A_session_with_only_godmode_fails() =>
        Assert.NotNull(SessionRulesModel.ShieldProblem(Script("mphost 1", "mp godmode on", "step a.b", "mp shieldplayers on")));

    [Fact]
    public void A_stated_reason_passes_and_an_empty_one_fails()
    {
        Assert.Null(SessionRulesModel.ShieldProblem(Script("mphost 1", "noshield losses are allowed", "step a.b")));
        Assert.NotNull(SessionRulesModel.ShieldProblem(Script("mphost 1", "noshield", "step a.b")));
    }

    [Fact]
    public void Single_player_scripts_and_clients_have_no_rule()
    {
        Assert.Null(SessionRulesModel.ShieldProblem(Script("newgame run", "step a.b")));
        Assert.Null(SessionRulesModel.ShieldProblem(Script("mpjoin 1", "step a.b")));
    }
}

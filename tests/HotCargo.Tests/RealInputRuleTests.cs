using System.Runtime.CompilerServices;
using Xunit;

namespace HotCargo.Tests;

/// <summary>
/// Test code presses game controls only through RealInput (src/HotCargo/Dev/RealInput.cs), the game's own input path. A direct call
/// of a click handler (`InvokeClicked()`, a vanilla `On…Clicked(…)`) skips the press and release frames, so a test can pass while the
/// player's click does nothing (issue #393, twice).
/// </summary>
public class RealInputRuleTests
{
    /// <summary>
    /// Direct calls in test code from before the rule (other areas), by file. A file may not get more; when one goes, lower its count.
    /// Never raise a count to pass the test: press the control through RealInput.
    /// </summary>
    static readonly Dictionary<string, int> Before = new()
    {
        ["Dev/MpAutotest.cs"] = 4,
        ["Onboarding/OnboardingAutotest.cs"] = 1,
        ["Ux/UxAutotest.cs"] = 5,
    };

    static string Source([CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", "..", "src", "HotCargo"));

    /// <summary>Calls of `InvokeClicked(` or of a member `.On…Clicked(` on the line.</summary>
    static int DirectClicks(string line)
    {
        int count = 0;
        for (int at = line.IndexOf("Clicked(", StringComparison.Ordinal); at >= 0; at = line.IndexOf("Clicked(", at + 1, StringComparison.Ordinal))
        {
            int start = at;
            while (start > 0 && (char.IsLetterOrDigit(line[start - 1]) || line[start - 1] == '_')) start--;
            string name = line[start..at];
            if (name == "Invoke" || name.StartsWith("On", StringComparison.Ordinal) && start > 0 && line[start - 1] == '.') count++;
        }
        return count;
    }

    [Fact]
    public void Test_code_presses_controls_only_through_RealInput()
    {
        var root = Source();
        var found = Directory.EnumerateFiles(root, "*Autotest*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Select(f => (File: Path.GetRelativePath(root, f).Replace(Path.DirectorySeparatorChar, '/'), Count: File.ReadLines(f).Sum(DirectClicks)))
            .Where(x => x.Count > 0)
            .ToDictionary(x => x.File, x => x.Count);
        Assert.NotEmpty(Directory.EnumerateFiles(root, "*Autotest*.cs", SearchOption.AllDirectories));
        Assert.Equal(Before.OrderBy(kv => kv.Key), found.OrderBy(kv => kv.Key));
    }

    [Theory]
    [InlineData("box.OnCancelAllClicked(null, EventArgs.Empty);", 1)]
    [InlineData("button.ClickController.InvokeClicked();", 1)]
    [InlineData("[HarmonyPatch(\"OnCancelAllClicked\")]", 0)]
    [InlineData("btn.Clicked += OnClicked;", 0)]
    public void A_direct_click_is_a_call_of_InvokeClicked_or_of_a_member_On_Clicked(string line, int calls) =>
        Assert.Equal(calls, DirectClicks(line));
}

using Cosmoteer.Game;
using Cosmoteer.Localization;
using Cosmoteer.Modes.Career.Missions;
using Cosmoteer.Ships;
using Cosmoteer.Simulation;
using HotCargo.Core;
using HotCargo.SharedChoice;
using HotCargo.Ui;

namespace HotCargo.Missions;

/// <summary>
/// Spec 1850, 1852: the comms page of a waiting escort, "Ready to leave?". "Ready" is a shared choice of the domain "escort", so
/// co-op players see hovers and votes. "Not yet" only closes the window. The page serial is the escort's ship ID.
/// </summary>
internal sealed class EscortHail : IHailProvider, ISharedChoiceDomain
{
    public static readonly EscortHail Instance = new();
    public const string DomainName = "escort";
    public const int OptionReady = 0;

    EscortHail() { }

    /// <summary>The waiting escort objective of an accepted mission whose escort is this ship, or null. Sim state only.</summary>
    internal static EscortObjective? WaitingFor(Run run, ObjectID ship)
    {
        var found = new List<EscortObjective>();
        foreach (Mission m in run.Mode.Missions)
            if (m.Completion == CompletionState.InProgress)
                foreach (var o in m.Objectives) o.GetObjectives(found);
        return found.FirstOrDefault(o => o.Waiting && o.Escort.ShipID == ship);
    }

    public static int Serial(ObjectID ship) => unchecked((int)(uint)ship);

    static string Text(string key) => Strings.GetText("HotCargoEscortHail/" + key);

    // ---- Comms

    public bool Answers(Ship caller, Ship callee) =>
        Run.Current is { } run && caller.Sim is { } sim && sim == run.Game.Sim && WaitingFor(run, callee.UniqueID) is not null;

    /// <summary>The waiting escort's page is this page only: hire or trade lines of other providers would hide the question.</summary>
    public bool Owns(Ship caller, Ship callee) => Answers(caller, callee);

    public void Build(Ship caller, Ship callee, HailPage page)
    {
        if (page.Shared is not null) throw new InvalidOperationException("the escort page owns its shared choices");
        page.Shared = (DomainName, Serial(callee.UniqueID));
        page.AddText(Text("Ask"));
        page.AddChoice(Text("Ready"), OptionReady, close: true);
        page.AddButton(Text("NotYet"), null, close: true);
    }

    // ---- Shared choice

    string ISharedChoiceDomain.Name => DomainName;

    public int OptionCount(Run run, int serial) => WaitingFor(run, new ObjectID(unchecked((uint)serial))) is null ? 0 : 1;

    public bool Apply(Run run, SimRoot sim, int playerIndex, int serial, int option)
    {
        if (option != OptionReady) throw new ArgumentOutOfRangeException(nameof(option), option, "the escort page has one option");
        var escort = WaitingFor(run, new ObjectID(unchecked((uint)serial))) ?? throw new InvalidOperationException($"no waiting escort {serial}");
        escort.Depart(sim, playerIndex);
        CommsHail.Unlock(DomainName, serial);
        return true;
    }

    /// <summary>The input of a "Ready" click. For autotests.</summary>
    internal static SharedChoiceInput Pick(ObjectID ship) => new(DomainName, Serial(ship), SharedChoiceAction.Choose, OptionReady);
}

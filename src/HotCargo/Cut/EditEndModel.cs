namespace HotCargo.Cut;

/// <summary>Spec 2477: the mode of the build window when the player tries to end a cut or weld.</summary>
public enum EditMode { Live, Blueprint }

/// <summary>Spec 2477: the stage of a cut or weld order.</summary>
public enum EditStage
{
    /// <summary>Made in blueprint mode, waits for "Make it so".</summary>
    Pending,
    /// <summary>A weld pulls the piece to its place.</summary>
    Pull,
    /// <summary>Crew work on the edges or joints.</summary>
    Work,
    /// <summary>Every edge but one is done: the next one splits the ship or merges the piece.</summary>
    LastEdge,
}

/// <summary>Spec 2477: each control that a player can use while a cut or weld order exists.</summary>
public enum EditControl
{
    CancelAll,
    DiscardChanges,
    /// <summary>Ctrl+Enter, the hotkey of "Make it so" and "Cancel all" (Inputs.Design.Construct).</summary>
    Hotkey,
    RightClick,
    Esc,
    OtherTool,
    CloseEditor,
}

/// <summary>Spec 2477: what a control does to the order.</summary>
public enum EditOutcome
{
    /// <summary>The order ends.</summary>
    Ends,
    /// <summary>The control is there, and the order goes on.</summary>
    Stays,
    /// <summary>The control is hidden or disabled, and the order goes on.</summary>
    Unavailable,
    /// <summary>The order goes on, whether the control shows or not.</summary>
    GoesOn,
}

public readonly record struct EditCase(EditMode Mode, EditStage Stage, EditControl Control)
{
    public override string ToString() => $"{Mode}/{Stage}/{Control}";
}

/// <summary>
/// Spec 2477: every way to end a cut or weld, in every mode and stage, and the expected result. The game test (`edit-end`) walks
/// <see cref="Cases"/> with the real widgets, so a new mode, stage or control is tested as soon as it is in an enum here.
/// </summary>
public static class EditEndModel
{
    /// <summary>Whether an order can be in <paramref name="stage"/> in <paramref name="mode"/>. Only a weld pulls.</summary>
    public static bool Exists(EditMode mode, EditStage stage, bool pulls) => stage switch
    {
        EditStage.Pending => mode == EditMode.Blueprint,
        EditStage.Pull => pulls,
        EditStage.Work or EditStage.LastEdge => true,
        _ => throw new ArgumentOutOfRangeException(nameof(stage), stage, null),
    };

    public static IEnumerable<EditCase> Cases(bool pulls) =>
        from mode in Enum.GetValues<EditMode>()
        from stage in Enum.GetValues<EditStage>()
        where Exists(mode, stage, pulls)
        from control in Enum.GetValues<EditControl>()
        select new EditCase(mode, stage, control);

    public static EditOutcome Expected(EditCase c) => c.Control switch
    {
        // Spec 2475: vanilla shows "Cancel all" only in live mode, for work that runs. A pending order has "Make it so".
        EditControl.CancelAll => c.Mode == EditMode.Live && c.Stage != EditStage.Pending ? EditOutcome.Ends : EditOutcome.Unavailable,
        // In blueprint mode "Discard changes" drops pending edits and work in progress, as for vanilla construction. In live mode it
        // can be enabled by other design changes; it never stops work that runs there.
        EditControl.DiscardChanges => c.Mode == EditMode.Blueprint ? EditOutcome.Ends : EditOutcome.GoesOn,
        // The hotkey presses the button that shows: "Make it so" applies a pending order, which then goes on; "Cancel all" (live
        // mode) ends one that runs; in blueprint mode the "No work needed" button takes no press.
        EditControl.Hotkey => c.Stage == EditStage.Pending ? EditOutcome.Stays : c.Mode == EditMode.Live ? EditOutcome.Ends : EditOutcome.GoesOn,
        // Spec 2484, 2473: a right click with the tool, with nothing picked or dragged, cancels the ship's order.
        EditControl.RightClick => EditOutcome.Ends,
        // Esc, another tool and closing the build window change only the tool, as for vanilla construction.
        EditControl.Esc or EditControl.OtherTool or EditControl.CloseEditor => EditOutcome.Stays,
        _ => throw new ArgumentOutOfRangeException(nameof(c), c, null),
    };
}

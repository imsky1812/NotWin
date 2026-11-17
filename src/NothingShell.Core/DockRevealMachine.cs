namespace NothingShell.Core;

public readonly record struct DockInputs(
    bool CursorAtEdge, bool CursorOverDock, bool PanelOpen, bool KeepVisible, bool Fullscreen);

/// <summary>
/// Deterministic dock show/hide state machine. It never reads a clock: the caller passes <c>now</c>
/// to <see cref="Update"/> and should call it again at <see cref="NextDeadline"/> (null = nothing
/// pending, so no timer is needed). Deadlines are inclusive: elapsed >= delay fires.
/// </summary>
public sealed class DockRevealMachine(TimeSpan revealDelay, TimeSpan hideDelay)
{
    private TimeSpan? _edgeSince;   // hidden: cursor has been at the edge since
    private TimeSpan? _idleSince;   // visible: cursor has been away from dock/edge (no panel) since

    public static DockRevealMachine CreateDefault() =>
        new(TimeSpan.FromMilliseconds(150), TimeSpan.FromMilliseconds(500));

    public bool Visible { get; private set; }

    public TimeSpan? NextDeadline =>
        !Visible ? _edgeSince + revealDelay : _idleSince + hideDelay;

    public void Update(DockInputs input, TimeSpan now)
    {
        // 1. Fullscreen (without KeepVisible) hides immediately and ignores edge input.
        if (input.Fullscreen && !input.KeepVisible)
        {
            Visible = false;
            _edgeSince = null;
            _idleSince = null;
            return;
        }

        // 2 + 5. KeepVisible and an open panel both pin the dock visible.
        if (input.KeepVisible || input.PanelOpen)
        {
            Visible = true;
            _edgeSince = null;
            _idleSince = null;
            return;
        }

        if (!Visible)
        {
            // 3. Edge held for the reveal delay shows the dock.
            _idleSince = null;
            if (!input.CursorAtEdge)
            {
                _edgeSince = null;
                return;
            }

            _edgeSince ??= now;
            if (now - _edgeSince.Value >= revealDelay)
            {
                Visible = true;
                _edgeSince = null;
            }
            return;
        }

        // 4. Visible: hide after the cursor has been away from dock and edge for the hide delay.
        _edgeSince = null;
        if (input.CursorOverDock || input.CursorAtEdge)
        {
            _idleSince = null;
            return;
        }

        _idleSince ??= now;
        if (now - _idleSince.Value >= hideDelay)
        {
            Visible = false;
            _idleSince = null;
        }
    }
}

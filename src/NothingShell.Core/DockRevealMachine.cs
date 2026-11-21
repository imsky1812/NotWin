namespace NothingShell.Core;

/// <param name="MaximizedFront">A maximized app is in front on the dock's monitor (user, 2026-11-21): a KeepVisible
/// dock then behaves like auto-hide.</param>
public readonly record struct DockInputs(
    bool CursorAtEdge, bool CursorOverDock, bool PanelOpen, bool KeepVisible, bool Fullscreen, bool MaximizedFront = false);

/// <summary>
/// Deterministic dock show/hide state machine. It never reads a clock: the caller passes <c>now</c>
/// to <see cref="Update"/> and should call it again at <see cref="NextDeadline"/> (null = nothing
/// pending, so no timer is needed). Deadlines are inclusive: elapsed >= delay fires.
/// Rule order: 1 Fullscreen hides; 2 KeepVisible / 5 open panel pin visible; 3 edge reveals; 4 idle hides.
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
        // 1. Fullscreen hides immediately (even with KeepVisible or an open panel) and ignores edge input;
        //    the appbar reservation, if any, stays registered. Leaving fullscreen re-evaluates from scratch,
        //    so a KeepVisible dock reappears at once.
        if (input.Fullscreen)
        {
            Visible = false;
            _edgeSince = null;
            _idleSince = null;
            return;
        }

        // 2 + 5. KeepVisible and an open panel both pin the dock visible (rule 1 already handled fullscreen). With a
        //    maximized app in front KeepVisible does not pin (user, 2026-11-21): the edge / idle rules below apply.
        if ((input.KeepVisible && !input.MaximizedFront) || input.PanelOpen)
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

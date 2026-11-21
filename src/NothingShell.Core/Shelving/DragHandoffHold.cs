namespace NothingShell.Core.Shelving;

/// <summary>
/// How long the notch stays click-through while the drag helper takes a shelf drag over (user report 2026-11-21).
/// <para>
/// The shell runs with UI access, so Windows keeps its windows above every normal app's topmost windows: the helper's
/// grip sits under the open notch, the re-sent press landed on the notch, and Windows refused a normal app's input into
/// the raised notch ("no press reached the grip"). While the notch lets clicks through, the press reaches the grip.
/// </para>
/// It becomes clickable again once the button has been seen up on two polls in a row (the helper's own release-and-press
/// is a single blip) and at least a second has passed, or after a minute whatever happens.
/// </summary>
public static class DragHandoffHold
{
    public static readonly TimeSpan Poll = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan Least = TimeSpan.FromSeconds(1), Most = TimeSpan.FromMinutes(1);

    public static bool ShouldRestore(TimeSpan elapsed, int releasedPolls) =>
        elapsed >= Most || (elapsed >= Least && releasedPolls >= 2);
}

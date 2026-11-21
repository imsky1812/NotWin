namespace NotWin.Core;

public static class TaskbarSession
{
    /// <summary>A leftover file means a previous run died before restoring; its value is the true original.</summary>
    public static TaskbarRestoreState ResolveOriginal(TaskbarRestoreState? leftoverFromLastRun, bool currentAutoHide)
        => leftoverFromLastRun ?? new TaskbarRestoreState(currentAutoHide);
}

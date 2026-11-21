using NotWin.Core.Alarms;

namespace NotWin.Alarms;

/// <summary>
/// NotWin's alarms (Plan 9): kept in <c>alarms.json</c>, checked by the app every second (<see cref="Check"/>).
/// Each ring happens once at its minute; a "once" alarm turns itself off; Snooze rings again 5 minutes later. Rings only
/// while the PC is awake and the shell runs. Used on the UI thread. Labels are never logged.
/// </summary>
public sealed class AlarmService(AlarmStore store, Func<DateTime> now)
{
    public static readonly TimeSpan SnoozeFor = TimeSpan.FromMinutes(5);

    /// <summary>A ring more than this late (the PC slept through it) does not sound (review, Plan 9).</summary>
    public static readonly TimeSpan Late = TimeSpan.FromMinutes(5);
    private DateTime _lastCheck = now();
    private readonly List<(Alarm Alarm, DateTime At)> _snoozed = [];

    public IReadOnlyList<Alarm> Alarms { get; private set; } = store.Load();

    /// <summary>The alarm ringing now (null when quiet).</summary>
    public Alarm? RingingNow { get; private set; }

    public event Action? Changed;
    public event Action<Alarm>? Ringing;

    /// <summary>Stop or Snooze ended the ringing.</summary>
    public event Action? Quiet;

    public void Set(IReadOnlyList<Alarm> alarms)
    {
        Alarms = alarms;
        try { store.Save(alarms); }
        catch (Exception e) { Log.Error("saving the alarms failed", e); }
        Changed?.Invoke();
    }

    /// <summary>Re-reads the file (a sync pulled a newer copy).</summary>
    public void Reload()
    {
        Alarms = store.Load();
        Changed?.Invoke();
    }

    public void Check()
    {
        var t = now();
        var cutoff = t - Late;
        var from = _lastCheck > cutoff ? _lastCheck : cutoff;
        var due = AlarmRules.Due(Alarms, from, t);
        var missed = _lastCheck < cutoff ? AlarmRules.Due(Alarms, _lastCheck, cutoff) : [];
        _lastCheck = t;
        if (missed.Count > 0) Log.Info($"alarm: {missed.Count} missed while the PC was asleep or busy");
        var passed = due.Concat(missed).Where(a => a.Days == 0).Select(a => a.Id).ToHashSet();
        if (passed.Count > 0) Set([.. Alarms.Select(a => passed.Contains(a.Id) ? a with { On = false } : a)]);
        _snoozed.RemoveAll(s => s.At < cutoff); // a snooze the PC slept through
        Alarm? ring = due.FirstOrDefault();
        if (ring is null)
        {
            var i = _snoozed.FindIndex(s => s.At <= t);
            if (i >= 0)
            {
                ring = _snoozed[i].Alarm;
                _snoozed.RemoveAt(i);
            }
        }
        if (ring is null) return;
        RingingNow = ring;
        Log.Info($"alarm ringing ({due.Count} due)");
        Ringing?.Invoke(ring);
        Changed?.Invoke();
    }

    public void Snooze()
    {
        if (RingingNow is not { } a) return;
        _snoozed.Add((a, now() + SnoozeFor));
        RingingNow = null;
        Quiet?.Invoke();
        Changed?.Invoke();
    }

    public void Stop()
    {
        if (RingingNow is null) return;
        RingingNow = null;
        Quiet?.Invoke();
        Changed?.Invoke();
    }

    /// <summary>When a snoozed alarm rings again (shown on the widget), or null.</summary>
    public DateTime? SnoozedUntil => _snoozed.Count == 0 ? null : _snoozed.Min(s => s.At);
}

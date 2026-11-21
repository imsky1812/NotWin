using NothingShell.Core.Alarms;

namespace NothingShell.Alarms;

/// <summary>
/// Nothing Shell's alarms (Plan 9): kept in <c>alarms.json</c>, checked by the app every second (<see cref="Check"/>).
/// Each ring happens once at its minute; a "once" alarm turns itself off; Snooze rings again 5 minutes later. Rings only
/// while the PC is awake and the shell runs. Used on the UI thread. Labels are never logged.
/// </summary>
public sealed class AlarmService(AlarmStore store, Func<DateTime> now)
{
    public static readonly TimeSpan SnoozeFor = TimeSpan.FromMinutes(5);
    private DateTime _lastCheck = now();
    private (Alarm Alarm, DateTime At)? _snoozed;

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
        var due = AlarmRules.Due(Alarms, _lastCheck, t);
        _lastCheck = t;
        Alarm? ring = due.FirstOrDefault();
        if (ring is null && _snoozed is { } s && s.At <= t)
        {
            ring = s.Alarm;
            _snoozed = null;
        }
        if (ring is null) return;
        if (due.Any(a => a.Days == 0))
            Set([.. Alarms.Select(a => a.Days == 0 && due.Any(d => d.Id == a.Id) ? a with { On = false } : a)]);
        RingingNow = ring;
        Log.Info($"alarm ringing ({due.Count} due)");
        Ringing?.Invoke(ring);
        Changed?.Invoke();
    }

    public void Snooze()
    {
        if (RingingNow is not { } a) return;
        _snoozed = (a, now() + SnoozeFor);
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
    public DateTime? SnoozedUntil => _snoozed?.At;
}

namespace NothingShell.Core.Widgets;

/// <summary>
/// A device in the battery widget: the laptop or a Bluetooth device. <see cref="Percent"/> is null when the device
/// reports no level (spec §9: those show no ring). <see cref="Icon"/> is one of laptop, head, mouse, speaker.
/// </summary>
public sealed record BatteryDevice(string Id, string Name, string Icon, int? Percent, bool Charging, bool IsLaptop);

/// <summary>
/// The battery widget's pages (spec §6.5): laptop first, then devices by name; the widget jumps to a newly connected
/// device; if the shown device goes away it returns to the laptop page.
/// </summary>
public sealed class BatteryPages
{
    private List<BatteryDevice> _devices = [];

    public IReadOnlyList<BatteryDevice> Devices => _devices;
    public int Current { get; private set; }

    public void Update(IReadOnlyList<BatteryDevice> now)
    {
        var shownId = _devices.Count > 0 ? _devices[Current].Id : null;
        var known = _devices.Select(d => d.Id).ToHashSet();
        var hadAny = _devices.Count > 0;
        _devices = now.OrderByDescending(d => d.IsLaptop).ThenBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        var fresh = hadAny ? _devices.FindIndex(d => !known.Contains(d.Id)) : -1;
        if (fresh >= 0) { Current = fresh; return; }
        var still = shownId is null ? -1 : _devices.FindIndex(d => d.Id == shownId);
        Current = still >= 0 ? still : 0;
    }

    public void Go(int index) => Current = _devices.Count == 0 ? 0 : Math.Clamp(index, 0, _devices.Count - 1);

    public void Scroll(int delta) => Go(Current + Math.Sign(delta));
}

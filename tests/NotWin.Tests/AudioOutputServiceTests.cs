using NotWin.Panel;

namespace NotWin.Tests;

public sealed class AudioOutputServiceTests
{
    [Fact]
    public void SwitchToNext_RereadsDevicesFirst()
    {
        // Opened with speakers + buds; the buds were unplugged while the panel stayed open.
        var devices = new List<(string, string)> { ("spk", "Speakers"), ("buds", "Buds") };
        var current = "buds";
        var set = new List<string>();
        var s = new AudioOutputService(() => devices, () => current, id => { set.Add(id); current = id; return true; });
        s.Refresh();
        devices.RemoveAt(1);
        current = "spk"; // Windows already fell back to the speakers
        var next = s.SwitchToNext();
        Assert.Null(next);   // only one device left: nothing to switch to
        Assert.Empty(set);   // never SetDefault on a vanished device
        Assert.Equal("spk", s.CurrentId);
    }

    [Fact]
    public void SwitchToNext_CyclesWithFreshList()
    {
        var devices = new List<(string, string)> { ("spk", "Speakers"), ("buds", "Buds") };
        var current = "spk";
        var s = new AudioOutputService(() => devices, () => current, id => { current = id; return true; });
        Assert.Equal("buds", s.SwitchToNext()?.Id);
    }
}

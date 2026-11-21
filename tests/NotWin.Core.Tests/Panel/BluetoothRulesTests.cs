using NotWin.Core.Panel;

namespace NotWin.Core.Tests.Panel;

public sealed class BluetoothRulesTests
{
    [Fact]
    public void Order_ConnectedFirstThenName()
    {
        var r = BluetoothRules.Order([
            new BtDevice("3", "Mouse", Connected: false, IsAudio: false),
            new BtDevice("1", "CMF Buds 2", Connected: true, IsAudio: true),
            new BtDevice("2", "Keyboard", Connected: false, IsAudio: false),
            new BtDevice("4", "Speaker", Connected: true, IsAudio: true),
        ]);
        Assert.Equal(["CMF Buds 2", "Speaker", "Keyboard", "Mouse"], r.Select(d => d.Name));
    }

    [Fact]
    public void Order_DropsBlankNames() => Assert.Empty(BluetoothRules.Order([new BtDevice("1", " ", false, false)]));
}

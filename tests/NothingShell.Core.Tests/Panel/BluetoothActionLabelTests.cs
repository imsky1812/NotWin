using NothingShell.Core.Panel;

namespace NothingShell.Core.Tests.Panel;

public sealed class BluetoothActionLabelTests
{
    // The row's right-hand label is what a click does: a connected device's row must say "Disconnect", not "Connected".
    [Fact] public void Connected_SaysDisconnect() => Assert.Equal("Disconnect", BluetoothRules.ActionLabel(new BtDevice("1", "Buds", true, true)));
    [Fact] public void NotConnected_SaysConnect() => Assert.Equal("Connect", BluetoothRules.ActionLabel(new BtDevice("1", "Buds", false, true)));
}

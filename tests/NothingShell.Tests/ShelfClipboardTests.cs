using System.IO;
using System.Windows;
using NothingShell.Notch.Dashboard.Tiles;

namespace NothingShell.Tests;

public sealed class ShelfClipboardTests
{
    private static void OnSta(Action body)
    {
        Exception? error = null;
        var t = new Thread(() => { try { body(); } catch (Exception e) { error = e; } });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
        if (error is not null) throw new Xunit.Sdk.XunitException("on STA: " + error);
    }

    // User, 2026-11-20: a shelf file must paste into WhatsApp etc. Drag-out cannot reach normal apps from this
    // raised process, so a click copies the file itself; "Preferred DropEffect" = copy so a paste never moves it.
    [Fact]
    public void Data_HoldsTheFiles_MarkedAsCopy() => OnSta(() =>
    {
        var data = ShelfClipboard.Data([@"C:\a\photo.jpg", @"C:\b\doc.pdf"]);
        Assert.Equal([@"C:\a\photo.jpg", @"C:\b\doc.pdf"], (string[])data.GetData(DataFormats.FileDrop)!);
        var effect = Assert.IsType<MemoryStream>(data.GetData("Preferred DropEffect"));
        Assert.Equal(1, BitConverter.ToInt32(effect.ToArray(), 0)); // DROPEFFECT_COPY
    });
}

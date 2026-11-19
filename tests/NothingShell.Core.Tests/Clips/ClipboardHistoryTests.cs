using NothingShell.Core.Clips;

namespace NothingShell.Core.Tests.Clips;

public sealed class ClipboardHistoryTests
{
    private static IEnumerable<string?> Texts(ClipboardHistory h) => h.Items.Select(i => i.Text);

    [Fact]
    public void Add_NewestFirst()
    {
        var h = new ClipboardHistory();
        h.Add("a");
        h.Add("b");
        Assert.Equal(["b", "a"], Texts(h));
    }

    [Fact]
    public void Add_Duplicate_MovesToTop()
    {
        var h = new ClipboardHistory();
        h.Add("a"); h.Add("b"); h.Add("c");
        Assert.True(h.Add("a"));
        Assert.Equal(["a", "c", "b"], Texts(h));
    }

    [Fact]
    public void Add_Blank_Ignored()
    {
        var h = new ClipboardHistory();
        Assert.False(h.Add(""));
        Assert.False(h.Add("   \r\n\t"));
        Assert.Empty(h.Items);
    }

    [Fact]
    public void Add_Over10000Chars_Ignored()
    {
        var h = new ClipboardHistory();
        Assert.False(h.Add(new string('x', 10_001)));
        Assert.True(h.Add(new string('x', 10_000)));
        Assert.Single(h.Items);
    }

    [Fact]
    public void Cap_DropsOldest()
    {
        var h = new ClipboardHistory(cap: 3);
        foreach (var s in new[] { "1", "2", "3", "4" }) h.Add(s);
        Assert.Equal(["4", "3", "2"], Texts(h));
        Assert.Equal(50, new ClipboardHistory().Cap);
    }

    [Fact]
    public void Remove_And_Clear()
    {
        var h = new ClipboardHistory();
        h.Add("a"); h.Add("b");
        h.Remove(0);
        Assert.Equal(["a"], Texts(h));
        h.Remove(5); // out of range: ignored
        h.Clear();
        Assert.Empty(h.Items);
    }

    [Fact]
    public void From_Null_Empty()
    {
        Assert.Empty(ClipboardHistory.From(null).Items);
        var h = ClipboardHistory.From([ClipItem.OfText("b"), ClipItem.OfText(""), ClipItem.OfText("a"), ClipItem.OfText("b")]);
        Assert.Equal(["b", "a"], Texts(h)); // stored newest first; blanks and repeats dropped
    }

    [Fact]
    public void AddImage_NewestFirst_RepeatMovesToTop()
    {
        // Screenshots copied by hand (Win+Shift+S, Print Screen) show in the notch history (user 2026-11-19).
        var h = new ClipboardHistory();
        h.Add("text");
        Assert.True(h.AddImage("img1", 1920, 1080));
        h.Add("more");
        Assert.True(h.AddImage("img1", 1920, 1080));
        Assert.Equal(ClipItem.OfImage("img1", 1920, 1080), h.Items[0]);
        Assert.Equal(3, h.Items.Count);
        Assert.True(h.Items[0].IsImage);
    }

    [Fact]
    public void Images_CappedSeparately_DroppedOnesReported()
    {
        var h = new ClipboardHistory(cap: 50, imageCap: 2);
        h.AddImage("a", 1, 1); h.AddImage("b", 1, 1); h.AddImage("c", 1, 1);
        Assert.Equal(["c", "b"], h.Items.Select(i => i.ImageId));
        Assert.Equal(["a"], h.TakeDropped().Select(i => i.ImageId)); // the app deletes a.png
        Assert.Empty(h.TakeDropped());
    }

    [Fact]
    public void RemoveAndClear_ReportDroppedImages()
    {
        var h = new ClipboardHistory();
        h.AddImage("a", 1, 1); h.Add("t"); h.AddImage("b", 1, 1);
        h.Remove(0);
        Assert.Equal(["b"], h.TakeDropped().Select(i => i.ImageId));
        h.Clear();
        Assert.Equal(["a"], h.TakeDropped().Select(i => i.ImageId)); // text entries have no files
    }

    [Fact]
    public void AddImage_BlankId_Ignored() => Assert.False(new ClipboardHistory().AddImage(" ", 1, 1));

    [Fact]
    public void ShouldRecord_PrivateFormats_False()
    {
        Assert.False(ClipboardRules.ShouldRecord(["UnicodeText", "ExcludeClipboardContentFromMonitorProcessing"], null));
        Assert.False(ClipboardRules.ShouldRecord(["UnicodeText", "Clipboard Viewer Ignore"], null));
        Assert.False(ClipboardRules.ShouldRecord(["UnicodeText", "CanIncludeInClipboardHistory"], 0));
        Assert.True(ClipboardRules.ShouldRecord(["UnicodeText", "CanIncludeInClipboardHistory"], 1));
        Assert.True(ClipboardRules.ShouldRecord(["UnicodeText"], null));
    }
}

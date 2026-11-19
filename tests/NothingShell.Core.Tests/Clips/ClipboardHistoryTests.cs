using NothingShell.Core.Clips;

namespace NothingShell.Core.Tests.Clips;

public sealed class ClipboardHistoryTests
{
    [Fact]
    public void Add_NewestFirst()
    {
        var h = new ClipboardHistory();
        h.Add("a");
        h.Add("b");
        Assert.Equal(["b", "a"], h.Items);
    }

    [Fact]
    public void Add_Duplicate_MovesToTop()
    {
        var h = new ClipboardHistory();
        h.Add("a"); h.Add("b"); h.Add("c");
        Assert.True(h.Add("a"));
        Assert.Equal(["a", "c", "b"], h.Items);
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
        Assert.Equal(["4", "3", "2"], h.Items);
        Assert.Equal(50, new ClipboardHistory().Cap);
    }

    [Fact]
    public void Remove_And_Clear()
    {
        var h = new ClipboardHistory();
        h.Add("a"); h.Add("b");
        h.Remove(0);
        Assert.Equal(["a"], h.Items);
        h.Remove(5); // out of range: ignored
        h.Clear();
        Assert.Empty(h.Items);
    }

    [Fact]
    public void From_Null_Empty()
    {
        Assert.Empty(ClipboardHistory.From(null).Items);
        var h = ClipboardHistory.From(["b", "", "a", "b"]); // stored newest first; blanks and repeats dropped
        Assert.Equal(["b", "a"], h.Items);
    }

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

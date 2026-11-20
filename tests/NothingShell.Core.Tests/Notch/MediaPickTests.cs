using NothingShell.Core.Notch;

namespace NothingShell.Core.Tests.Notch;

public sealed class MediaPickTests
{
    private sealed record S(string Name, bool Playing = false, long Since = 0);

    private static S? Pick(IReadOnlyList<S> all, S? previous = null, S? system = null) =>
        MediaPick.Choose(all, s => s.Playing, s => s.Since, previous, system);

    // User report 2026-11-20: Windows' "current" session stayed on Spotify while a YouTube video played.
    [Fact]
    public void ThePlayingSessionWins_OverWindowsCurrent()
    {
        var spotify = new S("spotify");
        var youtube = new S("chrome", Playing: true, Since: 10);
        Assert.Same(youtube, Pick([spotify, youtube], previous: spotify, system: spotify));
    }

    [Fact]
    public void TwoPlaying_KeepsTheOneAlreadyShown()
    {
        var a = new S("a", true, 5);
        var b = new S("b", true, 9);
        Assert.Same(a, Pick([a, b], previous: a)); // no flipping back and forth
    }

    [Fact]
    public void TwoPlaying_NothingShownYet_TheLatestToStartWins()
    {
        var a = new S("a", true, 5);
        var b = new S("b", true, 9);
        Assert.Same(b, Pick([a, b]));
    }

    [Fact]
    public void ShownOneStops_AnotherStillPlaying_SwitchesToIt()
    {
        var a = new S("a", false, 0);
        var b = new S("b", true, 3);
        Assert.Same(b, Pick([a, b], previous: a));
    }

    [Fact]
    public void NothingPlaying_KeepsThePausedOneShown()
    {
        var a = new S("a");
        var b = new S("b");
        Assert.Same(b, Pick([a, b], previous: b, system: a));
    }

    [Fact]
    public void NothingPlaying_ShownOneGone_FallsBackToWindowsCurrent_ThenFirst()
    {
        var a = new S("a");
        var b = new S("b");
        var gone = new S("gone");
        Assert.Same(b, Pick([a, b], previous: gone, system: b));
        Assert.Same(a, Pick([a, b], previous: gone));
        Assert.Null(Pick([]));
    }
}

using NotWin.Core.Notch;

namespace NotWin.Core.Tests.Notch;

public sealed class MediaProgressTests
{
    private static TimeSpan S(double s) => TimeSpan.FromSeconds(s);

    [Fact]
    public void Dots_HalfWay_IsHalf() => Assert.Equal(10, MediaProgress.Dots(S(60), S(120), 20));

    [Fact]
    public void Dots_UnknownDuration_Zero() => Assert.Equal(0, MediaProgress.Dots(S(60), TimeSpan.Zero, 20));

    [Fact]
    public void Dots_PastEnd_Full() => Assert.Equal(20, MediaProgress.Dots(S(500), S(120), 20));

    [Fact]
    public void Clock_Formats()
    {
        Assert.Equal("1:42", MediaProgress.Clock(S(102)));
        Assert.Equal("0:05", MediaProgress.Clock(S(5)));
        Assert.Equal("1:02:03", MediaProgress.Clock(S(3723)));
        Assert.Equal("0:00", MediaProgress.Clock(S(-3)));
    }

    [Fact]
    public void Position_Interpolates_WhilePlaying_CappedAtDuration()
    {
        var at = new DateTimeOffset(2026, 11, 19, 10, 0, 0, TimeSpan.Zero);
        Assert.Equal(S(110), MediaProgress.Position(S(100), S(200), at, playing: true, at.AddSeconds(10)));
        Assert.Equal(S(100), MediaProgress.Position(S(100), S(200), at, playing: false, at.AddSeconds(10)));
        Assert.Equal(S(200), MediaProgress.Position(S(100), S(200), at, playing: true, at.AddSeconds(500)));
    }

    // User, 2026-11-21: the progress dots are draggable. Where the pointer is on the bar = where in the song.
    [Theory]
    [InlineData(0, 100, 0)]
    [InlineData(50, 100, 120)]
    [InlineData(100, 100, 240)]
    [InlineData(-20, 100, 0)]     // dragged past the left end
    [InlineData(150, 100, 240)]   // past the right end
    public void SeekTarget_FromThePointer(double x, double width, int seconds) =>
        Assert.Equal(TimeSpan.FromSeconds(seconds), MediaProgress.SeekTarget(x, width, TimeSpan.FromSeconds(240)));

    [Fact]
    public void SeekTarget_UnknownLengthOrNoBar_IsNull()
    {
        Assert.Null(MediaProgress.SeekTarget(10, 100, TimeSpan.Zero));
        Assert.Null(MediaProgress.SeekTarget(10, 0, TimeSpan.FromSeconds(240)));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(50, 10)]
    [InlineData(100, 20)]
    [InlineData(-5, 0)]
    public void LitAt_PreviewWhileDragging(double x, int lit) => Assert.Equal(lit, MediaProgress.LitAt(x, 100, 20));
}

using NothingShell.Core.Notch;

namespace NothingShell.Core.Tests.Notch;

public sealed class EqualizerModelTests
{
    private static EqualizerModel After(params float[] peaks)
    {
        var m = new EqualizerModel();
        foreach (var p in peaks) m.Step(p);
        return m;
    }

    [Fact]
    public void Level_IsDecibelsBelowTheRecentPeak()
    {
        Assert.Equal(1, EqualizerModel.Level(0.05f, 0.05));
        Assert.Equal(0, EqualizerModel.Level(0f, 0.05));
        Assert.Equal(0, EqualizerModel.Level(0.05f / 16, 0.05), 2);   // 24 dB below the reference: the floor
        Assert.Equal(0.5, EqualizerModel.Level(0.05f / 4, 0.05), 2);  // 12 dB below: half way
    }

    [Fact]
    public void QuietMusic_StillFillsTheBars()
    {
        // Measured live 2026-11-19: Spotify peaks of 0.01-0.05 left the bars on the baseline with a fixed -36 dB scale.
        var m = After(0.03f, 0.021f, 0.009f, 0.015f, 0.041f, 0.033f, 0.05f);
        Assert.True(m.Heights[4] >= 3);
        var spread = m.Heights.Distinct().Count();
        Assert.True(spread >= 2); // the ripple shows the variation, not a flat block
    }

    [Fact]
    public void NearSilence_IsNotAmplified()
    {
        var m = After(0.0005f, 0.0008f, 0.0004f, 0.0006f, 0.0007f, 0.0005f);
        Assert.All(m.Heights, h => Assert.Equal(1, h)); // below the noise floor: baseline only
    }

    [Fact]
    public void Silence_ShowsBaselineOnly_NoRed()
    {
        var m = After(0, 0, 0, 0, 0, 0);
        Assert.All(m.Heights, h => Assert.Equal(1, h));
        Assert.All(Enumerable.Range(0, EqualizerModel.Columns), c => Assert.False(m.RedTop(c)));
    }

    [Fact]
    public void Loud_LightsCentreFirst_ThenRipplesOutward()
    {
        var m = After(1f);
        Assert.Equal(EqualizerModel.Rows, m.Heights[4]);
        Assert.Equal(EqualizerModel.Rows, m.Heights[5]);
        Assert.Equal(1, m.Heights[0]);                          // the edge has not heard it yet
        Assert.True(m.RedTop(4));

        for (var i = 0; i < 4; i++) m.Step(1f);                 // 4 more ticks: the ripple reaches the edges
        Assert.True(m.Heights[0] >= 3);
        Assert.True(m.Heights[9] >= 3);
    }

    [Fact]
    public void Columns_AreMirrored()
    {
        var m = After(1f, 0.2f, 0.6f, 0.05f, 0.9f);
        for (var c = 0; c < EqualizerModel.Columns / 2; c++)
            Assert.Equal(m.Heights[c], m.Heights[EqualizerModel.Columns - 1 - c]);
    }

    [Fact]
    public void Red_OnlyOnTheTallestColumns()
    {
        var m = After(0.3f, 0.3f, 0.3f, 1f);                   // centre loud, the ripple behind it quieter
        var max = m.Heights.Max();
        for (var c = 0; c < EqualizerModel.Columns; c++)
            Assert.Equal(m.Heights[c] == max, m.RedTop(c));
    }

    [Fact]
    public void Release_IsGradual()
    {
        var m = After(1f, 1f, 1f, 1f, 1f, 1f);
        m.Step(0f);
        Assert.True(m.Heights[4] >= 3);                         // one silent tick (50 ms) does not drop it to the floor
        for (var i = 0; i < 10; i++) m.Step(0f);
        Assert.Equal(1, m.Heights[4]);                          // ~0.5 s later it has settled
    }
}

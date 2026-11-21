using NotWin.Core.Panel;

namespace NotWin.Core.Tests.Panel;

public sealed class AudioOutputsTests
{
    private static readonly AudioOutput Spk = new("{spk}", "Speakers (Realtek Audio)");
    private static readonly AudioOutput Buds = new("{buds}", "CMF Buds 2 (Bluetooth)");
    private static readonly AudioOutput Hdmi = new("{hdmi}", "LG HDR 4K (NVIDIA)");

    [Fact]
    public void Next_CyclesToFollowingDevice() => Assert.Equal(Buds, AudioOutputs.Next([Spk, Buds, Hdmi], Spk.Id));

    [Fact]
    public void Next_WrapsAround() => Assert.Equal(Spk, AudioOutputs.Next([Spk, Buds, Hdmi], Hdmi.Id));

    [Fact]
    public void Next_SingleDevice_ReturnsNull() => Assert.Null(AudioOutputs.Next([Spk], Spk.Id));

    [Fact]
    public void Next_CurrentMissing_StartsAtFirst() => Assert.Equal(Spk, AudioOutputs.Next([Spk, Buds], "{gone}"));

    [Fact]
    public void Next_Empty_ReturnsNull() => Assert.Null(AudioOutputs.Next([], null));

    [Theory]
    [InlineData("Speakers (Realtek Audio)", "Speakers")]
    [InlineData("CMF Buds 2", "CMF Buds 2")]
    [InlineData("  ", "Output")]
    public void ShortName_IsTextBeforeFirstParen(string full, string expected) => Assert.Equal(expected, AudioOutputs.ShortName(full));
}

using System.Windows;
using NotWin.Settings;

namespace NotWin.Tests;

public sealed class SettingsLayoutTests
{
    private static readonly Rect Work = new(0, 0, 1536, 816); // a 1920×1080 screen at 125 %, minus nothing

    // User report 2026-11-20: after making many wallpapers the window grew past the top of the screen and its ✕
    // could not be reached. It must never be taller than the work area (minus a margin) and must stay inside it.
    [Fact]
    public void TallContent_IsCappedAndStaysOnScreen()
    {
        var (top, maxHeight) = SettingsLayout.Fit(actualHeight: 2400, Work);
        Assert.Equal(816 - 2 * SettingsLayout.Margin, maxHeight);
        Assert.Equal(Work.Top + SettingsLayout.Margin, top);
    }

    [Fact]
    public void ShortContent_IsCentred()
    {
        var (top, _) = SettingsLayout.Fit(actualHeight: 400, Work);
        Assert.Equal((816 - 400) / 2.0, top);
    }

    [Fact]
    public void WorkAreaNotAtZero_TopStaysInside()
    {
        var work = new Rect(0, 48, 1536, 768); // e.g. a reserved strip at the top
        var (top, maxHeight) = SettingsLayout.Fit(actualHeight: 5000, work);
        Assert.True(top >= work.Top);
        Assert.True(top + maxHeight <= work.Bottom);
    }

    // User 2026-11-27 (screenshot): the Quit button was hidden behind the floating dock. The window keeps clear of it.
    [Fact]
    public void BottomReserve_KeepsTheWholeWindowAboveTheDock()
    {
        const double dock = 84;
        var (top, maxHeight) = SettingsLayout.Fit(actualHeight: 5000, Work, bottomReserve: dock);
        Assert.True(top + maxHeight <= Work.Bottom - dock + 0.001);
        Assert.Equal(816 - 2 * SettingsLayout.Margin - dock, maxHeight);
    }

    // The cards are dealt into two columns of roughly equal height (a shorter, more square window).
    [Fact]
    public void Balance_FillsTheShorterColumn()
    {
        Assert.Equal([0, 1, 0, 1], SettingsLayout.Balance([10, 10, 10, 10]));
        Assert.Equal([0, 1, 1], SettingsLayout.Balance([30, 10, 10])); // the tall one alone, the two short ones beside it
    }
}

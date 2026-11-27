using NotWin.Core.Launcher;

namespace NotWin.Core.Tests.Launcher;

/// <summary>
/// User 2026-11-27: a game installed on D: (no Start Menu shortcut, drive not in the Windows index) could not be found
/// in search or added to a folder. "Add a program from your PC" stores the exe so it launches and is searchable.
/// </summary>
public sealed class CustomAppsTests
{
    private const string Nfs = @"D:\Games\Need for Speed - Payback\NeedForSpeedPayback.exe";

    [Fact]
    public void Add_NamesItFromTheFile()
    {
        var list = CustomApps.Add([], Nfs);
        var app = Assert.Single(list);
        Assert.Equal(Nfs, app.Path);
        Assert.Equal("NeedForSpeedPayback", app.Name);
    }

    [Fact]
    public void Add_IsIdempotent_CaseInsensitive()
    {
        var list = CustomApps.Add(CustomApps.Add([], Nfs), Nfs.ToUpperInvariant());
        Assert.Single(list);
    }

    [Fact]
    public void Add_RejectsBlankOrRelative()
    {
        Assert.Empty(CustomApps.Add([], "   "));
        Assert.Empty(CustomApps.Add([], "game.exe")); // not a full path
    }

    [Fact]
    public void Remove_DropsByPath_CaseInsensitive()
    {
        var list = CustomApps.Add([], Nfs);
        Assert.Empty(CustomApps.Remove(list, Nfs.ToUpperInvariant()));
    }

    [Fact]
    public void ToItem_IsALaunchableAppPointingAtThePath()
    {
        var item = CustomApps.ToItem(new CustomApp(Nfs, "NeedForSpeedPayback"));
        Assert.Equal(LaunchKind.App, item.Kind);
        Assert.Equal(Nfs, item.Id);
        Assert.Equal(Nfs, item.Target); // ShellExecute runs the exe directly
        Assert.Equal("NeedForSpeedPayback", item.Name);
    }

    [Fact]
    public void Add_KeepsAProgramEvenWithoutAnExeExtension()
    {
        // A .lnk or a launcher without .exe is still fine; only blank / relative are refused.
        var list = CustomApps.Add([], @"D:\Games\Launcher\play.lnk");
        Assert.Equal("play", Assert.Single(list).Name);
    }
}

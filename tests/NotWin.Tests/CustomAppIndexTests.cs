using System.IO;
using NotWin.Launcher;

namespace NotWin.Tests;

/// <summary>User 2026-11-27: adding a program from the PC makes it searchable and resolvable for an app folder.</summary>
public sealed class CustomAppIndexTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "nw-prog-" + Guid.NewGuid().ToString("N"));
    private const string Nfs = @"D:\Games\Need for Speed - Payback\NeedForSpeedPayback.exe";

    public CustomAppIndexTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    [Fact]
    public void AddedProgram_IsSearchable_Resolvable_AndPersists()
    {
        var index = new AppIndex(_dir);
        var item = index.AddCustomApp(Nfs);

        Assert.NotNull(item);
        Assert.Equal(Nfs, item!.Target);                                     // launch runs the exe
        Assert.Contains(index.Apps, a => a.Id == Nfs);                       // the folder chooser lists it
        Assert.Contains(index.All, a => a.Id == Nfs);                        // search finds it
        Assert.Equal(Nfs, index.FindApp(Nfs)?.Target);                       // a folder storing it resolves

        Assert.Contains(new AppIndex(_dir).Apps, a => a.Id == Nfs);          // survives a restart (programs.json)
    }

    [Fact]
    public void BlankPath_AddsNothing()
    {
        var index = new AppIndex(_dir);
        Assert.Null(index.AddCustomApp("   "));
        Assert.DoesNotContain(index.Apps, a => a.Name == "");
    }
}

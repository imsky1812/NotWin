using System.Diagnostics;
using System.IO;
using NothingShell.Clips;
using NothingShell.Native;
using NothingShell.Widgets;

namespace NothingShell.Tests;

// Security review (2026-11-21): one test per finding that lives in the app (not the install scripts).
public sealed class SecurityFixTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ns-sec-" + Guid.NewGuid().ToString("N"));

    public SecurityFixTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        // The junction goes first (a recursive delete refuses it), then the rest.
        try { var j = Path.Combine(_dir, "state"); if (Directory.Exists(j)) Directory.Delete(j); } catch (Exception) { }
        try { Directory.Delete(_dir, true); } catch (Exception) { }
    }

    // #2: the elevated watchdog deletes the state file only when it really is that file in that folder.
    [Fact]
    public void SafeDelete_DeletesAPlainFileInItsFolder()
    {
        var file = Path.Combine(_dir, "taskbar-restore.json");
        File.WriteAllText(file, "{}");
        Assert.True(SafeFileNative.DeleteInside(file, _dir));
        Assert.False(File.Exists(file));
    }

    [Fact]
    public void SafeDelete_RefusesWhenTheFolderIsAJunctionElsewhere()
    {
        var target = Path.Combine(_dir, "victim");
        Directory.CreateDirectory(target);
        var victim = Path.Combine(target, "taskbar-restore.json");
        File.WriteAllText(victim, "keep me");
        var junction = Path.Combine(_dir, "state");
        var p = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{junction}\" \"{target}\"") { CreateNoWindow = true, UseShellExecute = false })!;
        p.WaitForExit();
        Assert.True(Directory.Exists(junction));
        Assert.False(SafeFileNative.DeleteInside(Path.Combine(junction, "taskbar-restore.json"), junction));
        Assert.True(File.Exists(victim));
    }

    [Fact]
    public void SafeDelete_MissingFile_IsFalse() => Assert.False(SafeFileNative.DeleteInside(Path.Combine(_dir, "none.json"), _dir));

    // #3: without Explorer nothing is started with this process's raised token.
    [Fact]
    public void Open_ExplorerUnavailable_StartsNothing()
    {
        var before = ShellLinkNative.UnelevatedOpener;
        try
        {
            ShellLinkNative.UnelevatedOpener = (_, _) => false;
            Assert.False(ShellLinkNative.Open(@"C:\Windows\System32\notepad.exe"));
        }
        finally { ShellLinkNative.UnelevatedOpener = before; }
    }

    // #5: photo names never leave the frame's folder.
    [Theory]
    [InlineData(@"\\attacker\s\a.jpg")]
    [InlineData(@"C:\Windows\win.ini")]
    [InlineData(@"..\..\x.jpg")]
    [InlineData("a/b.jpg")]
    [InlineData("")]
    public void PhotoStore_UnsafeName_IsRefused(string name)
    {
        var store = new PhotoStore(_dir);
        Assert.Null(store.PhotoPath("frame1", name));
        Assert.Null(store.ThumbPath("frame1", name));
    }

    [Fact]
    public void PhotoStore_SafeName_StaysInTheFrame()
    {
        var store = new PhotoStore(_dir);
        Assert.Equal(Path.Combine(_dir, "frame1", "abc123.jpg"), store.PhotoPath("frame1", "abc123.jpg"));
        Assert.Null(store.PhotoPath(@"..\up", "abc123.jpg"));
    }

    // Hardening: clipboard image ids from clipboard.json never reach outside the clips folder.
    [Theory]
    [InlineData(@"..\..\evil")]
    [InlineData(@"\\attacker\s\x")]
    [InlineData("")]
    public void Clipboard_UnsafeImageId_IsRefused(string id) => Assert.Null(ClipboardService.SafeImageName(id));

    [Fact]
    public void Clipboard_GeneratedId_IsAccepted() =>
        Assert.Equal("3f2a9c0d1e8b4a7f9c2d1e0f3a4b5c6d.png", ClipboardService.SafeImageName("3f2a9c0d1e8b4a7f9c2d1e0f3a4b5c6d"));

    // Privacy: an exception's message (which can hold a full path) never reaches the log.
    [Fact]
    public void Log_Describe_LeavesTheMessageOut()
    {
        Exception e;
        try { File.ReadAllText(@"C:\Users\me\Secret Folder\private notes.txt"); throw new InvalidOperationException(); }
        catch (Exception ex) { e = ex; }
        var text = Log.Describe(e);
        Assert.DoesNotContain("Secret Folder", text);
        Assert.DoesNotContain("private notes", text);
        Assert.Contains(e.GetType().Name, text);
    }
}

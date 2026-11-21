using NothingShell.Sync;

namespace NothingShell.Tests;

// Plan 10: the status says what really happened: saved into the folder; the cloud app (OneDrive…) uploads it.
public sealed class SyncStatusTests
{
    private static readonly DateTime At = new(2026, 11, 21, 15, 4, 0);
    private static readonly string[] OneDrive = [@"C:\Users\me\OneDrive"];

    [Fact]
    public void Off() => Assert.Equal("OFF", SyncStatus.Text(null, reached: false, null, OneDrive));

    [Fact]
    public void Unreachable() => Assert.Equal("CAN'T REACH THE FOLDER", SyncStatus.Text(@"D:\gone", reached: false, null, OneDrive));

    [Fact]
    public void InOneDrive_SaysOneDriveUploads() =>
        Assert.Equal("SAVED TO THE FOLDER 15:04 · ONEDRIVE UPLOADS IT", SyncStatus.Text(@"C:\Users\me\OneDrive\Sync", true, At, OneDrive));

    [Fact]
    public void ElsewhereFolder_SaysItsSyncAppUploads() =>
        Assert.Equal("SAVED TO THE FOLDER 15:04 · ITS SYNC APP UPLOADS IT", SyncStatus.Text(@"D:\Dropbox\Shell", true, At, OneDrive));

    [Fact]
    public void OneDriveNameAlone_IsNotEnough() =>
        Assert.Equal("SAVED TO THE FOLDER 15:04 · ITS SYNC APP UPLOADS IT", SyncStatus.Text(@"C:\Users\me\OneDriveBackup", true, At, OneDrive));
}

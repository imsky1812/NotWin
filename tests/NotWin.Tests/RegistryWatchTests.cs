using Microsoft.Win32;
using NotWin.Native;

namespace NotWin.Tests;

/// <summary>
/// Plan 13 review: the mic / camera tiles' registry watch. Uses a throwaway HKCU key only.
/// </summary>
public sealed class RegistryWatchTests : IDisposable
{
    private readonly string _root = $@"Software\NotWin.Tests\{Guid.NewGuid():N}";
    private string Key => _root + @"\microphone";

    public RegistryWatchTests()
    {
        using var k = Registry.CurrentUser.CreateSubKey(Key);
        k.SetValue("Value", "Allow");
    }

    public void Dispose() => Registry.CurrentUser.DeleteSubKeyTree(_root, throwOnMissingSubKey: false);

    private static bool Fires(RegistryWatch watch, Action change)
    {
        using var fired = new ManualResetEventSlim(false);
        watch.Changed += () => fired.Set();
        change();
        return fired.Wait(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void Fires_WhenTheValueChanges()
    {
        using var watch = new RegistryWatch(Key);
        Assert.True(Fires(watch, () =>
        {
            using var k = Registry.CurrentUser.OpenSubKey(Key, writable: true)!;
            k.SetValue("Value", "Deny");
        }));
    }

    // Windows writes LastUsedTimeStart / Stop under per-app subkeys every time an app uses the mic: not our values.
    [Fact]
    public void Ignores_PerAppSubkeys()
    {
        using var watch = new RegistryWatch(Key);
        Assert.False(Fires(watch, () =>
        {
            using var app = Registry.CurrentUser.CreateSubKey(Key + @"\SomeApp");
            app.SetValue("LastUsedTimeStart", 12345L, RegistryValueKind.QWord);
        }));
    }

    [Fact]
    public void Dispose_WhileChanging_DoesNotThrow()
    {
        using var k = Registry.CurrentUser.OpenSubKey(Key, writable: true)!;
        for (var i = 0; i < 200; i++)
        {
            var watch = new RegistryWatch(Key);
            watch.Changed += () => Thread.SpinWait(50);
            k.SetValue("Value", i % 2 == 0 ? "Deny" : "Allow");
            watch.Dispose();
            watch.Dispose();
        }
        Thread.Sleep(200); // let any late callback run: an exception on a pool thread would end the test run
    }
}

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using NotWin.Core.Widgets;
using NotWin.Widgets;
using static NotWin.Tests.WidgetSurfaceTests;

namespace NotWin.Tests;

/// <summary>Plan 13 toggle tiles. Nothing here touches the real radios, theme, keep-awake or privacy settings.</summary>
[Collection("WidgetTheme")]
public sealed class ToggleWidgetTests
{
    private sealed class FakeBackend(ToggleState state) : IToggleBackend
    {
        public ToggleState State = state;
        public int Flips;
        public event Action? Changed;
        public string SettingsUri => "ms-settings:fake";
        public Task<ToggleState> ReadAsync() => Task.FromResult(State);
        public Task FlipAsync()
        {
            Flips++;
            State = State == ToggleState.On ? ToggleState.Off : ToggleState.On;
            Changed?.Invoke();
            return Task.CompletedTask;
        }
        public void Dispose() { }
    }

    private static (ToggleWidget Widget, FakeBackend Backend, List<string> Launched) Make(string choice, ToggleState state)
    {
        var backend = new FakeBackend(state);
        var launched = new List<string>();
        var w = new ToggleWidget(_ => backend, launched.Add);
        w.Configure(new WidgetConfig(Choice: choice));
        return (w, backend, launched);
    }

    private static IEnumerable<string> Texts(DependencyObject root)
    {
        if (root is TextBlock t) yield return t.Text;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var s in Texts(VisualTreeHelper.GetChild(root, i))) yield return s;
    }

    private static List<string> Rendered(FrameworkElement e)
    {
        e.Measure(new Size(284, 136));
        e.Arrange(new Rect(0, 0, 284, 136));
        return Texts(e).ToList();
    }

    [Fact]
    public void ToggleWidget_Surfaces() => OnSta(() =>
    {
        // Follow-up (user): on = red; everything else is the theme's card.
        Assert.Equal(Surface.Red, Make("wifi", ToggleState.On).Widget.Surface);
        Assert.Equal(Surface.Red, Make("mic", ToggleState.On).Widget.Surface);
        Assert.Equal(Surface.Dark, Make("wifi", ToggleState.Off).Widget.Surface);
        Assert.Equal(Surface.Dark, Make("mic", ToggleState.Blocked).Widget.Surface);
        Assert.Equal(Surface.Dark, Make("bluetooth", ToggleState.Unavailable).Widget.Surface);
    });

    // Follow-up (user): a blocked mic / camera is a plain card with the slashed icon, labelled Blocked.
    [Fact]
    public void ToggleWidget_PrivacyOff_IsPlainBlocked() => OnSta(() =>
    {
        var (w, _, _) = Make("camera", ToggleState.Off);
        Assert.Equal(Surface.Dark, w.Surface);
        Assert.Equal(["Camera access", "Blocked"], Rendered(w.Build(new WidgetSize(2, 1))));
    });

    [Fact]
    public void ToggleWidget_BuildsBothSizes_ShowsOnlyNameAndState() => OnSta(() =>
    {
        var (w, _, _) = Make("wifi", ToggleState.On);
        Assert.DoesNotContain(Rendered(w.Build(new WidgetSize(1, 1))), s => s.Length > 0);
        Assert.Equal(["Wi-Fi", "On"], Rendered(w.Build(new WidgetSize(2, 1))));
        var (mic, _, _) = Make("mic", ToggleState.Blocked);
        Assert.Equal(["Mic access", "Blocked"], Rendered(mic.Build(new WidgetSize(2, 1))));
    });

    [Fact]
    public void ToggleWidget_ClickFlips_RaisesCardChanged() => OnSta(() =>
    {
        var (w, backend, launched) = Make("dark", ToggleState.Off);
        w.Build(new WidgetSize(1, 1));
        var changed = 0;
        w.CardChanged += () => changed++;
        w.Click();
        Assert.Equal(1, backend.Flips);
        Assert.Equal(Surface.Red, w.Surface);
        Assert.True(changed >= 1);
        Assert.Empty(launched);
    });

    // Plan 13 review: a release that didn't start with a press on the tile (a popup dismissed by pressing outside it,
    // a press elsewhere released over the tile) must never flip a setting.
    private static void Raise(UIElement e, RoutedEvent ev) =>
        e.RaiseEvent(new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0, System.Windows.Input.MouseButton.Left) { RoutedEvent = ev });

    [Fact]
    public void ToggleWidget_ReleaseWithoutPress_DoesNotFlip() => OnSta(() =>
    {
        var (w, backend, _) = Make("wifi", ToggleState.On);
        var hit = w.Build(new WidgetSize(1, 1));
        Raise(hit, UIElement.MouseLeftButtonUpEvent);
        Assert.Equal(0, backend.Flips);
        Raise(hit, UIElement.MouseLeftButtonDownEvent);
        Raise(hit, UIElement.MouseLeftButtonUpEvent);
        Assert.Equal(1, backend.Flips);
        Raise(hit, UIElement.MouseLeftButtonUpEvent); // a second release alone does nothing
        Assert.Equal(1, backend.Flips);
    });

    [Fact]
    public void ToggleWidget_PressThenLeave_ReleaseDoesNotFlip() => OnSta(() =>
    {
        var (w, backend, _) = Make("mic", ToggleState.On);
        var hit = w.Build(new WidgetSize(2, 1));
        Raise(hit, UIElement.MouseLeftButtonDownEvent);
        hit.RaiseEvent(new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0) { RoutedEvent = UIElement.MouseLeaveEvent });
        Raise(hit, UIElement.MouseLeftButtonUpEvent);
        Assert.Equal(0, backend.Flips);
    });

    [Fact]
    public void ToggleWidget_Unavailable_OpensSettings() => OnSta(() =>
    {
        var (w, backend, launched) = Make("bluetooth", ToggleState.Unavailable);
        w.Build(new WidgetSize(1, 1));
        w.Click();
        Assert.Equal(0, backend.Flips);
        Assert.Equal(["ms-settings:fake"], launched);
    });

    [Fact]
    public void ToggleWidget_NewChoice_NewBackend() => OnSta(() =>
    {
        var asked = new List<string>();
        var w = new ToggleWidget(id => { asked.Add(id); return new FakeBackend(ToggleState.Off); }, _ => { });
        w.Configure(new WidgetConfig(Choice: "wifi"));
        w.Configure(new WidgetConfig(Choice: "wifi"));
        w.Configure(new WidgetConfig(Choice: "camera"));
        Assert.Equal(["wifi", "camera"], asked);
    });

    private sealed class Walls
    {
        public string? Current = @"C:\me\beach.jpg";
        public string? Remembered;
        public List<string> Applied = [];
    }

    // The theme is written under a throwaway HKCU key and nothing is broadcast; the wallpaper goes through fakes.
    [Fact]
    public async Task DarkModeToggle_SwitchesWallpaperAndBack()
    {
        var root = $@"Software\NotWin.Tests\{Guid.NewGuid():N}\Personalize";
        const string dark = @"C:\NotWin\Wallpapers\nothing_halftone_dark.png";
        try
        {
            using (var k = Registry.CurrentUser.CreateSubKey(root)) k.SetValue("AppsUseLightTheme", 1, RegistryValueKind.DWord);
            var w = new Walls();
            using var toggle = new DarkModeToggle(new DarkModeWallpaperHost(
                () => w.Current, p => { w.Applied.Add(p); w.Current = p; }, () => w.Remembered, r => w.Remembered = r, dark, _ => true),
                themeKey: root, broadcast: false);
            Assert.Equal(ToggleState.Off, await toggle.ReadAsync());
            await toggle.FlipAsync();
            Assert.Equal(ToggleState.On, await toggle.ReadAsync());
            Assert.Equal([dark], w.Applied);
            Assert.Equal(@"C:\me\beach.jpg", w.Remembered);
            using (var k = Registry.CurrentUser.OpenSubKey(root)!) Assert.Equal(0, k.GetValue("SystemUsesLightTheme"));
            await toggle.FlipAsync();
            Assert.Equal(ToggleState.Off, await toggle.ReadAsync());
            Assert.Equal([dark, @"C:\me\beach.jpg"], w.Applied);
            Assert.Null(w.Remembered);
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(@"Software\NotWin.Tests\" + root.Split('\\')[2], throwOnMissingSubKey: false);
        }
    }

    [Fact]
    public async Task PrivacyToggle_FlipsBothValues()
    {
        var root = $@"Software\NotWin.Tests\{Guid.NewGuid():N}\ConsentStore";
        try
        {
            using (var k = Registry.CurrentUser.CreateSubKey(root + @"\microphone"))
            {
                k.SetValue("Value", "Allow");
                using var np = k.CreateSubKey("NonPackaged");
                np.SetValue("Value", "Allow");
            }
            using var toggle = new PrivacyToggle("microphone", root, () => "Allow");
            Assert.Equal(ToggleState.On, await toggle.ReadAsync());
            await toggle.FlipAsync();
            Assert.Equal(ToggleState.Off, await toggle.ReadAsync());
            using (var k = Registry.CurrentUser.OpenSubKey(root + @"\microphone")!)
            {
                Assert.Equal("Deny", k.GetValue("Value"));
                Assert.Equal("Deny", k.OpenSubKey("NonPackaged")!.GetValue("Value"));
            }
            await toggle.FlipAsync();
            Assert.Equal(ToggleState.On, await toggle.ReadAsync());
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(@"Software\NotWin.Tests\" + root.Split('\\')[2], throwOnMissingSubKey: false);
        }
    }

    // Settings' "let desktop apps use the mic" switch lives in NonPackaged: the tile must hear it change.
    [Fact]
    public void PrivacyToggle_HearsTheDesktopAppsSwitch()
    {
        var root = $@"Software\NotWin.Tests\{Guid.NewGuid():N}\ConsentStore";
        try
        {
            using (var k = Registry.CurrentUser.CreateSubKey(root + @"\microphone"))
            {
                k.SetValue("Value", "Allow");
                using var np = k.CreateSubKey("NonPackaged");
                np.SetValue("Value", "Allow");
            }
            using var toggle = new PrivacyToggle("microphone", root, () => "Allow");
            using var heard = new ManualResetEventSlim(false);
            toggle.Changed += () => heard.Set();
            using (var np = Registry.CurrentUser.OpenSubKey(root + @"\microphone\NonPackaged", writable: true)!) np.SetValue("Value", "Deny");
            Assert.True(heard.Wait(TimeSpan.FromSeconds(2)));
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(@"Software\NotWin.Tests\" + root.Split('\\')[2], throwOnMissingSubKey: false);
        }
    }

    [Fact]
    public async Task PrivacyToggle_MachineDenied_IsBlocked_AndWontFlip()
    {
        var root = $@"Software\NotWin.Tests\{Guid.NewGuid():N}\ConsentStore";
        try
        {
            using var toggle = new PrivacyToggle("webcam", root, () => "Deny");
            Assert.Equal(ToggleState.Blocked, await toggle.ReadAsync());
            await toggle.FlipAsync();
            Assert.Null(Registry.CurrentUser.OpenSubKey(root + @"\webcam"));
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(@"Software\NotWin.Tests\" + root.Split('\\')[2], throwOnMissingSubKey: false);
        }
    }
}

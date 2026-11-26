using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using NotWin.Core.Widgets;
using NotWin.Widgets;
using static NotWin.Tests.WidgetSurfaceTests;

namespace NotWin.Tests;

/// <summary>Plan 13 toggle tiles. Nothing here touches the real radios, theme, keep-awake or privacy settings.</summary>
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
        Assert.Equal(Surface.Light, Make("wifi", ToggleState.On).Widget.Surface);
        Assert.Equal(Surface.Dark, Make("wifi", ToggleState.Off).Widget.Surface);
        Assert.Equal(Surface.Red, Make("mic", ToggleState.Blocked).Widget.Surface);
        Assert.Equal(Surface.Dark, Make("bluetooth", ToggleState.Unavailable).Widget.Surface);
    });

    // Spec: mic / camera "allowed = Light, blocked = Red" — blocked by this user (Off) is red too, labelled Blocked.
    [Fact]
    public void ToggleWidget_PrivacyOff_IsRedBlocked() => OnSta(() =>
    {
        var (w, _, _) = Make("camera", ToggleState.Off);
        Assert.Equal(Surface.Red, w.Surface);
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
        Assert.Equal(Surface.Light, w.Surface);
        Assert.True(changed >= 1);
        Assert.Empty(launched);
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

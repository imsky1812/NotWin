using System.Diagnostics;
using System.Windows;
using NothingShell.Interop;

namespace NothingShell;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var uiAccess = TokenInfo.GetUiAccess() ? 1 : 0;
        Log.Info($"startup uiAccess={uiAccess} pid={Environment.ProcessId}");

        // Placeholder window; replaced by the dock in a later task.
        new Window
        {
            Title = "Nothing Shell",
            Width = 200,
            Height = 80,
            Content = new System.Windows.Controls.TextBlock
            {
                Text = "Nothing Shell",
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        }.Show();
    }
}

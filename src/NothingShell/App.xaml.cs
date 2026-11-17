using System.Diagnostics;
using System.Windows;
using NothingShell.Interop;
using NothingShell.Shell;

namespace NothingShell;

public partial class App : Application
{
    private TaskbarController? _taskbar;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Register crash/exit restore paths before the taskbar is touched.
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            try { _taskbar?.Dispose(); }
            finally { Log.Error("unhandled exception", args.ExceptionObject as Exception); }
        };
        DispatcherUnhandledException += (_, args) =>
        {
            try { _taskbar?.Dispose(); }
            finally { Log.Error("dispatcher unhandled exception", args.Exception); }
            // Not marked handled: the crash continues.
        };
        SessionEnding += (_, _) => _taskbar?.Dispose();

        var uiAccess = TokenInfo.GetUiAccess() ? 1 : 0;
        Log.Info($"startup uiAccess={uiAccess} pid={Environment.ProcessId}");

        _taskbar = new TaskbarController();
        _taskbar.Start();

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

    protected override void OnExit(ExitEventArgs e)
    {
        _taskbar?.Dispose();
        base.OnExit(e);
    }
}

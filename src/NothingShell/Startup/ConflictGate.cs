using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using NothingShell.Core;

namespace NothingShell.Startup;

/// <summary>
/// Blocks startup while a conflicting shell (Fenn) is running. Nothing touches the taskbar until it is gone.
/// </summary>
public static class ConflictGate
{
    /// <summary>True when startup may proceed; false when the user chose to quit.</summary>
    public static bool CheckOrWait()
    {
        while (true)
        {
            var conflict = ConflictDetector.FindConflict(ProcessNames());
            if (conflict is null) return true;
            Log.Info($"conflict: {conflict} is running");
            var dialog = new ConflictWindow($"{conflict} is running. Quit {conflict} to use Nothing Shell's taskbar and notch.");
            dialog.ShowDialog();
            if (!dialog.CheckAgain) return false;
        }
    }

    private static List<string> ProcessNames()
    {
        // Fail open: if the scan itself breaks we must not block startup; TaskbarController keeps its own safety net.
        try
        {
            var procs = Process.GetProcesses();
            var names = new List<string>(procs.Length);
            foreach (var p in procs)
            {
                try { names.Add(p.ProcessName); }
                catch (Exception) { /* process exited or access denied: skip it */ }
                finally { p.Dispose(); }
            }
            return names;
        }
        catch (Exception e)
        {
            Log.Error("conflict scan failed; treating as no conflict", e);
            return [];
        }
    }

    private sealed class ConflictWindow : Window
    {
        public bool CheckAgain { get; private set; }

        public ConflictWindow(string message)
        {
            var res = Application.Current.Resources;
            Title = "Nothing Shell";
            SizeToContent = SizeToContent.WidthAndHeight;
            ResizeMode = ResizeMode.NoResize;
            WindowStyle = WindowStyle.None;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Topmost = true;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            UseLayoutRounding = true;
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
            buttons.Children.Add(MakeButton("Check again", true, () => { CheckAgain = true; Close(); }));
            buttons.Children.Add(MakeButton("Quit Nothing Shell", false, Close));
            var panel = new StackPanel();
            panel.Children.Add(new TextBlock
            {
                Text = "NOTHING SHELL", FontFamily = (FontFamily)res["Font.Grotesk"], FontSize = 11,
                Foreground = (Brush)res["Brush.Dim"], Margin = new Thickness(0, 0, 0, 10),
            });
            panel.Children.Add(new TextBlock
            {
                Text = message, TextWrapping = TextWrapping.Wrap, MaxWidth = 340,
                FontFamily = (FontFamily)res["Font.Geist"], FontSize = 14, Foreground = (Brush)res["Brush.Text"],
            });
            panel.Children.Add(buttons);
            Content = new Border
            {
                CornerRadius = new CornerRadius(20), Padding = new Thickness(22),
                Background = (Brush)res["Brush.Card"],
                BorderBrush = (Brush)res["Brush.Line"], BorderThickness = new Thickness(1),
                Child = panel,
            };
            KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        }

        private static Button MakeButton(string label, bool primary, Action click)
        {
            var res = Application.Current.Resources;
            var b = new Button
            {
                Content = label, Padding = new Thickness(14, 7, 14, 7), Margin = new Thickness(8, 0, 0, 0),
                FontFamily = (FontFamily)res["Font.Geist"], FontSize = 13,
                Foreground = (Brush)res[primary ? "Brush.Ink" : "Brush.Text"],
                Background = (Brush)res[primary ? "Brush.Text" : "Brush.Hover"],
                BorderThickness = new Thickness(0), Cursor = Cursors.Hand,
            };
            var bd = new FrameworkElementFactory(typeof(Border));
            bd.SetValue(Border.CornerRadiusProperty, new CornerRadius(14));
            bd.SetBinding(Border.BackgroundProperty, new Binding("Background") { RelativeSource = RelativeSource.TemplatedParent });
            bd.SetBinding(Border.PaddingProperty, new Binding("Padding") { RelativeSource = RelativeSource.TemplatedParent });
            var cp = new FrameworkElementFactory(typeof(ContentPresenter));
            cp.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            bd.AppendChild(cp);
            b.Template = new ControlTemplate(typeof(Button)) { VisualTree = bd };
            b.Click += (_, _) => click();
            return b;
        }
    }
}

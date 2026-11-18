using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using NothingShell.Core.Launcher;
using NothingShell.Dock;

namespace NothingShell.Launcher;

/// <summary>
/// Icons for launcher items: real (original-colour) icons for apps, files and folders via a per-open
/// <see cref="AppIconCache"/>; a monochrome gear for settings. The cache is dropped when the launcher hides.
/// </summary>
public static class LauncherIcons
{
    private static AppIconCache? s_cache;
    private static ImageSource? s_gear;

    /// <summary>For XAML bindings on a <see cref="LaunchItem"/>.</summary>
    public static IValueConverter Converter { get; } = new ItemConverter();

    public static void BeginSession() => s_cache = new AppIconCache();
    public static void EndSession() => s_cache = null;

    public static ImageSource For(LaunchItem item, int sizePx)
    {
        if (item.Kind == LaunchKind.Setting) return Gear();
        s_cache ??= new AppIconCache();
        return s_cache.Get(item.Id, sizePx);
    }

    private static ImageSource Gear()
    {
        if (s_gear is not null) return s_gear;
        // Line gear (stroke #F2F2F2) drawn once as a frozen DrawingImage.
        var pen = new Pen(new SolidColorBrush(Color.FromRgb(0xF2, 0xF2, 0xF2)), 1.8) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        var g = new GeometryGroup();
        g.Children.Add(new EllipseGeometry(new Point(12, 12), 3, 3));
        for (var i = 0; i < 8; i++)
        {
            var a = i * Math.PI / 4;
            g.Children.Add(new LineGeometry(new Point(12 + 6 * Math.Cos(a), 12 + 6 * Math.Sin(a)), new Point(12 + 9 * Math.Cos(a), 12 + 9 * Math.Sin(a))));
        }
        g.Children.Add(new EllipseGeometry(new Point(12, 12), 6, 6));
        var drawing = new GeometryDrawing(null, pen, g);
        var img = new DrawingImage(drawing);
        img.Freeze();
        return s_gear = img;
    }

    private sealed class ItemConverter : IValueConverter
    {
        public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is LaunchItem item ? For(item, 32) : null;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
    }
}

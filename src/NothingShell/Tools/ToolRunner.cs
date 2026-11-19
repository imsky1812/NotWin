using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Windows;
using System.Windows.Media.Imaging;
using NothingShell.Core.Notch;
using NothingShell.Core.Tools;
using NothingShell.Native;
using NothingShell.Notch;
using SoftwareBitmap = Windows.Graphics.Imaging.SoftwareBitmap;
using BitmapPixelFormat = Windows.Graphics.Imaging.BitmapPixelFormat;
using BitmapAlphaMode = Windows.Graphics.Imaging.BitmapAlphaMode;
using Windows.Media.Ocr;

namespace NothingShell.Tools;

/// <summary>
/// Runs the notch's tool buttons (spec §6.4). Region tools close the notch first (so it is not in the shot), then:
/// Grab text = OCR of a region to the clipboard; Color = a pixel's hex to the clipboard; Snip = the region to the
/// clipboard and a PNG in Pictures\Screenshots (user decision 2026-11-19). Phone opens Phone Link; Draw opens
/// Snipping Tool; Settings opens Nothing Shell's settings. Each result shows as a card in the notch. OCR text is never
/// logged.
/// </summary>
public sealed class ToolRunner(NotchController notch, Action openSettings)
{
    private bool _busy;

    public async void Run(TileKind tool)
    {
        if (_busy) return;
        _busy = true;
        try
        {
            switch (tool)
            {
                case TileKind.GrabText: await GrabTextAsync(); break;
                case TileKind.Color: await ColorAsync(); break;
                case TileKind.Snip: await SnipAsync(); break;
                case TileKind.Phone: Phone(); break;
                case TileKind.Draw: Draw(); break;
                case TileKind.Settings: notch.Close(); openSettings(); break;
            }
        }
        catch (Exception e)
        {
            Log.Error($"tool {tool} failed", e);
            notch.NotifyLocal(Title(tool), "Something went wrong", "settings");
        }
        finally { _busy = false; }
    }

    private static string Title(TileKind k) => k switch
    {
        TileKind.GrabText => "Grab text",
        _ => k.ToString(),
    };

    private async Task<PickResult?> PickAsync(PickMode mode)
    {
        notch.Close();
        await Task.Delay(200); // let the notch shrink out of the shot
        return await RegionPicker.PickAsync(mode);
    }

    private async Task GrabTextAsync()
    {
        var pick = await PickAsync(PickMode.Region);
        if (pick is null) return;
        var engine = OcrEngine.TryCreateFromUserProfileLanguages();
        if (engine is null) { notch.NotifyLocal("Grab text", "No text recognition language installed", "text"); return; }
        using var bitmap = await ToSoftwareBitmapAsync(pick.Shot);
        var result = await engine.RecognizeAsync(bitmap);
        var text = ToolMath.JoinOcrLines(result.Lines.Select(l => l.Text));
        if (text.Length == 0) { notch.NotifyLocal("Grab text", "No text found", "text"); return; }
        if (!SetClipboard(() => Clipboard.SetText(text))) { notch.NotifyLocal("Grab text", "Clipboard is busy", "text"); return; }
        var words = ToolMath.WordCount(text);
        Log.Info($"grab text: {words} words");
        notch.NotifyLocal("Grab text", words == 1 ? "Copied 1 word" : $"Copied {words} words", "text");
    }

    private async Task ColorAsync()
    {
        var pick = await PickAsync(PickMode.Point);
        if (pick?.Color is not { } c) return;
        var hex = ToolMath.Hex(c.R, c.G, c.B);
        if (!SetClipboard(() => Clipboard.SetText(hex))) { notch.NotifyLocal("Color", "Clipboard is busy", "picker"); return; }
        notch.NotifyLocal("Color", hex, "picker");
    }

    private async Task SnipAsync()
    {
        var pick = await PickAsync(PickMode.Region);
        if (pick is null) return;
        var copied = SetClipboard(() => Clipboard.SetImage(pick.Shot));
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Screenshots");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, ToolMath.SnipFileName(DateTime.Now));
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(pick.Shot));
        using (var fs = File.Create(path)) encoder.Save(fs);
        Log.Info($"snip saved ({pick.Rect.Right - pick.Rect.Left}x{pick.Rect.Bottom - pick.Rect.Top}), copied={copied}");
        notch.NotifyLocal("Snip", copied ? "Copied and saved to Screenshots" : "Saved to Screenshots", "region");
    }

    private void Phone()
    {
        notch.Close();
        if (!ShellLinkNative.Open(@"shell:AppsFolder\Microsoft.YourPhone_8wekyb3d8bbwe!App"))
            notch.NotifyLocal("Phone", "Phone Link isn't installed", "phone");
    }

    private void Draw()
    {
        notch.Close();
        if (!ShellLinkNative.Open(@"shell:AppsFolder\Microsoft.ScreenSketch_8wekyb3d8bbwe!App") && !ShellLinkNative.Open("ms-screenclip:"))
            notch.NotifyLocal("Draw", "Snipping Tool isn't available", "draw");
    }

    private static bool SetClipboard(Action set)
    {
        for (var i = 0; i < 3; i++)
        {
            try { set(); return true; }
            catch (System.Runtime.InteropServices.COMException) { Thread.Sleep(20); }
        }
        return false;
    }

    private static async Task<SoftwareBitmap> ToSoftwareBitmapAsync(BitmapSource source)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        using var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
        await stream.WriteAsync(ms.ToArray().AsBuffer());
        stream.Seek(0);
        var decoder = await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(stream);
        return await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
    }
}

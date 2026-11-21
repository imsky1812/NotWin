using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using NothingShell.Core.Clips;

namespace NothingShell.Clips;

/// <summary>
/// The clipboard history the notch shows: text and images (user request 2026-11-19: screenshots copied by hand show
/// too), fed by <see cref="ClipboardWatcher"/>, saved to clipboard.json (1 s after the last change, and on dispose).
/// Images are stored as <c>clips\&lt;hash&gt;.png</c> (hashing and encoding off the UI thread); files of entries that
/// leave the history are deleted, and stray files are cleaned up on start. Counts and sizes are logged, never contents.
/// </summary>
public sealed class ClipboardService : IDisposable
{
    private const long MaxPixels = 40_000_000; // ~ an 8K screenshot; anything larger is not kept
    private readonly ClipboardStore _store;
    private readonly ClipboardWatcher? _watcher;
    private readonly DispatcherTimer _save;
    private readonly string _clipsDir;
    private readonly Dictionary<string, ImageSource> _thumbs = [];
    private readonly Dispatcher _ui = Dispatcher.CurrentDispatcher;
    private bool _disposed;

    public ClipboardService(string dataDir, bool watch = true)
    {
        _store = new ClipboardStore(dataDir);
        _clipsDir = Path.Combine(dataDir, "clips");
        History = ClipboardHistory.From(_store.Load());
        CleanStrayFiles();
        _save = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _save.Tick += (_, _) => { _save.Stop(); Save(); };
        if (!watch) return;
        try
        {
            _watcher = new ClipboardWatcher();
            _watcher.TextCopied += text => { if (History.Add(text)) Touched(); };
            _watcher.ImageCopied += OnImageCopied;
        }
        catch (Exception e) { Log.Error("clipboard watcher failed to start", e); }
    }

    public ClipboardHistory History { get; }

    public event Action? Changed;

    /// <summary>The image's path in the clips folder; ids read back from clipboard.json are checked first (security review).</summary>
    public string ImagePath(string id) => Path.Combine(_clipsDir, SafeImageName(id) ?? "invalid.png");

    /// <summary>"&lt;id&gt;.png" for an id of 1–64 letters and digits, else null (never a path outside the clips folder).</summary>
    public static string? SafeImageName(string? id) =>
        id is { Length: > 0 and <= 64 } && id.All(char.IsAsciiLetterOrDigit) ? id + ".png" : null;

    /// <summary>A small cached thumbnail of a stored image (null if its file is gone).</summary>
    public ImageSource? Thumbnail(string id)
    {
        if (_thumbs.TryGetValue(id, out var t)) return t;
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.DecodePixelHeight = 96;
            bmp.UriSource = new Uri(ImagePath(id));
            bmp.EndInit();
            bmp.Freeze();
            return _thumbs[id] = bmp;
        }
        catch (Exception) { return null; }
    }

    /// <summary>Adds an image the app itself produced (Draw, Snip) without waiting for the watcher.</summary>
    public void AddImage(BitmapSource image) => StoreAsync(image);

    // UI thread: read the clipboard image, then hash + encode in the background, then add on the UI thread.
    private void OnImageCopied()
    {
        BitmapSource? image = null;
        try { image = Clipboard.GetImage(); }
        catch (Exception e) { Log.Error("reading a clipboard image failed", e); }
        if (image is not null) StoreAsync(image);
    }

    private void StoreAsync(BitmapSource image)
    {
        if ((long)image.PixelWidth * image.PixelHeight > MaxPixels) { Log.Info("clipboard image too large, not kept"); return; }
        BitmapSource bgra = image.Format == PixelFormats.Bgra32 ? image : new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
        var (w, h) = (bgra.PixelWidth, bgra.PixelHeight);
        var pixels = new byte[w * h * 4];
        try { bgra.CopyPixels(pixels, w * 4, 0); }
        catch (Exception e) { Log.Error("copying clipboard image pixels failed", e); return; }
        _ = Task.Run(() =>
        {
            try
            {
                var id = Convert.ToHexString(SHA256.HashData(pixels))[..20].ToLowerInvariant();
                var path = ImagePath(id);
                if (!File.Exists(path))
                {
                    Directory.CreateDirectory(_clipsDir);
                    var frozen = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, pixels, w * 4);
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(frozen));
                    var temp = path + ".tmp";
                    using (var fs = File.Create(temp)) encoder.Save(fs);
                    File.Move(temp, path, overwrite: true);
                }
                _ui.BeginInvoke(() =>
                {
                    if (_disposed) return;
                    if (History.AddImage(id, w, h)) Touched();
                    Log.Info($"clipboard image kept ({w}x{h})");
                });
            }
            catch (Exception e) { Log.Error("saving a clipboard image failed", e); }
        });
    }

    /// <summary>Puts an item back on the clipboard; the watcher then moves it to the top.</summary>
    public bool Copy(ClipItem item)
    {
        if (item.IsImage)
        {
            BitmapSource image;
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.UriSource = new Uri(ImagePath(item.ImageId!));
                bmp.EndInit();
                bmp.Freeze();
                image = bmp;
            }
            catch (Exception e) { Log.Error("clipboard image file unreadable", e); return false; }
            return Set(() => Clipboard.SetImage(image));
        }
        return item.Text is { } text && Set(() => Clipboard.SetText(text));
    }

    private static bool Set(Action set)
    {
        try { set(); return true; }
        catch (System.Runtime.InteropServices.COMException)
        {
            Log.Info("clipboard copy-back failed: clipboard busy");
            return false;
        }
    }

    public void Remove(int index)
    {
        History.Remove(index);
        Touched();
    }

    public void Clear()
    {
        History.Clear();
        Touched();
    }

    private void Touched()
    {
        DeleteDropped();
        Changed?.Invoke();
        _save.Stop();
        _save.Start();
    }

    private void DeleteDropped()
    {
        foreach (var gone in History.TakeDropped())
        {
            _thumbs.Remove(gone.ImageId!);
            try { File.Delete(ImagePath(gone.ImageId!)); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
    }

    // PNGs no entry refers to (an entry dropped while the shell was not running, a crash mid-save).
    private void CleanStrayFiles()
    {
        try
        {
            if (!Directory.Exists(_clipsDir)) return;
            var keep = History.Items.Where(i => i.IsImage).Select(i => i.ImageId + ".png").ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var f in Directory.EnumerateFiles(_clipsDir))
                if (!keep.Contains(Path.GetFileName(f))) File.Delete(f);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    private void Save()
    {
        try { _store.Save(History.Items); }
        catch (Exception e) { Log.Error($"saving clipboard history failed (items={History.Items.Count})", e); }
    }

    public void Dispose()
    {
        _disposed = true;
        _watcher?.Dispose();
        if (_save.IsEnabled) { _save.Stop(); Save(); }
    }
}

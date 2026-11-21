using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace NotWin.Widgets;

/// <summary>
/// A photo frame's own copies of the user's photos (Plan 5b): <c>&lt;framesRoot&gt;\&lt;frame id&gt;\&lt;random&gt;.jpg</c>,
/// long side at most 1600 px, JPEG quality 90, plus a 240 px <c>.thumb.jpg</c> for the manage popup. The original
/// file names are never kept or logged. Ids and names that look like paths are refused, so nothing outside the
/// frame's own folder is ever touched.
/// </summary>
public sealed class PhotoStore(string framesRoot)
{
    public const int LongSide = 1600, ThumbSide = 240;

    // Null for a frame id or photo name that could leave the frame's folder (a synced or hand-edited settings file:
    // security review 2026-11-21, #5).
    public string? PhotoPath(string frameId, string name) =>
        ValidId(frameId) && ValidName(name) ? Path.Combine(framesRoot, frameId, name) : null;

    public string? ThumbPath(string frameId, string name) =>
        ValidId(frameId) && ValidName(name) ? Path.Combine(framesRoot, frameId, Path.GetFileNameWithoutExtension(name) + ".thumb.jpg") : null;

    /// <summary>
    /// Copies the files into the frame (off the UI thread). Unreadable files are skipped; of the rest at most
    /// <paramref name="room"/> are kept, oldest first by date taken (else the file's date). Returns the new names in
    /// that order and how many were skipped.
    /// </summary>
    public Task<(IReadOnlyList<string> Added, int Skipped)> ImportAsync(string frameId, IReadOnlyList<string> files, int room = int.MaxValue)
    {
        var done = new TaskCompletionSource<(IReadOnlyList<string>, int)>();
        var thread = new Thread(() =>
        {
            try { done.SetResult(Import(frameId, files, room)); }
            catch (Exception e) { done.SetException(e); }
        }) { IsBackground = true, Name = "photo import" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return done.Task;
    }

    private (IReadOnlyList<string>, int) Import(string frameId, IReadOnlyList<string> files, int room)
    {
        if (!ValidId(frameId)) throw new ArgumentException("bad frame id", nameof(frameId));
        var dir = Path.Combine(framesRoot, frameId);
        Directory.CreateDirectory(dir);
        var made = new List<(string Name, DateTime Taken)>();
        var skipped = 0;
        foreach (var file in files)
        {
            try { made.Add(Copy(dir, file)); }
            catch (Exception) { skipped++; } // unreadable, missing, not an image: counted, never named
        }
        var ordered = made.OrderBy(m => m.Taken).ToList();
        var keep = ordered.Take(Math.Max(0, room)).Select(m => m.Name).ToList();
        foreach (var extra in ordered.Skip(keep.Count))
        {
            Remove(frameId, extra.Name);
            skipped++;
        }
        return (keep, skipped);
    }

    private static (string, DateTime) Copy(string dir, string file)
    {
        BitmapSource source;
        DateTime taken;
        using (var fs = File.OpenRead(file))
        {
            var frame = BitmapDecoder.Create(fs, BitmapCreateOptions.IgnoreColorProfile | BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
            source = Oriented(frame, Orientation(frame));
            taken = DateTaken(frame) ?? File.GetLastWriteTime(file);
        }
        var name = RandomName() + ".jpg";
        Save(Scale(source, LongSide), Path.Combine(dir, name));
        Save(Scale(source, ThumbSide), Path.Combine(dir, Path.GetFileNameWithoutExtension(name) + ".thumb.jpg"));
        return (name, taken);
    }

    // EXIF Orientation (tag 274): phones save portraits as landscape pixels plus this tag, and the copy keeps no
    // metadata, so the rotation is applied to the pixels here. 1 (or none) = as stored.
    private static int Orientation(BitmapFrame frame)
    {
        try
        {
            if (frame.Metadata is BitmapMetadata m && m.GetQuery("/app1/ifd/{ushort=274}") is ushort o) return o;
        }
        catch (Exception) { } // formats without EXIF throw for the query
        return 1;
    }

    private static BitmapSource Oriented(BitmapSource source, int orientation)
    {
        var t = new TransformGroup();
        switch (orientation)
        {
            case 2: t.Children.Add(new ScaleTransform(-1, 1)); break;
            case 3: t.Children.Add(new RotateTransform(180)); break;
            case 4: t.Children.Add(new ScaleTransform(1, -1)); break;
            case 5: t.Children.Add(new RotateTransform(90)); t.Children.Add(new ScaleTransform(-1, 1)); break;
            case 6: t.Children.Add(new RotateTransform(90)); break;
            case 7: t.Children.Add(new RotateTransform(270)); t.Children.Add(new ScaleTransform(-1, 1)); break;
            case 8: t.Children.Add(new RotateTransform(270)); break;
            default: return source;
        }
        return new TransformedBitmap(source, t);
    }

    private static DateTime? DateTaken(BitmapFrame frame)
    {
        try
        {
            if (frame.Metadata is BitmapMetadata m && m.DateTaken is { } text
                && DateTime.TryParse(text, CultureInfo.CurrentCulture, DateTimeStyles.None, out var t))
                return t;
        }
        catch (Exception) { } // some formats throw for unsupported metadata queries
        return null;
    }

    private static BitmapSource Scale(BitmapSource source, int longSide)
    {
        var f = Math.Min(1.0, (double)longSide / Math.Max(source.PixelWidth, source.PixelHeight));
        BitmapSource scaled = f < 1 ? new TransformedBitmap(source, new ScaleTransform(f, f)) : source;
        // JPEG has no alpha and some decoders give odd formats (CMYK, indexed): always encode 24-bit colour.
        return new FormatConvertedBitmap(scaled, PixelFormats.Bgr24, null, 0);
    }

    private static void Save(BitmapSource image, string path)
    {
        var enc = new JpegBitmapEncoder { QualityLevel = 90 };
        enc.Frames.Add(BitmapFrame.Create(image));
        using var fs = File.Create(path);
        enc.Save(fs);
    }

    /// <summary>Deletes one photo and its thumbnail (path-like names are ignored).</summary>
    public void Remove(string frameId, string name)
    {
        if (!ValidId(frameId) || !ValidName(name)) return;
        TryDelete(PhotoPath(frameId, name));
        TryDelete(ThumbPath(frameId, name));
    }

    /// <summary>Deletes the frame's folder (only <c>framesRoot\frameId</c>; path-like ids are refused).</summary>
    public void Delete(string frameId)
    {
        if (!ValidId(frameId)) return;
        var dir = Path.Combine(framesRoot, frameId);
        try { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
        catch (Exception e) { Log.Info($"deleting a photo frame's folder failed: {e.GetType().Name}"); } // no paths in the log
    }

    private static void TryDelete(string? path)
    {
        try { if (path is not null && File.Exists(path)) File.Delete(path); }
        catch (Exception e) { Log.Info($"deleting a frame photo failed: {e.GetType().Name}"); }
    }

    private static bool ValidId(string id) => id.Length is > 0 and <= 32 && id.All(char.IsAsciiLetterOrDigit);

    private static bool ValidName(string name) =>
        name.Length is > 0 and <= 64 && name.All(c => char.IsAsciiLetterOrDigit(c) || c == '.') && !name.StartsWith('.');

    private static string RandomName() => Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant();
}

using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Interop;
using System.Windows.Threading;
using NotWin.Core.Clips;

namespace NotWin.Clips;

/// <summary>
/// Raises <see cref="TextCopied"/> when text lands on the clipboard (spec §7: AddClipboardFormatListener on a
/// message-only window). Copies marked private by password managers are skipped (<see cref="ClipboardRules"/>).
/// A clipboard held open by another app is retried twice, 30 ms apart, then skipped. The text is never logged.
/// </summary>
public sealed class ClipboardWatcher : IDisposable
{
    private const int WmClipboardUpdate = 0x031D;
    private const uint CfUnicodeText = 13;
    private static readonly nint HwndMessage = -3;
    private readonly HwndSource _source;
    private readonly uint _canIncludeFormat;
    private readonly DispatcherTimer _retry;
    private int _tries;
    private bool _disposed;

    public event Action<string>? TextCopied;

    /// <summary>
    /// An image (no text) landed on the clipboard and may be recorded — a screenshot from Win+Shift+S / Print Screen,
    /// Snip, Draw (user request 2026-11-19). Raised after the clipboard is closed; read it with System.Windows.Clipboard.
    /// </summary>
    public event Action? ImageCopied;

    public ClipboardWatcher()
    {
        _source = new HwndSource(new HwndSourceParameters("NotWin.Clipboard") { ParentWindow = HwndMessage, WindowStyle = 0 });
        _source.AddHook(WndProc);
        _canIncludeFormat = RegisterClipboardFormatW("CanIncludeInClipboardHistory");
        _retry = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
        _retry.Tick += (_, _) => { _retry.Stop(); Read(); };
        if (!AddClipboardFormatListener(_source.Handle)) Log.Error($"AddClipboardFormatListener failed ({Marshal.GetLastPInvokeError()})");
    }

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == WmClipboardUpdate && !_disposed)
        {
            _tries = 0;
            _retry.Stop();
            Read();
        }
        return 0;
    }

    private void Read()
    {
        if (_disposed) return;
        if (!OpenClipboard(_source.Handle))
        {
            if (++_tries < 3) _retry.Start();
            return;
        }
        string? text = null;
        var image = false;
        try
        {
            var names = new List<string>();
            uint canInclude = 1;
            var hasCanInclude = false;
            for (var f = EnumClipboardFormats(0); f != 0; f = EnumClipboardFormats(f))
            {
                names.Add(FormatName(f));
                if (f == _canIncludeFormat) hasCanInclude = true;
            }
            if (hasCanInclude && ReadDword(_canIncludeFormat) is { } v) canInclude = v;
            if (ClipboardRules.ShouldRecord(names, hasCanInclude ? canInclude : null))
            {
                if (names.Contains("UnicodeText")) text = ReadText();
                else image = names.Contains("#8") || names.Contains("#17") || names.Contains("#2"); // CF_DIB, CF_DIBV5, CF_BITMAP
            }
        }
        catch (Exception e) { Log.Error("clipboard read failed", e); }
        finally { CloseClipboard(); }
        if (text is not null) TextCopied?.Invoke(text);
        else if (image) ImageCopied?.Invoke();
    }

    private static string FormatName(uint f)
    {
        if (f == CfUnicodeText) return "UnicodeText";
        if (f < 0xC000) return "#" + f;
        var sb = new StringBuilder(256);
        return GetClipboardFormatNameW(f, sb, sb.Capacity) > 0 ? sb.ToString() : "#" + f;
    }

    private static uint? ReadDword(uint format)
    {
        var h = GetClipboardData(format);
        if (h == 0) return null;
        var p = GlobalLock(h);
        if (p == 0) return null;
        try { return GlobalSize(h) >= 4 ? (uint)Marshal.ReadInt32(p) : null; }
        finally { GlobalUnlock(h); }
    }

    private static string? ReadText()
    {
        var h = GetClipboardData(CfUnicodeText);
        if (h == 0) return null;
        var p = GlobalLock(h);
        if (p == 0) return null;
        try { return Marshal.PtrToStringUni(p); }
        finally { GlobalUnlock(h); }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _retry.Stop();
        RemoveClipboardFormatListener(_source.Handle);
        _source.RemoveHook(WndProc);
        _source.Dispose();
    }

    [DllImport("user32.dll", SetLastError = true)] private static extern bool AddClipboardFormatListener(nint hwnd);
    [DllImport("user32.dll")] private static extern bool RemoveClipboardFormatListener(nint hwnd);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool OpenClipboard(nint hwnd);
    [DllImport("user32.dll")] private static extern bool CloseClipboard();
    [DllImport("user32.dll")] private static extern uint EnumClipboardFormats(uint format);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClipboardFormatNameW(uint format, StringBuilder name, int max);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterClipboardFormatW(string name);
    [DllImport("user32.dll")] private static extern nint GetClipboardData(uint format);
    [DllImport("kernel32.dll")] private static extern nint GlobalLock(nint h);
    [DllImport("kernel32.dll")] private static extern bool GlobalUnlock(nint h);
    [DllImport("kernel32.dll")] private static extern nuint GlobalSize(nint h);
}

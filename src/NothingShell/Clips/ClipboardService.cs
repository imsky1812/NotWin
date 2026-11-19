using System.Windows.Threading;
using NothingShell.Core.Clips;

namespace NothingShell.Clips;

/// <summary>
/// The clipboard history the notch shows: fed by <see cref="ClipboardWatcher"/>, loaded from and saved to
/// <see cref="ClipboardStore"/> (1 s after the last change, and on dispose). Counts are logged, never text.
/// </summary>
public sealed class ClipboardService : IDisposable
{
    private readonly ClipboardStore _store;
    private readonly ClipboardWatcher? _watcher;
    private readonly DispatcherTimer _save;

    public ClipboardService(string dataDir, bool watch = true)
    {
        _store = new ClipboardStore(dataDir);
        History = ClipboardHistory.From(_store.Load());
        _save = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _save.Tick += (_, _) => { _save.Stop(); Save(); };
        if (!watch) return;
        try
        {
            _watcher = new ClipboardWatcher();
            _watcher.TextCopied += text => { if (History.Add(text)) Touched(); };
        }
        catch (Exception e) { Log.Error("clipboard watcher failed to start", e); }
    }

    public ClipboardHistory History { get; }

    public event Action? Changed;

    /// <summary>Puts an item back on the clipboard; the watcher then moves it to the top.</summary>
    public bool Copy(string text)
    {
        for (var i = 0; i < 3; i++)
        {
            try
            {
                System.Windows.Clipboard.SetText(text);
                return true;
            }
            catch (System.Runtime.InteropServices.COMException) { Thread.Sleep(20); } // held open by another app
        }
        Log.Info("clipboard copy-back failed: clipboard busy");
        return false;
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
        Changed?.Invoke();
        _save.Stop();
        _save.Start();
    }

    private void Save()
    {
        try { _store.Save(History.Items); }
        catch (Exception e) { Log.Error($"saving clipboard history failed (items={History.Items.Count})", e); }
    }

    public void Dispose()
    {
        _watcher?.Dispose();
        if (_save.IsEnabled) { _save.Stop(); Save(); }
    }
}

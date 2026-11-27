using NotWin.Dock;

namespace NotWin.Panel;

/// <summary>
/// Opens, closes and toggles the quick-settings panel (Win+A / Win+N / dock tray click). The dock stays up while the
/// panel is open. Section controllers subscribe to <see cref="Opening"/> to refresh their data each time it opens.
/// </summary>
public sealed class QuickPanelController : IDisposable
{
    private readonly DockReveal? _reveal;
    private bool _disposed;

    public QuickPanelController(DockReveal? reveal)
    {
        _reveal = reveal;
        Window = new QuickPanelWindow();
        Window.Dismissed += Close;
    }

    public QuickPanelWindow Window { get; }

    /// <summary>Raised on the UI thread just before the panel shows (sections refresh here).</summary>
    public event Action? Opening;

    /// <summary>Raised after the panel hid.</summary>
    public event Action? Closed;

    public bool IsOpen => Window.IsVisible && !Window.Hiding; // fading out counts as closed

    public void Toggle()
    {
        if (IsOpen) Close();
        else Open();
    }

    public void Open()
    {
        if (_disposed || IsOpen) return;
        try { Opening?.Invoke(); }
        catch (Exception e) { Log.Error("panel section refresh failed", e); }
        _reveal?.SetPanelOpen("panel", true);
        Window.ShowAndFocus();
        Log.Info("panel opened");
    }

    public void Close()
    {
        if (!IsOpen) return;
        Window.HideNow();
        _reveal?.SetPanelOpen("panel", false);
        try { Closed?.Invoke(); }
        catch (Exception e) { Log.Error("panel close handler failed", e); }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { Window.CloseForGood(); } catch { }
    }
}

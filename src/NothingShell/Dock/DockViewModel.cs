using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace NothingShell.Dock;

/// <summary>State the dock window renders. <see cref="IsVisible"/> is driven by <see cref="DockReveal"/>.</summary>
public sealed class DockViewModel : INotifyPropertyChanged
{
    private bool _isVisible;

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool IsVisible
    {
        get => _isVisible;
        set
        {
            if (_isVisible == value) return;
            _isVisible = value;
            OnPropertyChanged();
        }
    }

    /// <summary>Pinned and running apps (see <see cref="DockController"/>).</summary>
    public ObservableCollection<DockItemViewModel> Items { get; } = [];

    /// <summary>Tray values (clock, Wi-Fi, volume, battery). Created on the UI thread; started by App.</summary>
    public TrayStatusService Tray { get; } = new();

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

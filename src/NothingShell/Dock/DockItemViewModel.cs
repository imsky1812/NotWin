using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using NothingShell.Core;

namespace NothingShell.Dock;

/// <summary>One dock slot. Kept alive across refreshes (updated in place) so hover state and the icon survive.</summary>
public sealed class DockItemViewModel : INotifyPropertyChanged
{
    private DockItem _item;
    private ImageSource _icon;

    public DockItemViewModel(DockItem item, ImageSource icon)
    {
        _item = item;
        _icon = icon;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public DockItem Item => _item;
    public string AppId => _item.AppId;
    public string DisplayName => _item.DisplayName;
    public bool Running => _item.Running;
    public bool Focused => _item.Focused;
    public ImageSource Icon => _icon;

    public void Update(DockItem item, ImageSource icon)
    {
        _item = item;
        if (!ReferenceEquals(_icon, icon)) { _icon = icon; Raise(nameof(Icon)); }
        Raise(nameof(Item)); Raise(nameof(DisplayName)); Raise(nameof(Running)); Raise(nameof(Focused));
    }

    private void Raise([CallerMemberName] string? n = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

using System.ComponentModel;
using System.Runtime.CompilerServices;
using NothingShell.Core.Launcher;

namespace NothingShell.Launcher;

public enum LauncherView { Home, Results, AllApps }

/// <summary>Where the launcher's lists come from (the app wires these to <see cref="AppIndex"/> and <see cref="FileSearchService"/>).</summary>
public sealed record LauncherSources(
    Func<IReadOnlyList<LaunchItem>> Searchable,
    Func<IReadOnlyList<LaunchItem>> MostUsed,
    Func<IReadOnlyList<LaunchItem>> AllApps,
    Func<IReadOnlyDictionary<string, LaunchUsage>> Usage,
    Func<string, CancellationToken, Task<IReadOnlyList<LaunchItem>>> FileSearch);

/// <summary>
/// The launcher's state. Apps and settings rank synchronously on every keystroke; file rows arrive later
/// (debounced, cancelled on the next keystroke) and are dropped if the query changed meanwhile. File rows are
/// last in the ranking, so they never push apps or settings around; the selection stays on the same item.
/// </summary>
public sealed class LauncherViewModel : INotifyPropertyChanged
{
    private readonly LauncherSources _src;
    private readonly TimeSpan _fileDebounce;
    private string _query = "";
    private LauncherView _view = LauncherView.Home;
    private IReadOnlyList<LaunchItem> _results = [];
    private IReadOnlyList<LaunchItem> _mostUsed = [];
    private IReadOnlyList<LaunchItem> _allApps = [];
    private IReadOnlyList<LaunchItem> _files = [];
    private int _selected = -1;
    private int _version;
    private bool _pointerMoved;
    private CancellationTokenSource? _fileCts;

    public LauncherViewModel(LauncherSources sources, TimeSpan fileDebounce)
    {
        _src = sources;
        _fileDebounce = fileDebounce;
        _mostUsed = sources.MostUsed();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Raised by <see cref="ActivateSelected"/> (Enter) or a click on an item.</summary>
    public event Action<LaunchItem>? LaunchRequested;

    /// <summary>The latest file search (completes after its results were merged or dropped). For tests and diagnostics.</summary>
    public Task PendingFileSearch { get; private set; } = Task.CompletedTask;

    public string Query
    {
        get => _query;
        set
        {
            value ??= "";
            if (_query == value) return;
            _query = value;
            OnChanged();
            OnQueryChanged();
        }
    }

    public LauncherView View { get => _view; private set => Set(ref _view, value); }
    public IReadOnlyList<LaunchItem> Results { get => _results; private set => Set(ref _results, value); }
    public IReadOnlyList<LaunchItem> MostUsed { get => _mostUsed; private set => Set(ref _mostUsed, value); }
    public IReadOnlyList<LaunchItem> AllApps { get => _allApps; private set => Set(ref _allApps, value); }

    public int Selected
    {
        get => _selected;
        private set
        {
            if (Set(ref _selected, value)) OnChanged(nameof(SelectedItem));
        }
    }

    public LaunchItem? SelectedItem
    {
        get
        {
            var list = CurrentList;
            return _selected >= 0 && _selected < list.Count ? list[_selected] : null;
        }
    }

    private IReadOnlyList<LaunchItem> CurrentList => _view switch
    {
        LauncherView.Results => _results,
        LauncherView.AllApps => _allApps,
        _ => _mostUsed,
    };

    public void MoveSelection(int delta)
    {
        var count = CurrentList.Count;
        if (count == 0) { Selected = -1; return; }
        var start = _selected < 0 ? (delta > 0 ? -1 : 0) : _selected;
        Selected = Math.Clamp(start + delta, 0, count - 1);
    }

    /// <summary>Selects <paramref name="item"/> in the current list.</summary>
    public void Select(LaunchItem item)
    {
        var i = IndexOf(CurrentList, item.Id);
        if (i >= 0) Selected = i;
    }

    /// <summary>
    /// Mouse hover selection: ignored until the pointer has actually moved since the launcher opened, so a cursor
    /// that happens to rest over a tile does not pick an app the user never chose (Enter would launch it).
    /// </summary>
    public void HoverSelect(LaunchItem item)
    {
        if (_pointerMoved) Select(item);
    }

    /// <summary>The mouse moved since the launcher opened (the window reports it).</summary>
    public void PointerMoved() => _pointerMoved = true;

    public void ActivateSelected()
    {
        if (SelectedItem is { } item) LaunchRequested?.Invoke(item);
    }

    public void Activate(LaunchItem item) => LaunchRequested?.Invoke(item);

    public void ShowAllApps()
    {
        AllApps = _src.AllApps().OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase).ToList();
        View = LauncherView.AllApps;
        Selected = -1; // nothing highlighted until ↑/↓ or hover, like Home
    }

    /// <summary>All apps → Home. False when there is nothing to go back from (Esc then closes the launcher).</summary>
    public bool Back()
    {
        if (_view != LauncherView.AllApps) return false;
        AllApps = [];
        View = LauncherView.Home;
        Selected = -1;
        return true;
    }

    /// <summary>Back to a fresh Home (called when the launcher hides or opens).</summary>
    public void Reset()
    {
        CancelFiles();
        _version++;
        _pointerMoved = false;
        _query = "";
        OnChanged(nameof(Query));
        _files = [];
        Results = [];
        AllApps = [];
        MostUsed = _src.MostUsed();
        View = LauncherView.Home;
        Selected = -1;
    }

    private void OnQueryChanged()
    {
        CancelFiles();
        var version = ++_version;
        _files = [];
        if (string.IsNullOrWhiteSpace(_query))
        {
            Results = [];
            View = LauncherView.Home;
            Selected = -1;
            PendingFileSearch = Task.CompletedTask;
            return;
        }

        Rerank(keepSelectionId: null);
        View = LauncherView.Results;
        var cts = _fileCts = new CancellationTokenSource();
        PendingFileSearch = FetchFilesAsync(_query, version, cts.Token);
    }

    private async Task FetchFilesAsync(string query, int version, CancellationToken ct)
    {
        try
        {
            if (_fileDebounce > TimeSpan.Zero) await Task.Delay(_fileDebounce, ct);
            var files = await _src.FileSearch(query, ct);
            if (version != _version || ct.IsCancellationRequested) return; // stale: the query changed meanwhile
            _files = files;
            Rerank(keepSelectionId: SelectedItem?.Id);
        }
        catch (OperationCanceledException) { }
        catch (Exception e) { Log.Error("launcher file results failed", e); }
    }

    private void Rerank(string? keepSelectionId)
    {
        Results = SearchRanker.Rank(_query, _src.Searchable().Concat(_files), _src.Usage());
        var i = keepSelectionId is null ? -1 : IndexOf(Results, keepSelectionId);
        Selected = i >= 0 ? i : (Results.Count > 0 ? 0 : -1);
        OnChanged(nameof(SelectedItem));
    }

    private void CancelFiles()
    {
        _fileCts?.Cancel();
        _fileCts?.Dispose();
        _fileCts = null;
    }

    private static int IndexOf(IReadOnlyList<LaunchItem> list, string id)
    {
        for (var i = 0; i < list.Count; i++)
            if (string.Equals(list[i].Id, id, StringComparison.OrdinalIgnoreCase)) return i;
        return -1;
    }

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnChanged(name);
        return true;
    }

    private void OnChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

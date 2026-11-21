using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.Media.Control;

namespace NothingShell.Media;

/// <summary>What the notch shows for media: playing or not, plus title/artist/art (art null = grey placeholder).</summary>
public sealed record MediaSnapshot(bool Playing, string? Title, string? Artist, string? SourceAppId, ImageSource? Art)
{
    public static readonly MediaSnapshot None = new(false, null, null, null, null);
}

/// <summary>The current media session, abstracted so tests do not need WinRT.</summary>
public interface ISessionSource
{
    bool PlaybackPlaying { get; }
    string? Title { get; }
    string? Artist { get; }
    string? AppId { get; }
    Task<Stream?> ThumbnailAsync();
    event Action? Changed;
}

public interface ISessionManager
{
    ISessionSource? Current { get; }
    event Action? CurrentChanged;
}

/// <summary>
/// Follows the system's current media session (GlobalSystemMediaTransportControlsSessionManager). Music shows in
/// the notch only while the current session is Playing (spec §6.4). <see cref="Changed"/> is raised on the context
/// <see cref="StartAsync"/> was called on (the UI thread in the app). Logs the source app only, never titles.
/// </summary>
public sealed class MediaSessionService : IDisposable
{
    private readonly Func<Task<ISessionManager?>> _manager;
    private readonly Func<Stream, ImageSource?> _decodeArt;
    private SynchronizationContext? _context;
    private ISessionManager? _mgr;
    private ISessionSource? _session;
    private Task _refresh = Task.CompletedTask;
    private readonly object _gate = new();
    private bool _disposed;

    public MediaSessionService(Func<Task<ISessionManager?>>? manager = null, Func<Stream, ImageSource?>? decodeArt = null)
    {
        _manager = manager ?? WinRtSessionManager.CreateAsync;
        _decodeArt = decodeArt ?? DecodeArt;
    }

    public event Action<MediaSnapshot>? Changed;

    public MediaSnapshot Current { get; private set; } = MediaSnapshot.None;

    public async Task StartAsync()
    {
        _context = SynchronizationContext.Current;
        try { _mgr = await _manager(); }
        catch (Exception e) { Log.Error("media session manager unavailable", e); return; }
        if (_mgr is null) return;
        _mgr.CurrentChanged += OnCurrentChanged;
        OnCurrentChanged();
        await Idle();
    }

    /// <summary>Completes when the latest refresh has finished (tests, diagnostics).</summary>
    public Task Idle() { lock (_gate) return _refresh; }

    private void OnCurrentChanged()
    {
        if (_disposed) return;
        var next = _mgr?.Current;
        if (!ReferenceEquals(next, _session))
        {
            if (_session is not null) _session.Changed -= Refresh;
            _session = next;
            if (_session is not null) _session.Changed += Refresh;
            Log.Info($"media session: {_session?.AppId ?? "(none)"}");
        }
        Refresh();
    }

    private void Refresh()
    {
        lock (_gate) _refresh = _refresh.ContinueWith(_ => RefreshCoreAsync(), TaskScheduler.Default).Unwrap();
    }

    private async Task RefreshCoreAsync()
    {
        if (_disposed) return;
        var s = _session;
        MediaSnapshot snap;
        try
        {
            if (s is null) snap = MediaSnapshot.None;
            else
            {
                ImageSource? art = null;
                if (s.PlaybackPlaying)
                {
                    using var stream = await s.ThumbnailAsync();
                    if (stream is not null) art = _decodeArt(stream);
                }
                snap = new MediaSnapshot(s.PlaybackPlaying, s.Title, s.Artist, s.AppId, art);
            }
        }
        catch (Exception e)
        {
            Log.Error("media session read failed", e);
            snap = MediaSnapshot.None;
        }
        if (snap.Playing == Current.Playing && snap.Title == Current.Title && snap.SourceAppId == Current.SourceAppId
            && ReferenceEquals(snap.Art, Current.Art) && snap.Art is null) return; // nothing visible changed
        Current = snap;
        if (_context is null) { Changed?.Invoke(snap); return; }
        // Deliver on the starting context and finish this refresh only once it ran, so Idle() covers delivery.
        var delivered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _context.Post(_ =>
        {
            try { Changed?.Invoke(snap); }
            finally { delivered.TrySetResult(); }
        }, null);
        await delivered.Task;
    }

    private static ImageSource? DecodeArt(Stream stream)
    {
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.DecodePixelWidth = 44; // 22 DIP at up to 200%
            bmp.StreamSource = stream;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch { return null; }
    }

    public void Dispose()
    {
        _disposed = true;
        if (_mgr is not null) _mgr.CurrentChanged -= OnCurrentChanged;
        if (_session is not null) _session.Changed -= Refresh;
    }

    // ---- WinRT adapters ----

    private sealed class WinRtSessionManager : ISessionManager
    {
        private readonly GlobalSystemMediaTransportControlsSessionManager _m;
        private WinRtSession? _current;

        private WinRtSessionManager(GlobalSystemMediaTransportControlsSessionManager m)
        {
            _m = m;
            _m.CurrentSessionChanged += (_, _) => { _current = Wrap(); CurrentChanged?.Invoke(); };
            _current = Wrap();
        }

        public static async Task<ISessionManager?> CreateAsync() =>
            new WinRtSessionManager(await GlobalSystemMediaTransportControlsSessionManager.RequestAsync());

        private WinRtSession? Wrap() => _m.GetCurrentSession() is { } s ? new WinRtSession(s) : null;

        public ISessionSource? Current => _current;
        public event Action? CurrentChanged;
    }

    private sealed class WinRtSession : ISessionSource
    {
        private readonly GlobalSystemMediaTransportControlsSession _s;
        private GlobalSystemMediaTransportControlsSessionMediaProperties? _props;

        public WinRtSession(GlobalSystemMediaTransportControlsSession s)
        {
            _s = s;
            _s.PlaybackInfoChanged += (_, _) => Changed?.Invoke();
            _s.MediaPropertiesChanged += (_, _) => { _props = null; Changed?.Invoke(); };
        }

        public bool PlaybackPlaying =>
            _s.GetPlaybackInfo()?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;

        public string? Title => Props()?.Title;
        public string? Artist => Props()?.Artist;
        public string? AppId => _s.SourceAppUserModelId;

        private GlobalSystemMediaTransportControlsSessionMediaProperties? Props()
        {
            try { return _props ??= _s.TryGetMediaPropertiesAsync().AsTask().GetAwaiter().GetResult(); }
            catch { return null; }
        }

        public async Task<Stream?> ThumbnailAsync()
        {
            var thumb = Props()?.Thumbnail;
            if (thumb is null) return null;
            var ras = await thumb.OpenReadAsync();
            return ras.AsStreamForRead();
        }

        public event Action? Changed;
    }
}

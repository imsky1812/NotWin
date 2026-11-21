using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NothingShell.Core.Notch;
using Windows.Media.Control;

namespace NothingShell.Media;

/// <summary>What the notch shows for media: playing or not, plus title/artist/art (art null = grey placeholder).</summary>
public sealed record MediaSnapshot(bool Playing, string? Title, string? Artist, string? SourceAppId, ImageSource? Art)
{
    public static readonly MediaSnapshot None = new(false, null, null, null, null);

    /// <summary>Timeline as last reported by the session (Plan 4 media tile); interpolate with MediaProgress.Position.</summary>
    public TimeSpan Position { get; init; }
    public TimeSpan Duration { get; init; }
    public DateTimeOffset PositionAt { get; init; }
}

/// <summary>The current media session, abstracted so tests do not need WinRT.</summary>
public interface ISessionSource
{
    bool PlaybackPlaying { get; }
    string? Title { get; }
    string? Artist { get; }
    string? AppId { get; }
    Task<Stream?> ThumbnailAsync();
    (TimeSpan Position, TimeSpan Duration, DateTimeOffset At) Timeline { get; }
    Task<bool> PlayPauseAsync();
    Task<bool> NextAsync();
    Task<bool> PreviousAsync();
    event Action? Changed;
}

public interface ISessionManager
{
    ISessionSource? Current { get; }
    event Action? CurrentChanged;
}

/// <summary>
/// Follows the media session chosen from every app that reports to Windows' media controls
/// (GlobalSystemMediaTransportControlsSessionManager; see MediaPick). Music shows in the notch only while that
/// session is Playing (spec §6.4). <see cref="Changed"/> is raised on the context
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

    /// <summary>Transport controls for the current session (media tile). False without a session or on failure.</summary>
    public Task<bool> PlayPauseAsync() => Control(s => s.PlayPauseAsync(), "play/pause");

    public Task<bool> NextAsync() => Control(s => s.NextAsync(), "next");

    public Task<bool> PreviousAsync() => Control(s => s.PreviousAsync(), "previous");

    private async Task<bool> Control(Func<ISessionSource, Task<bool>> act, string what)
    {
        var s = _session;
        if (s is null) return false;
        try { return await act(s); }
        catch (Exception e)
        {
            Log.Error($"media {what} failed", e);
            return false;
        }
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
                // Art also while paused (the media tile shows a paused track); reused while the track is the same.
                var title = s.Title;
                var artist = s.Artist;
                ImageSource? art = null;
                if (Current.Art is not null && Current.Title == title && Current.Artist == artist && Current.SourceAppId == s.AppId)
                    art = Current.Art;
                else
                {
                    using var stream = await s.ThumbnailAsync();
                    if (stream is not null) art = _decodeArt(stream);
                }
                var (position, duration, at) = s.Timeline;
                snap = new MediaSnapshot(s.PlaybackPlaying, title, artist, s.AppId, art)
                {
                    Position = position, Duration = duration, PositionAt = at,
                };
            }
        }
        catch (Exception e)
        {
            Log.Error("media session read failed", e);
            snap = MediaSnapshot.None;
        }
        if (snap == Current) return; // nothing visible changed (art is reused, so equal tracks compare equal)
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
            bmp.DecodePixelWidth = 120; // the media tile's 54 DIP art at up to 200%
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

    // Every app that reports to Windows' media controls (Spotify, a browser playing YouTube, …), not only Windows'
    // "current" session, which can stay on a paused app while another plays (user report 2026-11-20). The session to
    // follow is chosen by MediaPick: the playing one, the shown one kept while it plays, else the last to start.
    private sealed class WinRtSessionManager : ISessionManager
    {
        private readonly GlobalSystemMediaTransportControlsSessionManager _m;
        private readonly object _lock = new();
        private readonly Dictionary<WinRtSession, long> _since = []; // when each playing session started playing
        private List<WinRtSession> _all = [];
        private WinRtSession? _current;

        private WinRtSessionManager(GlobalSystemMediaTransportControlsSessionManager m)
        {
            _m = m;
            _m.SessionsChanged += (_, _) => Guard(Resync);
            _m.CurrentSessionChanged += (_, _) => Guard(Reevaluate);
            Resync();
        }

        public static async Task<ISessionManager?> CreateAsync() =>
            new WinRtSessionManager(await GlobalSystemMediaTransportControlsSessionManager.RequestAsync());

        public ISessionSource? Current { get { lock (_lock) return _current; } }
        public event Action? CurrentChanged;

        private void Resync()
        {
            lock (_lock)
            {
                var next = new List<WinRtSession>();
                foreach (var raw in _m.GetSessions())
                {
                    var known = _all.FirstOrDefault(w => w.Is(raw));
                    if (known is null)
                    {
                        known = new WinRtSession(raw);
                        known.Changed += () => Guard(() => OnSessionChanged(known));
                        if (known.PlaybackPlaying) _since[known] = Environment.TickCount64;
                    }
                    next.Add(known);
                }
                foreach (var gone in _all.Where(w => !next.Contains(w)))
                {
                    gone.Detach();
                    _since.Remove(gone);
                }
                _all = next;
            }
            Reevaluate();
        }

        private void OnSessionChanged(WinRtSession session)
        {
            lock (_lock)
            {
                if (!_all.Contains(session)) return;
                if (!session.PlaybackPlaying) _since.Remove(session);
                else if (!_since.ContainsKey(session)) _since[session] = Environment.TickCount64;
            }
            Reevaluate();
        }

        private void Reevaluate()
        {
            bool changed;
            lock (_lock)
            {
                var systemRaw = _m.GetCurrentSession();
                var system = systemRaw is null ? null : _all.FirstOrDefault(w => w.Is(systemRaw));
                var choice = MediaPick.Choose(_all, w => w.PlaybackPlaying, w => _since.GetValueOrDefault(w), _current, system);
                changed = !ReferenceEquals(choice, _current);
                _current = choice;
            }
            if (changed) CurrentChanged?.Invoke();
        }

        private static void Guard(Action a)
        {
            try { a(); }
            catch (Exception e) { Log.Error("media sessions update failed", e); }
        }
    }

    private sealed class WinRtSession : ISessionSource
    {
        private readonly GlobalSystemMediaTransportControlsSession _s;
        private readonly Windows.Foundation.TypedEventHandler<GlobalSystemMediaTransportControlsSession, PlaybackInfoChangedEventArgs> _onPlayback;
        private readonly Windows.Foundation.TypedEventHandler<GlobalSystemMediaTransportControlsSession, MediaPropertiesChangedEventArgs> _onProps;
        private readonly Windows.Foundation.TypedEventHandler<GlobalSystemMediaTransportControlsSession, TimelinePropertiesChangedEventArgs> _onTimeline;
        private GlobalSystemMediaTransportControlsSessionMediaProperties? _props;

        public WinRtSession(GlobalSystemMediaTransportControlsSession s)
        {
            _s = s;
            _onPlayback = (_, _) => Changed?.Invoke();
            _onProps = (_, _) => { _props = null; Changed?.Invoke(); };
            _onTimeline = (_, _) => Changed?.Invoke();
            _s.PlaybackInfoChanged += _onPlayback;
            _s.MediaPropertiesChanged += _onProps;
            _s.TimelinePropertiesChanged += _onTimeline;
        }

        public bool Is(GlobalSystemMediaTransportControlsSession other) => ReferenceEquals(_s, other) || _s.Equals(other);

        /// <summary>The session went away: stop listening to it.</summary>
        public void Detach()
        {
            try
            {
                _s.PlaybackInfoChanged -= _onPlayback;
                _s.MediaPropertiesChanged -= _onProps;
                _s.TimelinePropertiesChanged -= _onTimeline;
            }
            catch { /* the app already closed it */ }
        }

        public (TimeSpan Position, TimeSpan Duration, DateTimeOffset At) Timeline
        {
            get
            {
                try
                {
                    var t = _s.GetTimelineProperties();
                    return (t.Position - t.StartTime, t.EndTime - t.StartTime, t.LastUpdatedTime);
                }
                catch { return default; }
            }
        }

        public async Task<bool> PlayPauseAsync() => await _s.TryTogglePlayPauseAsync();
        public async Task<bool> NextAsync() => await _s.TrySkipNextAsync();
        public async Task<bool> PreviousAsync() => await _s.TrySkipPreviousAsync();

        public bool PlaybackPlaying
        {
            get
            {
                try { return _s.GetPlaybackInfo()?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing; }
                catch { return false; } // the app closed the session meanwhile
            }
        }

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

using System.IO;
using NothingShell.Media;

namespace NothingShell.Tests;

public sealed class MediaSessionServiceTests
{
    private sealed class FakeSession : ISessionSource
    {
        public bool PlaybackPlaying { get; set; }
        public string? Title { get; set; }
        public string? Artist { get; set; }
        public string? AppId { get; set; } = "Spotify.exe";
        public Stream? Thumb { get; set; }
        public Task<Stream?> ThumbnailAsync() => Task.FromResult(Thumb);
        public (TimeSpan Position, TimeSpan Duration, DateTimeOffset At) Timeline { get; set; }
        public int PlayPauseCalls, NextCalls, PreviousCalls;
        public Task<bool> PlayPauseAsync() { PlayPauseCalls++; return Task.FromResult(true); }
        public Task<bool> NextAsync() { NextCalls++; return Task.FromResult(true); }
        public Task<bool> PreviousAsync() { PreviousCalls++; return Task.FromResult(true); }
        public TimeSpan? SoughtTo;
        public Task<bool> SeekAsync(TimeSpan position) { SoughtTo = position; return Task.FromResult(true); }
        public event Action? Changed;
        public void Raise() => Changed?.Invoke();
    }

    private sealed class FakeManager : ISessionManager
    {
        public ISessionSource? Current { get; set; }
        public event Action? CurrentChanged;
        public void Raise() => CurrentChanged?.Invoke();
    }

    private static async Task<MediaSessionService> Started(FakeManager m)
    {
        var s = new MediaSessionService(() => Task.FromResult<ISessionManager?>(m), decodeArt: _ => null);
        await s.StartAsync();
        return s;
    }

    [Fact]
    public async Task NoSession_IsNotPlaying()
    {
        var s = await Started(new FakeManager());
        Assert.False(s.Current.Playing);
    }

    [Fact]
    public async Task PausedSession_IsNotPlaying()
    {
        var s = await Started(new FakeManager { Current = new FakeSession { PlaybackPlaying = false, Title = "x" } });
        Assert.False(s.Current.Playing);
    }

    [Fact]
    public async Task PlayingSession_ReportsTitleAndArtist()
    {
        var s = await Started(new FakeManager { Current = new FakeSession { PlaybackPlaying = true, Title = "Song", Artist = "Band" } });
        Assert.True(s.Current.Playing);
        Assert.Equal("Song", s.Current.Title);
        Assert.Equal("Band", s.Current.Artist);
    }

    [Fact]
    public async Task NoThumbnail_UsesPlaceholder()
    {
        var s = await Started(new FakeManager { Current = new FakeSession { PlaybackPlaying = true, Thumb = null } });
        Assert.Null(s.Current.Art);
    }

    // User, 2026-11-21: drag the media widget's progress dots to jump anywhere in the song.
    [Fact]
    public async Task Seek_GoesToTheCurrentSession()
    {
        var session = new FakeSession { PlaybackPlaying = true };
        var s = await Started(new FakeManager { Current = session });
        Assert.True(await s.SeekAsync(TimeSpan.FromSeconds(95)));
        Assert.Equal(TimeSpan.FromSeconds(95), session.SoughtTo);
    }

    [Fact]
    public async Task PlayPause_CallsCurrentSession()
    {
        var session = new FakeSession { PlaybackPlaying = true };
        var s = await Started(new FakeManager { Current = session });
        Assert.True(await s.PlayPauseAsync());
        Assert.True(await s.NextAsync());
        Assert.True(await s.PreviousAsync());
        Assert.Equal((1, 1, 1), (session.PlayPauseCalls, session.NextCalls, session.PreviousCalls));
    }

    [Fact]
    public async Task Snapshot_HasTimeline()
    {
        var at = new DateTimeOffset(2026, 11, 19, 10, 0, 0, TimeSpan.Zero);
        var session = new FakeSession
        {
            PlaybackPlaying = true, Title = "Kids",
            Timeline = (TimeSpan.FromSeconds(102), TimeSpan.FromSeconds(238), at),
        };
        var s = await Started(new FakeManager { Current = session });
        Assert.Equal(TimeSpan.FromSeconds(102), s.Current.Position);
        Assert.Equal(TimeSpan.FromSeconds(238), s.Current.Duration);
        Assert.Equal(at, s.Current.PositionAt);
    }

    [Fact]
    public async Task PausedSession_StillHasTitle()
    {
        var s = await Started(new FakeManager { Current = new FakeSession { PlaybackPlaying = false, Title = "Kids" } });
        Assert.Equal("Kids", s.Current.Title); // the media tile shows a paused track
    }

    [Fact]
    public async Task NoSession_ControlsReturnFalse()
    {
        var s = await Started(new FakeManager());
        Assert.False(await s.PlayPauseAsync());
        Assert.False(await s.NextAsync());
        Assert.False(await s.PreviousAsync());
    }

    [Fact]
    public async Task SessionChange_RaisesChanged()
    {
        var m = new FakeManager();
        var s = await Started(m);
        MediaSnapshot? got = null;
        s.Changed += snap => got = snap;
        var session = new FakeSession { PlaybackPlaying = true, Title = "Now" };
        m.Current = session;
        m.Raise();
        await s.Idle();
        Assert.True(got?.Playing);

        session.PlaybackPlaying = false;
        session.Raise();
        await s.Idle();
        Assert.False(got?.Playing);
    }
}

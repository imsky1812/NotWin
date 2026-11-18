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

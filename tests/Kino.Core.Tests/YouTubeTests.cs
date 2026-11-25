using Kino.Core.Media;
using Kino.Core.Sync;

namespace Kino.Core.Tests;

public class YouTubeTests
{
    [Theory]
    [InlineData("https://www.youtube.com/watch?v=aqz-KE-bpKQ")]
    [InlineData("https://youtube.com/watch?feature=share&v=aqz-KE-bpKQ&t=30")]
    [InlineData("https://m.youtube.com/watch?v=aqz-KE-bpKQ")]
    [InlineData("https://youtu.be/aqz-KE-bpKQ?t=12")]
    [InlineData("https://www.youtube.com/shorts/aqz-KE-bpKQ")]
    [InlineData("https://www.youtube.com/embed/aqz-KE-bpKQ")]
    [InlineData(" aqz-KE-bpKQ ")]
    public void Recognises_the_same_links_as_the_theatre(string link) => Assert.Equal("aqz-KE-bpKQ", YouTube.VideoId(link));

    [Theory]
    [InlineData("https://evil.example/watch?v=aqz-KE-bpKQ")]
    [InlineData("http://www.youtube.com/watch?v=aqz-KE-bpKQ")]
    [InlineData("https://www.youtube.com/channel/UCabcdefghijk")]
    [InlineData("https://example.com/video.mp4")]
    public void Rejects_everything_else(string link) => Assert.Null(YouTube.VideoId(link));

    [Fact]
    public void Switch_routes_the_engine_to_the_active_player()
    {
        var mpv = new SimPlayer { Pos = 10 };
        var yt = new SimPlayer { Pos = 99 };
        var sw = new PlaybackSwitch(mpv);
        Assert.Equal(10, sw.Position);
        sw.Active = yt;
        sw.Seek(42);
        Assert.Equal(42, yt.Pos);
        Assert.Equal(10, mpv.Pos);
    }

    [Fact]
    public void YouTube_settings_have_no_rate_correction()
    {
        var h = new SimHarness(SyncSettings.YouTube);
        h.Player.Pos = 9; h.Player.Paused = false; // 1 s behind: inside YouTube's tolerance band
        h.Engine.ApplyAnchor(new Kino.Core.Protocol.Anchor("i1", "playing", 10, 1, h.TheatreMs, 1));
        h.Run(10);
        Assert.All(h.Player.SpeedHistory, s => Assert.Equal(1.0, s));
        Assert.Equal(0, h.Player.Seeks);

        h.Player.Seek(h.Player.Pos - 3); // now 4 s behind: past 1.5 s, so it seeks
        h.Run(4);
        Assert.InRange(h.TrueDrift, -1.0, 1.0);
    }
}

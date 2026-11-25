using Kino.Core.Identity;
using Kino.Core.Net;
using Kino.Core.Protocol;

namespace Kino.Core.Tests;

/// <summary>
/// End-to-end checks against a real theatre. Opt-in: set KINO_LIVE_THEATRE to its URL, e.g.
/// <c>$env:KINO_LIVE_THEATRE="https://kino-aurora.srie.workers.dev"</c> or <c>http://127.0.0.1:8787</c>.
/// Without it, these tests return immediately.
/// </summary>
public class LiveTheatreTests
{
    private static readonly string? Url = Environment.GetEnvironmentVariable("KINO_LIVE_THEATRE");

    [Fact]
    public async Task Private_screen_rejects_wrong_password_and_accepts_right_one()
    {
        if (Url is null) return;
        var api = new TheatreApi(new Uri(Url));
        var host = DeviceIdentity.LoadOrCreate("test-live-host");
        var viewer = DeviceIdentity.LoadOrCreate("test-live-viewer");

        var (hostSession, created) = await ScreenSession.CreateAsync(api, host, new SimPlayer(),
            new CreateScreenRequest("Live test", "private", "letmein1", Hidden: true), "Host");
        await using var _ = hostSession;
        Assert.True(hostSession.IsHost);

        var network = new TheatreNetwork([new("live", "Live", Url)]);
        var (found, _) = await network.ResolveAsync(created.Code);
        Assert.NotNull(found);
        Assert.True(found.Screen.RequiresPassword);

        var lobby = await api.LobbyAsync();
        Assert.DoesNotContain(lobby!.Screens, s => s.Code == created.Code); // hidden private screens aren't listed

        var wrong = await Assert.ThrowsAsync<TheatreException>(() =>
            ScreenSession.JoinResolvedAsync(found.Api, viewer, new SimPlayer(), found.Screen, "Viewer", "nope"));
        Assert.Equal("wrong_password", wrong.Code);

        await using var joined = await ScreenSession.JoinResolvedAsync(found.Api, viewer, new SimPlayer(), found.Screen, "Viewer", "letmein1");
        Assert.False(joined.IsHost);
        Assert.Equal(2, joined.Viewers.Count);
    }
}

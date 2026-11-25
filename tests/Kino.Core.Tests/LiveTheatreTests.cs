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

    [Fact]
    public async Task Co_host_takes_over_the_timeline_while_the_host_is_away()
    {
        if (Url is null) return;
        var api = new TheatreApi(new Uri(Url));
        var hostId = DeviceIdentity.LoadOrCreate("test-live-host");
        var coId = DeviceIdentity.LoadOrCreate("test-live-viewer");

        var (host, created) = await ScreenSession.CreateAsync(api, hostId, new SimPlayer(), new CreateScreenRequest("Handover test", "public"), "Host");
        var resolved = (await api.ResolveAsync(created.Code))!;
        await using var co = await ScreenSession.JoinResolvedAsync(api, coId, new SimPlayer(), resolved, "Co");
        Assert.False(co.Engine.IsHost);

        await WaitFor(co, () => co.Viewers.Count == 2);
        await host.SetCoHostAsync(co.Self!.Id, true);
        await WaitFor(co, () => co.Self?.CoHost == 1);
        Assert.True(co.Can(Permissions.ViewersKick)); // co-hosts get every permission
        Assert.False(co.IsTimelineAuthority);

        await host.DisposeAsync(); // host leaves
        await WaitFor(co, () => co.Self?.ActingHost == true);
        Assert.True(co.IsTimelineAuthority);
        Assert.True(co.Engine.IsHost); // now re-anchors instead of being corrected

        await using var back = await ScreenSession.JoinResolvedAsync(api, hostId, new SimPlayer(), resolved, "Host");
        Assert.True(back.IsHost);
        await WaitFor(co, () => co.Self?.ActingHost == false);
        Assert.False(co.Engine.IsHost);
        await back.CloseScreenAsync();
    }

    [Fact]
    public async Task Network_drop_reconnects_silently_and_kick_does_not()
    {
        if (Url is null) return;
        var api = new TheatreApi(new Uri(Url));
        var (host, created) = await ScreenSession.CreateAsync(api, DeviceIdentity.LoadOrCreate("test-live-host"), new SimPlayer(),
            new CreateScreenRequest("Reconnect test", "private", "pw123456"), "Host");
        await using var _ = host;
        var resolved = (await api.ResolveAsync(created.Code))!;
        await using var viewer = await ScreenSession.JoinResolvedAsync(api, DeviceIdentity.LoadOrCreate("test-live-viewer"), new SimPlayer(), resolved, "Viewer", "pw123456");
        string id = viewer.Self!.Id;

        int reconnecting = 0, reconnected = 0;
        string? closed = null;
        viewer.Reconnecting += () => reconnecting++;
        viewer.Reconnected += () => reconnected++;
        viewer.Closed += r => closed = r;

        viewer.SimulateNetworkDrop();
        await WaitFor(host, () => reconnected == 1); // no password needed: the theatre remembers this viewer
        Assert.Equal(1, reconnecting);
        Assert.Null(closed);
        await WaitFor(host, () => host.Viewers.Any(v => v.Id == id && v.Connected));

        // Settings and code changes reach everyone.
        await host.UpdateSettingsAsync(name: "Renamed", maxViewers: 6);
        await WaitFor(viewer, () => viewer.Screen?.Name == "Renamed" && viewer.Screen.MaxViewers == 6);
        string oldCode = viewer.Code;
        await host.RegenerateCodeAsync();
        await WaitFor(viewer, () => viewer.Code != oldCode);
        Assert.Null(await api.ResolveAsync(oldCode));

        await host.KickAsync(id);
        await WaitFor(host, () => closed is not null);
        Assert.Equal("kicked", closed);
        Assert.Equal(1, reconnecting); // a kick is final: no reconnect attempt
        await host.CloseScreenAsync();
    }

    private static async Task WaitFor(ScreenSession s, Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("condition not met within 10 s");
            await Task.Delay(50);
        }
    }
}

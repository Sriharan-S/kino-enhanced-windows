using Kino.Core.Lobby;
using Kino.Core.Net;
using Kino.Core.Protocol;

namespace Kino.Core.Tests;

public class LobbyViewTests
{
    private static readonly TheatreEntry Aurora = new("aurora", "Aurora", "https://aurora.test");
    private static readonly TheatreEntry Nova = new("nova", "Nova", "https://nova.test");
    private static readonly TheatreEntry Orion = new("orion", "Orion", "https://orion.test");

    private static LobbyScreen S(string code, string name, int viewers, string vis = "public", int max = 10, string? host = "Sam") =>
        new(code, name, vis, viewers, max, host);

    private static IReadOnlyList<LobbyEntry> Sample() => LobbyView.Merge(
    [
        new(Aurora, [S("AAAAAA", "Movie night", 3), S("BBBBBB", "Anime club", 7, "private")], 40, null),
        new(Nova, [S("CCCCCC", "movie marathon", 10, max: 10), S("DDDDDD", "Quiet room", 1, host: "Alex")], 120, null),
        new(Orion, [], null, "timed out"),
    ]);

    [Fact]
    public void Merges_all_available_theatres_and_skips_failed_ones()
    {
        var all = Sample();
        Assert.Equal(4, all.Count);
        Assert.Equal(2, all.Count(e => e.Theatre.Id == "nova"));
        Assert.Equal(40, all.First(e => e.Theatre.Id == "aurora").PingMs);
    }

    [Fact]
    public void Known_pings_override_request_time()
    {
        var all = LobbyView.Merge([new(Aurora, [S("AAAAAA", "x", 1)], 900, null)], new Dictionary<string, double> { ["aurora"] = 35 });
        Assert.Equal(35, all[0].PingMs);
    }

    [Fact]
    public void Default_sort_is_most_viewers_with_full_screens_last()
    {
        var codes = LobbyView.Apply(Sample(), new LobbyQuery()).Select(e => e.Screen.Code);
        Assert.Equal(["BBBBBB", "AAAAAA", "DDDDDD", "CCCCCC"], codes); // CCCCCC is full (10/10)
    }

    [Fact]
    public void Sorts_by_ping_and_name()
    {
        var byPing = LobbyView.Apply(Sample(), new LobbyQuery(Sort: LobbySort.Ping)).Select(e => e.Theatre.Id).ToList();
        Assert.Equal(["aurora", "aurora", "nova", "nova"], byPing);

        var byName = LobbyView.Apply(Sample(), new LobbyQuery(Sort: LobbySort.Name)).Select(e => e.Screen.Name);
        Assert.Equal(["Anime club", "movie marathon", "Movie night", "Quiet room"], byName); // case-insensitive
    }

    [Theory]
    [InlineData("movie", 2)]      // name, case-insensitive
    [InlineData("alex", 1)]       // host name
    [InlineData("dddddd", 1)]     // exact code
    [InlineData("DDD", 0)]        // partial codes don't match
    [InlineData("  ", 4)]
    public void Searches_name_host_and_code(string search, int expected) =>
        Assert.Equal(expected, LobbyView.Apply(Sample(), new LobbyQuery(Search: search)).Count);

    [Fact]
    public void Filters_by_visibility_and_theatre()
    {
        Assert.Single(LobbyView.Apply(Sample(), new LobbyQuery(Visibility: VisibilityFilter.Private)));
        Assert.Equal(3, LobbyView.Apply(Sample(), new LobbyQuery(Visibility: VisibilityFilter.Public)).Count);
        Assert.All(LobbyView.Apply(Sample(), new LobbyQuery(TheatreId: "nova")), e => Assert.Equal("nova", e.Theatre.Id));
    }

    [Fact]
    public void Statuses_report_unavailable_theatres()
    {
        var st = LobbyView.Statuses(
        [
            new(Aurora, [], 40, null),
            new(Orion, [], null, "timed out"),
        ]);
        Assert.Equal(40, st[0].PingMs);
        Assert.Equal("timed out", st[1].Failure);
        Assert.Null(st[1].PingMs);
    }
}

public class LobbyNetworkTests
{
    [Fact]
    public async Task Lobby_fetch_survives_a_down_theatre()
    {
        TheatreEntry[] theatres = [new("aurora", "Aurora", "https://aurora.test"), new("orion", "Orion", "https://orion.test")];
        var http = new HttpClient(new StubHandler(req => req.RequestUri!.Host == "orion.test"
            ? throw new HttpRequestException("down")
            : Task.FromResult(StubHandler.Json("""
                { "theatre": { "id": "aurora", "name": "Aurora" },
                  "screens": [ { "code": "AAAAAA", "name": "Movie", "visibility": "public", "viewers": 2, "maxViewers": 10, "host": "Sam" } ] }
                """))));

        var lobbies = await new TheatreNetwork(theatres, http).LobbyAllAsync();

        Assert.True(lobbies[0].Available);
        Assert.Single(lobbies[0].Screens);
        Assert.False(lobbies[1].Available);
        Assert.Single(LobbyView.Merge(lobbies));
    }
}

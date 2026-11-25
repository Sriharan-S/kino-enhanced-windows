using System.Net;
using System.Text;
using Kino.Core.Net;

namespace Kino.Core.Tests;

/// <summary>Answers requests from a function; throwing simulates a network failure.</summary>
internal sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
{
    public StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : this((r, _) => respond(r)) { }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => respond(request, ct);

    public static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
}

public sealed class TheatreDirectoryTests : IDisposable
{
    private static readonly Uri Url = new("https://directory.test/servers.json");
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "kino-tests-" + Guid.NewGuid().ToString("N"));
    private string CachePath => Path.Combine(_dir, "servers.json");

    private const string Two = """
        { "version": 1, "theatres": [
          { "id": "aurora", "name": "Aurora", "url": "https://kino-aurora.example.workers.dev" },
          { "id": "nova",   "name": "Nova",   "url": "https://kino-nova.example.workers.dev" } ] }
        """;

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private static HttpClient Client(Func<HttpRequestMessage, Task<HttpResponseMessage>> f) => new(new StubHandler(f));

    [Fact]
    public async Task Live_list_is_used_and_cached()
    {
        var list = await TheatreDirectory.LoadAsync(Client(_ => Task.FromResult(StubHandler.Json(Two))), Url, CachePath);
        Assert.Equal(DirectorySource.Live, list.Source);
        Assert.Equal(["aurora", "nova"], list.Theatres.Select(t => t.Id));
        Assert.True(File.Exists(CachePath));
    }

    [Fact]
    public async Task Falls_back_to_cache_then_bundled()
    {
        var offline = Client(_ => throw new HttpRequestException("offline"));

        var bundled = await TheatreDirectory.LoadAsync(offline, Url, CachePath);
        Assert.Equal(DirectorySource.Bundled, bundled.Source);
        Assert.Equal(TheatreDirectory.Bundled.Theatres, bundled.Theatres);

        await TheatreDirectory.LoadAsync(Client(_ => Task.FromResult(StubHandler.Json(Two))), Url, CachePath);
        var cached = await TheatreDirectory.LoadAsync(offline, Url, CachePath);
        Assert.Equal(DirectorySource.Cached, cached.Source);
        Assert.Equal(2, cached.Theatres.Count);
    }

    [Fact]
    public async Task Bad_live_file_does_not_overwrite_good_cache()
    {
        await TheatreDirectory.LoadAsync(Client(_ => Task.FromResult(StubHandler.Json(Two))), Url, CachePath);
        var list = await TheatreDirectory.LoadAsync(Client(_ => Task.FromResult(StubHandler.Json("<html>captive portal</html>"))), Url, CachePath);
        Assert.Equal(DirectorySource.Cached, list.Source);
        Assert.Contains("nova", File.ReadAllText(CachePath));
    }

    [Theory]
    [InlineData("""{ "version": 2, "theatres": [ { "id": "a", "name": "A", "url": "https://a.test" } ] }""")] // unknown version
    [InlineData("""{ "version": 1, "theatres": [] }""")]
    [InlineData("""{ "version": 1, "theatres": [ { "id": "a", "name": "A", "url": "http://a.test" } ] }""")] // plain http
    [InlineData("not json")]
    public void Rejects_unusable_files(string json) => Assert.Null(TheatreDirectory.Parse(json));

    [Fact]
    public void Drops_invalid_and_duplicate_entries_but_keeps_the_rest()
    {
        var f = TheatreDirectory.Parse("""
            { "version": 1, "theatres": [
              { "id": "a", "name": "A", "url": "https://a.test" },
              { "id": "a", "name": "A again", "url": "https://a2.test" },
              { "id": "",  "name": "No id", "url": "https://b.test" },
              { "id": "c", "name": "C", "url": "ftp://c.test" },
              { "id": "local", "name": "Local", "url": "http://127.0.0.1:8787" } ] }
            """);
        Assert.NotNull(f);
        Assert.Equal(["a", "local"], f.Theatres.Select(t => t.Id));
    }
}

public class TheatreNetworkTests
{
    private static readonly TheatreEntry[] Theatres =
    [
        new("aurora", "Aurora", "https://aurora.test"),
        new("nova", "Nova", "https://nova.test"),
        new("orion", "Orion", "https://orion.test"),
    ];

    private const string Resolved = """{ "code": "ABC234", "screenId": "s1", "name": "Movie", "visibility": "public", "requiresPassword": false }""";

    private static string Info(string id, string status = "ok", int min = 1, int max = 1) =>
        $$"""{ "id": "{{id}}", "name": "{{id}}", "protocol": {{max}}, "minProtocol": {{min}}, "status": "{{status}}", "time": 0 }""";

    [Fact]
    public async Task Resolve_finds_the_code_on_whichever_theatre_has_it()
    {
        var http = new HttpClient(new StubHandler(async req =>
        {
            switch (req.RequestUri!.Host)
            {
                case "aurora.test": return StubHandler.Json("""{"error":{"code":"not_found"}}""", HttpStatusCode.NotFound);
                case "nova.test": await Task.Delay(50); return StubHandler.Json(Resolved);
                default: throw new HttpRequestException("down");
            }
        }));
        var net = new TheatreNetwork(Theatres, http);

        var (found, _) = await net.ResolveAsync("ABC234");
        Assert.NotNull(found);
        Assert.Equal("nova", found.Theatre.Id);
        Assert.Equal("s1", found.Screen.ScreenId);
    }

    [Fact]
    public async Task Resolve_reports_unreachable_theatres_when_not_found()
    {
        var http = new HttpClient(new StubHandler(req => req.RequestUri!.Host == "orion.test"
            ? throw new HttpRequestException("down")
            : Task.FromResult(StubHandler.Json("{}", HttpStatusCode.NotFound))));
        var net = new TheatreNetwork(Theatres, http);

        var (found, unreachable) = await net.ResolveAsync("ZZZZZZ");
        Assert.Null(found);
        Assert.Equal(1, unreachable);
    }

    [Fact]
    public async Task Slow_theatre_times_out_without_blocking()
    {
        var http = new HttpClient(new StubHandler(async (req, ct) =>
        {
            if (req.RequestUri!.Host == "orion.test") await Task.Delay(TimeSpan.FromSeconds(30), ct);
            return StubHandler.Json(Info(req.RequestUri.Host.Split('.')[0]));
        }));
        var net = new TheatreNetwork(Theatres, http) { Timeout = TimeSpan.FromMilliseconds(200) };

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var probes = await net.ProbeAllAsync();
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5));
        Assert.Equal("timed out", probes[2].Failure);
    }

    [Fact]
    public async Task Fastest_skips_unhealthy_and_incompatible_theatres()
    {
        var http = new HttpClient(new StubHandler(req => Task.FromResult(StubHandler.Json(req.RequestUri!.Host switch
        {
            "aurora.test" => Info("aurora", status: "quota_exhausted"),
            "nova.test" => Info("nova", min: 2, max: 2), // needs a newer client
            _ => Info("orion"),
        }))));
        var probes = await new TheatreNetwork(Theatres, http).ProbeAllAsync();

        Assert.Equal("orion", TheatreNetwork.Fastest(probes)?.Theatre.Id);
        Assert.False(probes[0].Usable);
        Assert.False(probes[1].Usable);
    }
}

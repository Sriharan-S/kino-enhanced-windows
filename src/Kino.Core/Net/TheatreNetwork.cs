using System.Diagnostics;
using Kino.Core.Protocol;

namespace Kino.Core.Net;

/// <summary>Result of probing one theatre with <c>GET /info</c>.</summary>
public sealed record TheatreProbe(TheatreEntry Theatre, TheatreInfo? Info, double? RttMs, string? Failure)
{
    /// <summary>Reachable, reports ok, and accepts this client's protocol version.</summary>
    public bool Usable => Info is { Status: "ok" } i && i.MinProtocol <= ProtocolInfo.Version && ProtocolInfo.Version <= i.Protocol;
}

/// <summary>One theatre's lobby listing, or why it couldn't be fetched.</summary>
public sealed record TheatreLobby(TheatreEntry Theatre, IReadOnlyList<LobbyScreen> Screens, double? RequestMs, string? Failure)
{
    public bool Available => Failure is null;
}

/// <summary>A screen code found on a specific theatre.</summary>
public sealed record ResolvedScreen(TheatreApi Api, TheatreEntry Theatre, ResolveResponse Screen);

/// <summary>Operations across every theatre in the directory, run in parallel (spec §4.4, §8).</summary>
public sealed class TheatreNetwork
{
    private readonly HttpClient _http;
    private readonly Dictionary<string, TheatreApi> _apis;

    public IReadOnlyList<TheatreEntry> Theatres { get; }

    /// <summary>Per-theatre timeout, so one slow theatre never blocks the others (spec §10.4).</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(3);

    public TheatreNetwork(IReadOnlyList<TheatreEntry> theatres, HttpClient? http = null)
    {
        Theatres = theatres;
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        _apis = theatres.ToDictionary(t => t.Id, t => new TheatreApi(t.Uri, _http));
    }

    public TheatreApi Api(TheatreEntry theatre) => _apis[theatre.Id];

    /// <summary>Probes every theatre. The first request warms up the connection, so the second one is timed.</summary>
    public Task<TheatreProbe[]> ProbeAllAsync(CancellationToken ct = default) =>
        Task.WhenAll(Theatres.Select(t => ProbeAsync(t, ct)));

    private async Task<TheatreProbe> ProbeAsync(TheatreEntry t, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(Timeout);
        try
        {
            var api = _apis[t.Id];
            await api.InfoAsync(cts.Token); // DNS + TLS + connection setup
            long start = Stopwatch.GetTimestamp();
            var info = await api.InfoAsync(cts.Token);
            double rtt = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            return new(t, info, rtt, null);
        }
        catch (Exception e) when (!ct.IsCancellationRequested)
        {
            return new(t, null, null, e is OperationCanceledException ? "timed out" : e.Message);
        }
    }

    /// <summary>
    /// Fetches every theatre's lobby in parallel. A slow or failed theatre yields a result with
    /// <see cref="TheatreLobby.Failure"/> set instead of blocking the others. Once a connection is warm, the request
    /// time doubles as the theatre's ping, so lobby refreshes cost one request per theatre.
    /// </summary>
    public Task<TheatreLobby[]> LobbyAllAsync(CancellationToken ct = default) =>
        Task.WhenAll(Theatres.Select(t => LobbyAsync(t, ct)));

    private async Task<TheatreLobby> LobbyAsync(TheatreEntry t, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(Timeout);
        try
        {
            long start = Stopwatch.GetTimestamp();
            var lobby = await _apis[t.Id].LobbyAsync(cts.Token);
            double ms = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            return new(t, lobby?.Screens ?? [], ms, null);
        }
        catch (Exception e) when (!ct.IsCancellationRequested)
        {
            return new(t, [], null, e is OperationCanceledException ? "timed out" : e.Message);
        }
    }

    /// <summary>The usable theatre with the lowest latency, or null if none is usable.</summary>
    public static TheatreProbe? Fastest(IEnumerable<TheatreProbe> probes) =>
        probes.Where(p => p.Usable).OrderBy(p => p.RttMs).FirstOrDefault();

    /// <summary>
    /// Looks a code up on every theatre in parallel and returns the first match.
    /// Theatres that are down or slow are skipped; <paramref name="unreachable"/> counts them.
    /// </summary>
    public async Task<(ResolvedScreen? Found, int Unreachable)> ResolveAsync(string code, CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(Timeout);

        var pending = Theatres.Select(async t =>
        {
            var api = _apis[t.Id];
            var screen = await api.ResolveAsync(code, cts.Token);
            return screen is null ? null : new ResolvedScreen(api, t, screen);
        }).ToList();

        int unreachable = 0;
        while (pending.Count > 0)
        {
            var done = await Task.WhenAny(pending);
            pending.Remove(done);
            if (done.IsCompletedSuccessfully)
            {
                if (done.Result is { } found)
                {
                    await cts.CancelAsync(); // stop the others
                    return (found, unreachable);
                }
            }
            else
            {
                ct.ThrowIfCancellationRequested();
                unreachable++;
            }
        }
        return (null, unreachable);
    }
}

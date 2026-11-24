using Kino.Core.Identity;
using Kino.Core.Net;
using Kino.Core.Protocol;
using Kino.Core.Sync;

namespace Kino.Core;

/// <summary>
/// A viewer's live session in one screen: connection, clock sync and the sync engine loop.
/// Events are raised on background threads.
/// </summary>
public sealed class ScreenSession : IAsyncDisposable
{
    private readonly ScreenConnection _conn = new();
    private readonly ClockSync _clock = new();
    private readonly SyncSettings _settings;
    private readonly CancellationTokenSource _cts = new();
    private TaskCompletionSource<Welcome>? _welcome;
    private Task? _loop;

    public SyncEngine Engine { get; }
    public ClockSync Clock => _clock;
    public string Code { get; private set; } = "";
    public Welcome? Joined { get; private set; }
    public bool IsHost => Joined?.Self.IsHost == true;
    public Item? CurrentItem { get; private set; }
    public IReadOnlyList<ViewerInfo> Viewers { get; private set; } = [];

    public event Action<Item?>? ItemChanged;
    public event Action<IReadOnlyList<ViewerInfo>>? ViewersChanged;
    public event Action<SyncStatus>? StatusChanged;
    public event Action<ServerError>? Error;
    public event Action<string>? Closed;

    public ScreenSession(IPlayback player, SyncSettings? settings = null)
    {
        _settings = settings ?? SyncSettings.Default;
        Engine = new SyncEngine(player, () => _clock.TheatreNowMs, () => _clock.LocalNowMs, _settings);
        Engine.ReanchorRequested += (pos, at) => _ = _conn.SendAsync("reanchor", new { position = pos, at });
        _conn.Message += OnMessage;
        _conn.Closed += reason => Closed?.Invoke(reason);
    }

    /// <summary>Creates a screen on the theatre and joins it as host.</summary>
    public static async Task<(ScreenSession Session, CreateScreenResponse Created)> CreateAsync(
        TheatreApi api, DeviceIdentity identity, IPlayback player, CreateScreenRequest req, string displayName, CancellationToken ct = default)
    {
        var created = await api.CreateScreenAsync(req, ct);
        var session = new ScreenSession(player);
        await session.JoinAsync(api, identity, created.Code, created.ScreenId, displayName, password: null, claim: created.Claim, ct);
        return (session, created);
    }

    /// <summary>Resolves a code and joins the screen.</summary>
    public static async Task<ScreenSession> JoinByCodeAsync(
        TheatreApi api, DeviceIdentity identity, IPlayback player, string code, string displayName, string? password = null, CancellationToken ct = default)
    {
        var resolved = await api.ResolveAsync(code, ct) ?? throw new TheatreException(404, "screen not found");
        var session = new ScreenSession(player);
        await session.JoinAsync(api, identity, resolved.Code, resolved.ScreenId, displayName, password, claim: null, ct);
        return session;
    }

    private async Task JoinAsync(TheatreApi api, DeviceIdentity identity, string code, string screenId, string name,
                                 string? password, string? claim, CancellationToken ct)
    {
        Code = code;
        _welcome = new(TaskCreationOptions.RunContinuationsAsynchronously);
        await _conn.ConnectAsync(api.WebSocketUrl(code), ct);
        await _conn.SendAsync("hello", new
        {
            protocol = ProtocolInfo.Version,
            token = identity.TokenFor(screenId),
            name,
            device = "windows",
            password,
            claim,
        });

        var welcome = await _welcome.Task.WaitAsync(TimeSpan.FromSeconds(10), ct);
        Joined = welcome;
        Engine.IsHost = welcome.Self.IsHost;
        CurrentItem = welcome.Item;
        Viewers = welcome.Viewers;

        // Clock-sync burst before the engine starts acting on anchors.
        Engine.ApplyAnchor(welcome.Anchor);
        await PingBurstAsync(_settings.BurstSamples);
        _loop = Task.Run(RunLoop);
    }

    // ---- Commands (the theatre validates permissions) ---------------------------------

    public Task PlayAsync() => _conn.SendAsync("play");
    public Task PauseAsync() => _conn.SendAsync("pause");
    public Task SeekAsync(double seconds) => _conn.SendAsync("seek", new { to = seconds });

    public Task LoadLocalAsync(string fileName, double? duration) =>
        _conn.SendAsync("load", new { kind = "local", name = fileName, duration });

    public Task LoadUrlAsync(string url, string name, double? duration) =>
        _conn.SendAsync("load", new { kind = "url", name, duration, url });

    // ---- Internals -------------------------------------------------------------------

    private void OnMessage(ServerMessage msg)
    {
        switch (msg)
        {
            case ServerMessage.WelcomeMsg w:
                _welcome?.TrySetResult(w.Data);
                break;
            case ServerMessage.PongMsg p:
                _clock.AddSample(p.Data.T0, p.Data.Ts, _clock.LocalNowMs);
                break;
            case ServerMessage.AnchorMsg a:
                // Safe before clock sync completes: the engine only acts on anchors once the loop runs.
                Engine.ApplyAnchor(a.Data);
                break;
            case ServerMessage.ItemMsg i:
                CurrentItem = i.Data;
                ItemChanged?.Invoke(i.Data);
                break;
            case ServerMessage.ViewersMsg v:
                Viewers = v.Data;
                ViewersChanged?.Invoke(v.Data);
                break;
            case ServerMessage.ErrorMsg e:
                _welcome?.TrySetException(new TheatreException(0, $"{e.Data.Code}: {e.Data.Message}"));
                Error?.Invoke(e.Data);
                break;
        }
    }

    private async Task PingBurstAsync(int samples)
    {
        for (int i = 0; i < samples; i++)
        {
            await _conn.SendAsync("ping", new { t0 = _clock.LocalNowMs });
            await Task.Delay(_settings.BurstInterval);
        }
        await Task.Delay(500); // let the last pongs arrive
        if (_clock.Commit()) await _conn.SendAsync("rtt", new { value = Math.Round(_clock.RttMs, 1) });
    }

    private async Task RunLoop()
    {
        using var timer = new PeriodicTimer(_settings.Tick);
        double last = _clock.LocalNowMs;
        double lastResync = last;

        try
        {
            while (await timer.WaitForNextTickAsync(_cts.Token).ConfigureAwait(false))
            {
                double now = _clock.LocalNowMs;
                Engine.Tick((now - last) / 1000.0);
                last = now;
                StatusChanged?.Invoke(Engine.Status);

                if (now - lastResync >= _settings.ResyncEvery.TotalMilliseconds)
                {
                    lastResync = now;
                    _ = PingBurstAsync(_settings.ResyncSamples);
                }
            }
        }
        catch (OperationCanceledException) { /* session ended */ }
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        if (_loop is not null) await Task.WhenAny(_loop, Task.Delay(1000));
        await _conn.DisposeAsync();
    }
}

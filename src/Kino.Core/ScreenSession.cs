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
    public Item? CurrentItem { get; private set; }
    public IReadOnlyList<ViewerInfo> Viewers { get; private set; } = [];
    public IReadOnlyList<Role> Roles { get; private set; } = [];

    /// <summary>Co-hosts in priority order, including ones who aren't present.</summary>
    public IReadOnlyList<CoHostInfo> CoHosts { get; private set; } = [];

    /// <summary>This viewer's current entry. Roles, co-host status and permissions change during a session.</summary>
    public ViewerInfo? Self { get; private set; }

    public bool IsHost => Self?.IsHost == true;

    /// <summary>The host, or the acting host while the host is away: re-anchors instead of being corrected.</summary>
    public bool IsTimelineAuthority => Self?.IsTimelineAuthority == true;

    /// <summary>Convenience for enabling controls. The theatre enforces permissions regardless.</summary>
    public bool Can(string permission) => Self?.Can(permission) == true;

    public event Action<Item?>? ItemChanged;
    public event Action<IReadOnlyList<ViewerInfo>>? ViewersChanged;
    public event Action<IReadOnlyList<Role>>? RolesChanged;
    public event Action<IReadOnlyList<CoHostInfo>>? CoHostsChanged;
    public event Action<SyncStatus>? StatusChanged;
    public event Action<ServerError>? Error;
    public event Action<string>? Closed;

    public ScreenSession(IPlayback player, SyncSettings? settings = null)
    {
        _settings = settings ?? SyncSettings.Default;
        Engine = new SyncEngine(player, () => _clock.TheatreNowMs, () => _clock.LocalNowMs, _settings) { Enabled = false };
        Engine.ReanchorRequested += (pos, at) => _ = _conn.SendAsync("reanchor", new { position = pos, at });
        _conn.Message += OnMessage;
        _conn.Closed += reason =>
        {
            // A rejection closes with code 4001 and the error code as the reason; don't wait 10 s for a welcome.
            _welcome?.TrySetException(new TheatreException(0, $"connection closed ({reason})", reason));
            Closed?.Invoke(reason);
        };
    }

    /// <summary>Creates a screen on the theatre and joins it as host.</summary>
    public static async Task<(ScreenSession Session, CreateScreenResponse Created)> CreateAsync(
        TheatreApi api, DeviceIdentity identity, IPlayback player, CreateScreenRequest req, string displayName, CancellationToken ct = default)
    {
        var created = await api.CreateScreenAsync(req, ct);
        var session = new ScreenSession(player);
        await session.JoinOrDisposeAsync(api, identity, created.Code, created.ScreenId, displayName, password: null, claim: created.Claim, ct);
        return (session, created);
    }

    /// <summary>Resolves a code and joins the screen.</summary>
    public static async Task<ScreenSession> JoinByCodeAsync(
        TheatreApi api, DeviceIdentity identity, IPlayback player, string code, string displayName, string? password = null, CancellationToken ct = default)
    {
        var resolved = await api.ResolveAsync(code, ct) ?? throw new TheatreException(404, "screen not found");
        return await JoinResolvedAsync(api, identity, player, resolved, displayName, password, ct);
    }

    /// <summary>Joins a screen that has already been resolved (e.g. by <see cref="TheatreNetwork.ResolveAsync"/>).</summary>
    public static async Task<ScreenSession> JoinResolvedAsync(
        TheatreApi api, DeviceIdentity identity, IPlayback player, ResolveResponse resolved, string displayName, string? password = null, CancellationToken ct = default)
    {
        var session = new ScreenSession(player);
        await session.JoinOrDisposeAsync(api, identity, resolved.Code, resolved.ScreenId, displayName, password, claim: null, ct);
        return session;
    }

    private async Task JoinOrDisposeAsync(TheatreApi api, DeviceIdentity identity, string code, string screenId, string name,
                                          string? password, string? claim, CancellationToken ct)
    {
        try
        {
            await JoinAsync(api, identity, code, screenId, name, password, claim, ct);
        }
        catch
        {
            await DisposeAsync();
            throw;
        }
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
        CurrentItem = welcome.Item;
        Roles = welcome.Roles;
        CoHosts = welcome.CoHosts;
        SetViewers(welcome.Viewers, welcome.Self);

        // Clock-sync burst before the engine starts acting on anchors (it stays disabled until then).
        Engine.ApplyAnchor(welcome.Anchor);
        await PingBurstAsync(_settings.BurstSamples);
        Engine.Enabled = true;
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

    // ---- Roles, moderation, host actions (theatre checks permissions and hierarchy) ------------

    public Task CreateRoleAsync(string name, string? colour, IEnumerable<string> permissions, int? position = null) =>
        _conn.SendAsync("roles.create", new { name, colour, permissions = permissions.ToArray(), position });

    /// <summary>Custom roles: name, colour (null clears it), permissions and rank. The Viewer role: permissions only.</summary>
    public Task UpdateRoleAsync(string id, IEnumerable<string> permissions, (string Name, string? Colour, int Position)? details = null)
    {
        // A dictionary, so an explicit null colour is sent (records drop nulls when serialising).
        var data = new Dictionary<string, object?> { ["id"] = id, ["permissions"] = permissions.ToArray() };
        if (details is var (name, colour, position))
        {
            data["name"] = name;
            data["colour"] = colour;
            data["position"] = position;
        }
        return _conn.SendAsync("roles.update", data);
    }

    public Task DeleteRoleAsync(string id) => _conn.SendAsync("roles.delete", new { id });

    public Task AssignRoleAsync(string viewerId, string roleId, bool assigned) =>
        _conn.SendAsync("roles.assign", new { viewer = viewerId, role = roleId, assigned });

    public Task SetOverridesAsync(string viewerId, IEnumerable<string> allow, IEnumerable<string> deny) =>
        _conn.SendAsync("viewer.overrides", new { viewer = viewerId, allow = allow.ToArray(), deny = deny.ToArray() });

    public Task MuteAsync(string viewerId, bool muted) => _conn.SendAsync("viewer.mute", new { viewer = viewerId, muted });
    public Task KickAsync(string viewerId) => _conn.SendAsync("viewer.kick", new { viewer = viewerId });
    public Task BanAsync(string viewerId) => _conn.SendAsync("viewer.ban", new { viewer = viewerId });

    /// <summary>Host only: co-hosts in priority order.</summary>
    public Task SetCoHostsAsync(IEnumerable<string> viewerIds) => _conn.SendAsync("cohosts.set", new { viewers = viewerIds.ToArray() });

    public Task TransferHostAsync(string viewerId) => _conn.SendAsync("host.transfer", new { viewer = viewerId });
    public Task CloseScreenAsync() => _conn.SendAsync("screen.close");

    /// <summary>Host only: adds a co-host at the lowest priority, or removes one. Keeps co-hosts who aren't present.</summary>
    public Task SetCoHostAsync(string viewerId, bool coHost)
    {
        var ids = CoHosts.Select(c => c.Id).Where(id => id != viewerId).ToList();
        if (coHost) ids.Add(viewerId);
        return SetCoHostsAsync(ids);
    }

    /// <summary>Host only: moves a co-host up (-1) or down (+1) in priority.</summary>
    public Task MoveCoHostAsync(string viewerId, int delta)
    {
        var ids = CoHosts.Select(c => c.Id).ToList();
        int i = ids.IndexOf(viewerId), j = i + delta;
        if (i < 0 || j < 0 || j >= ids.Count) return Task.CompletedTask;
        (ids[i], ids[j]) = (ids[j], ids[i]);
        return SetCoHostsAsync(ids);
    }

    // ---- Internals -------------------------------------------------------------------

    private void SetViewers(IReadOnlyList<ViewerInfo> viewers, ViewerInfo? self)
    {
        Viewers = viewers;
        string? myId = self?.Id ?? Joined?.Self.Id;
        Self = viewers.FirstOrDefault(v => v.Id == myId) ?? self ?? Self;
        // Becoming acting host (or handing control back) switches the engine between correcting and re-anchoring.
        Engine.IsHost = Self?.IsTimelineAuthority == true;
    }

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
                // Safe before clock sync completes: the engine stays disabled until then.
                Engine.ApplyAnchor(a.Data);
                break;
            case ServerMessage.ItemMsg i:
                CurrentItem = i.Data;
                ItemChanged?.Invoke(i.Data);
                break;
            case ServerMessage.ViewersMsg v:
                SetViewers(v.Data, null);
                ViewersChanged?.Invoke(v.Data);
                break;
            case ServerMessage.RolesMsg r:
                Roles = r.Data;
                RolesChanged?.Invoke(r.Data);
                break;
            case ServerMessage.CoHostsMsg c:
                CoHosts = c.Data;
                CoHostsChanged?.Invoke(c.Data);
                break;
            case ServerMessage.ErrorMsg e:
                _welcome?.TrySetException(new TheatreException(0, e.Data.Message, e.Data.Code));
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

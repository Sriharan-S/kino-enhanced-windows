using System.Diagnostics;
using Kino.Core.Identity;
using Kino.Core.Net;
using Kino.Core.Protocol;
using Kino.Core.Sync;

namespace Kino.Core;

/// <summary>
/// A viewer's live session in one screen: connection (with automatic reconnect), clock sync and the sync engine loop.
/// Events are raised on background threads.
/// </summary>
public sealed class ScreenSession : IAsyncDisposable
{
    /// <summary>How long to keep trying after the connection drops. Matches the theatre's reconnect grace.</summary>
    public static readonly TimeSpan ReconnectWindow = TimeSpan.FromSeconds(60);

    private static readonly TimeSpan[] ReconnectDelays =
        [TimeSpan.FromMilliseconds(300), TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(5)];

    private ScreenConnection _conn = new();
    private readonly ClockSync _clock = new();
    private readonly SyncSettings _settings;
    private readonly CancellationTokenSource _cts = new();
    private Task? _loop;
    private int _reconnecting;
    private volatile bool _disposed;

    // Needed to reconnect.
    private TheatreApi? _api;
    private DeviceIdentity? _identity;
    private string _screenId = "";
    private string _name = "";

    public SyncEngine Engine { get; }
    public ClockSync Clock => _clock;
    public string Code { get; private set; } = "";
    public Welcome? Joined { get; private set; }
    public ScreenInfo? Screen { get; private set; }
    public Item? CurrentItem { get; private set; }
    public IReadOnlyList<Item> Playlist { get; private set; } = [];

    /// <summary>Subtitle files shared in this screen (per item).</summary>
    public IReadOnlyList<SubMeta> Subs { get; private set; } = [];
    public event Action<IReadOnlyList<SubMeta>>? SubsChanged;

    /// <summary>Names the screen is paused for under "wait for everyone", or null (spec §14.10).</summary>
    public IReadOnlyList<string>? Hold { get; private set; }
    public event Action<IReadOnlyList<string>?>? HoldChanged;

    /// <summary>The current or last ready check (spec §14.14).</summary>
    public ReadyCheck? Ready { get; private set; }
    public event Action<ReadyCheck?>? ReadyChanged;

    /// <summary>Set by the app when this viewer's file differs from the host's; reported as "mismatch".</summary>
    public bool FileMismatch { get; set; }

    private StatusReporter _status = new();

    /// <summary>Recent chat, oldest first (the theatre keeps the last 100).</summary>
    public IReadOnlyList<ChatMessage> Chat { get { lock (_chatLock) return [.. _chat]; } }
    private List<ChatMessage> _chat = [];
    private readonly Lock _chatLock = new();

    /// <summary>The fixed reaction set (spec §18).</summary>
    public static readonly string[] Reactions = ["👍", "❤️", "😂", "😮", "😢", "🔥", "👏", "🎉"];
    public IReadOnlyList<ViewerInfo> Viewers { get; private set; } = [];
    public IReadOnlyList<Role> Roles { get; private set; } = [];

    /// <summary>Co-hosts in priority order, including ones who aren't present.</summary>
    public IReadOnlyList<CoHostInfo> CoHosts { get; private set; } = [];

    /// <summary>This viewer's current entry. Roles, co-host status and permissions change during a session.</summary>
    public ViewerInfo? Self { get; private set; }

    public bool IsHost => Self?.IsHost == true;

    /// <summary>The host, or the acting host while the host is away: re-anchors instead of being corrected.</summary>
    public bool IsTimelineAuthority => Self?.IsTimelineAuthority == true;

    /// <summary>True while the connection has dropped and the session is trying to get back in.</summary>
    public bool IsReconnecting => Volatile.Read(ref _reconnecting) == 1;

    /// <summary>Convenience for enabling controls. The theatre enforces permissions regardless.</summary>
    public bool Can(string permission) => Self?.Can(permission) == true;

    public event Action<Item?>? ItemChanged;
    public event Action<IReadOnlyList<Item>>? PlaylistChanged;
    public event Action<ChatMessage>? ChatReceived;

    /// <summary>The whole history was replaced (join or reconnect).</summary>
    public event Action? ChatReset;

    public event Action<Reaction>? ReactionReceived;
    public event Action<IReadOnlyList<ViewerInfo>>? ViewersChanged;
    public event Action<IReadOnlyList<Role>>? RolesChanged;
    public event Action<IReadOnlyList<CoHostInfo>>? CoHostsChanged;
    public event Action<ScreenInfo>? ScreenChanged;
    public event Action<SyncStatus>? StatusChanged;
    public event Action<ServerError>? Error;

    /// <summary>The connection dropped; playback continues locally while reconnecting.</summary>
    public event Action? Reconnecting;

    public event Action? Reconnected;

    /// <summary>The session is over: rejected, kicked, banned, closed, replaced, or reconnecting gave up.</summary>
    public event Action<string>? Closed;

    public ScreenSession(IPlayback player, SyncSettings? settings = null)
    {
        _settings = settings ?? SyncSettings.Default;
        Engine = new SyncEngine(player, () => _clock.TheatreNowMs, () => _clock.LocalNowMs, _settings) { Enabled = false };
        Engine.ReanchorRequested += (pos, at) => _ = _conn.SendAsync("reanchor", new { position = pos, at });
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
            _api = api;
            _identity = identity;
            _screenId = screenId;
            _name = name;
            Code = code;

            var welcome = await ConnectAsync(password, claim, ct);
            ApplyWelcome(welcome, notify: false);

            // Clock-sync burst before the engine starts acting on anchors (it stays disabled until then).
            await PingBurstAsync(_settings.BurstSamples);
            Engine.Enabled = true;
            _loop = Task.Run(RunLoop);
        }
        catch
        {
            await DisposeAsync();
            throw;
        }
    }

    /// <summary>Opens a fresh connection, sends hello and waits for the welcome. Rejections throw with their error code.</summary>
    private async Task<Welcome> ConnectAsync(string? password, string? claim, CancellationToken ct)
    {
        var conn = new ScreenConnection();
        var welcome = new TaskCompletionSource<Welcome>(TaskCreationOptions.RunContinuationsAsynchronously);
        conn.Message += msg =>
        {
            if (msg is ServerMessage.WelcomeMsg w) welcome.TrySetResult(w.Data);
            else if (msg is ServerMessage.ErrorMsg e && !welcome.Task.IsCompleted) welcome.TrySetException(new TheatreException(0, e.Data.Message, e.Data.Code));
            if (conn == _conn) OnMessage(msg);
        };
        conn.Closed += info =>
        {
            // A rejection closes with 4001 and the error code as the reason; fail the join at once instead of timing out.
            welcome.TrySetException(new TheatreException(0, $"connection closed ({info.Reason})", info.Status == 4001 ? info.Reason : null));
            OnConnectionClosed(conn, info);
        };

        var old = _conn;
        _conn = conn;
        if (old != conn) _ = old.DisposeAsync().AsTask();

        await conn.ConnectAsync(_api!.WebSocketUrl(Code), ct);
        await conn.SendAsync("hello", new
        {
            protocol = ProtocolInfo.Version,
            token = _identity!.TokenFor(_screenId),
            name = _name,
            device = "windows",
            password,
            claim,
        });
        return await welcome.Task.WaitAsync(TimeSpan.FromSeconds(10), ct);
    }

    private void ApplyWelcome(Welcome w, bool notify)
    {
        Joined = w;
        Screen = w.Screen;
        Code = w.Screen.Code;
        CurrentItem = w.Item;
        Playlist = w.Playlist;
        Subs = w.Subs;
        Hold = w.Hold;
        Ready = w.Ready;
        _status = new StatusReporter(); // the theatre starts us at "sync" again
        lock (_chatLock) _chat = [.. w.Chat];
        Roles = w.Roles;
        CoHosts = w.CoHosts;
        SetViewers(w.Viewers, w.Self);
        Engine.ApplyAnchor(w.Anchor);
        if (!notify) return;
        ScreenChanged?.Invoke(w.Screen);
        PlaylistChanged?.Invoke(w.Playlist);
        ChatReset?.Invoke();
        SubsChanged?.Invoke(w.Subs);
        HoldChanged?.Invoke(w.Hold);
        ReadyChanged?.Invoke(w.Ready);
        ItemChanged?.Invoke(w.Item);
        RolesChanged?.Invoke(w.Roles);
        CoHostsChanged?.Invoke(w.CoHosts);
        ViewersChanged?.Invoke(w.Viewers);
    }

    private void OnConnectionClosed(ScreenConnection conn, ConnectionClosed info)
    {
        if (conn != _conn || _disposed || _loop is null) return; // stale socket, leaving, or still joining
        if (info.IsFinal)
        {
            Closed?.Invoke(info.Reason);
            return;
        }
        if (Interlocked.Exchange(ref _reconnecting, 1) == 0) _ = ReconnectLoopAsync(info.Reason);
    }

    /// <summary>
    /// Network drop: keep playing on the last anchor and rejoin with the same token (no password needed: the theatre
    /// remembers us). Gives up after <see cref="ReconnectWindow"/>, or at once on a real rejection.
    /// </summary>
    private async Task ReconnectLoopAsync(string reason)
    {
        Reconnecting?.Invoke();
        var started = Stopwatch.StartNew();
        for (int attempt = 0; !_disposed && started.Elapsed < ReconnectWindow; attempt++)
        {
            try
            {
                await Task.Delay(ReconnectDelays[Math.Min(attempt, ReconnectDelays.Length - 1)], _cts.Token);
                var welcome = await ConnectAsync(null, null, _cts.Token);
                ApplyWelcome(welcome, notify: true);
                Volatile.Write(ref _reconnecting, 0);
                Reconnected?.Invoke();
                _ = PingBurstAsync(_settings.ResyncSamples);
                return;
            }
            catch (OperationCanceledException) when (_disposed)
            {
                return;
            }
            catch (TheatreException e) when (e.Code is not null || e.Status == 404)
            {
                Volatile.Write(ref _reconnecting, 0);
                if (!_disposed) Closed?.Invoke(e.Code ?? "closed"); // banned, closed, full…
                return;
            }
            catch (Exception e) when (!_disposed)
            {
                reason = e.Message; // still offline; try again
            }
        }
        Volatile.Write(ref _reconnecting, 0);
        if (!_disposed) Closed?.Invoke($"couldn't reconnect ({reason})");
    }

    // ---- Commands (the theatre validates permissions) ---------------------------------

    public Task PlayAsync() => _conn.SendAsync("play");
    public Task PauseAsync() => _conn.SendAsync("pause");
    public Task SeekAsync(double seconds) => _conn.SendAsync("seek", new { to = seconds });

    public Task LoadLocalAsync(string fileName, double? duration) =>
        _conn.SendAsync("load", new { kind = "local", name = fileName, duration });

    public Task LoadUrlAsync(string url, string name, double? duration) =>
        _conn.SendAsync("load", new { kind = "url", name, duration, url });

    // ---- Playlist (§13) ----------------------------------------------------------------------

    /// <summary>A playlist entry to add. Local files share only their name and duration, never the path.</summary>
    public sealed record NewItem(string Kind, string Name, double? Duration, string? Url = null)
    {
        public static NewItem Local(string fileName, double? duration) => new("local", fileName, duration);
        public static NewItem Link(string url, string name, double? duration = null) => new("url", name, duration, url);
        public static NewItem YouTube(string videoId, string name, double? duration = null) => new("youtube", name, duration, Media.YouTube.WatchUrl(videoId));
    }

    public Task AddToPlaylistAsync(IEnumerable<NewItem> items, int? at = null) =>
        _conn.SendAsync("playlist.add", new { items = items.Select(i => new { kind = i.Kind, name = i.Name, duration = i.Duration, url = i.Url }).ToArray(), at });

    public Task RemoveItemAsync(string itemId) => _conn.SendAsync("playlist.remove", new { id = itemId });
    public Task MoveItemAsync(string itemId, int to) => _conn.SendAsync("playlist.move", new { id = itemId, to });
    public Task PlayItemAsync(string itemId) => _conn.SendAsync("playlist.play", new { id = itemId });
    /// <summary>YouTube items end when everyone has finished (spec §14.13): report that this player reached the end.</summary>
    public Task ReportEndedAsync(string itemId) => _conn.SendAsync("item.ended", new { id = itemId });

    public Task SkipAsync(bool next) => _conn.SendAsync("skip", new { direction = next ? "next" : "previous" });

    public Task SetAutoAdvanceAsync(bool on) => _conn.SendAsync("settings.update", new { autoAdvance = on });

    // ---- Chat (§18) ------------------------------------------------------------------------------

    public Task SendChatAsync(string text) => _conn.SendAsync("chat.send", new { text });

    /// <summary>Only the GIF's URL (and size) is sent; the theatre accepts URLs from GIF services only.</summary>
    public Task SendGifAsync(Gif gif) => _conn.SendAsync("chat.gif", new { url = gif.Url, width = gif.Width, height = gif.Height, title = gif.Title });

    public Task ReactAsync(string emoji) => _conn.SendAsync("react", new { emoji });

    // ---- Subtitles (§17) ----------------------------------------------------------------------------

    public static readonly string[] SubtitleFormats = ["srt", "vtt", "ass", "ssa"];

    /// <summary>"srt", "vtt", "ass" or "ssa" from the file name, or null if it isn't a supported subtitle file.</summary>
    public static string? SubtitleFormatOf(string path) =>
        Path.GetExtension(path).TrimStart('.').ToLowerInvariant() is var ext && SubtitleFormats.Contains(ext) ? ext : null;

    /// <summary>Shares a subtitle file for an item with everyone (needs <c>subtitles.share</c>). Only the file's name and contents are sent.</summary>
    public async Task<SubMeta> ShareSubtitleAsync(string itemId, string path, CancellationToken ct = default)
    {
        string format = SubtitleFormatOf(path) ?? throw new TheatreException(400, "subtitles must be .srt, .vtt, .ass or .ssa");
        byte[] content = await File.ReadAllBytesAsync(path, ct);
        return await _api!.UploadSubtitleAsync(Code, _identity!.TokenFor(_screenId), itemId, Path.GetFileName(path), format, content, ct);
    }

    public Task<byte[]> DownloadSubtitleAsync(SubMeta sub, CancellationToken ct = default) =>
        _api!.DownloadSubtitleAsync(Code, _identity!.TokenFor(_screenId), sub.Id, ct);

    public Task UnshareSubtitleAsync(string id) => _conn.SendAsync("subs.unshare", new { id });

    // ---- Buffering policy and ready check --------------------------------------------------------

    /// <summary>"Resume anyway" while the screen waits for someone (needs <c>playback.toggle</c>).</summary>
    public Task ReleaseHoldAsync() => _conn.SendAsync("hold.release");

    public Task StartReadyCheckAsync() => _conn.SendAsync("ready.start");
    public Task AnswerReadyAsync(long id, bool ready) => _conn.SendAsync("ready.answer", new { id, ready });

    public Task SetBufferingPolicyAsync(bool waitForEveryone) =>
        _conn.SendAsync("settings.update", new { bufferingPolicy = waitForEveryone ? "wait" : "keep" });

    // ---- Screen settings & invites -------------------------------------------------------

    /// <summary>Changes screen settings (<c>screen.settings</c>). Only the given fields change; a password is needed to go private.</summary>
    public Task UpdateSettingsAsync(string? name = null, bool? isPrivate = null, string? password = null, bool? hidden = null,
                                    int? maxViewers = null, bool? showDevices = null) =>
        _conn.SendAsync("settings.update", new
        {
            name,
            visibility = isPrivate is bool p ? (p ? "private" : "public") : null,
            password = string.IsNullOrEmpty(password) ? null : password,
            hidden,
            maxViewers,
            showDevices,
        });

    /// <summary>New screen code (<c>screen.invite</c>); the old code and links stop working.</summary>
    public Task RegenerateCodeAsync() => _conn.SendAsync("code.regenerate");

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
            case ServerMessage.PongMsg p:
                _clock.AddSample(p.Data.T0, p.Data.Ts, _clock.LocalNowMs);
                break;
            case ServerMessage.AnchorMsg a:
                // Safe before clock sync completes: the engine stays disabled until then.
                Engine.ApplyAnchor(a.Data);
                break;
            case ServerMessage.PlaylistMsg pl:
                Playlist = pl.Data;
                PlaylistChanged?.Invoke(pl.Data);
                break;
            case ServerMessage.ChatMsg c:
                lock (_chatLock)
                {
                    if (_chat.Any(m => m.Id == c.Data.Id)) break; // already in a welcome snapshot
                    _chat.Add(c.Data);
                    if (_chat.Count > 100) _chat.RemoveAt(0);
                }
                ChatReceived?.Invoke(c.Data);
                break;
            case ServerMessage.SubsMsg sb:
                Subs = sb.Data;
                SubsChanged?.Invoke(sb.Data);
                break;
            case ServerMessage.HoldMsg h:
                Hold = h.Data;
                HoldChanged?.Invoke(h.Data);
                break;
            case ServerMessage.ReadyMsg rc:
                Ready = rc.Data;
                ReadyChanged?.Invoke(rc.Data);
                break;
            case ServerMessage.ReactMsg r:
                ReactionReceived?.Invoke(r.Data);
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
            case ServerMessage.ScreenMsg sc:
                Screen = sc.Data;
                Code = sc.Data.Code;
                ScreenChanged?.Invoke(sc.Data);
                break;
            case ServerMessage.ErrorMsg e:
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
                if (!IsReconnecting && _status.Update(Engine.Status.Zone, FileMismatch, now) is { } status)
                    _ = _conn.SendAsync("status", new { value = status });

                if (!IsReconnecting && now - lastResync >= _settings.ResyncEvery.TotalMilliseconds)
                {
                    lastResync = now;
                    _ = PingBurstAsync(_settings.ResyncSamples);
                }
            }
        }
        catch (OperationCanceledException) { /* session ended */ }
    }

    /// <summary>Test hook: simulates a network drop, which should trigger a silent reconnect.</summary>
    internal void SimulateNetworkDrop() => _conn.Abort();

    /// <summary>Leaves the screen. Sends <c>leave</c> first so the theatre frees the seat at once (no reconnect grace).</summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        if (_loop is not null && _conn.IsOpen) await _conn.SendAsync("leave");
        _cts.Cancel();
        if (_loop is not null) await Task.WhenAny(_loop, Task.Delay(1000));
        await _conn.DisposeAsync();
    }
}

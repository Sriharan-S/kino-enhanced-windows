using System.Text.Json;
using System.Text.Json.Serialization;

namespace Kino.Core.Protocol;

/// <summary>Kino protocol v1 wire types. Spec: kino-enhanced/protocol/README.md.</summary>
public static class ProtocolInfo
{
    public const int Version = 1;

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}

public sealed record Anchor(string? ItemId, string State, double Position, double Rate, double At, long Seq)
{
    public bool IsPlaying => State == "playing";

    /// <summary>Expected media position (s) at theatre time <paramref name="nowMs"/>.</summary>
    public double ExpectedAt(double nowMs) =>
        !IsPlaying || nowMs <= At ? Position : Position + Rate * (nowMs - At) / 1000.0;
}

public sealed record Item(string Id, string Kind, string Name, double? Duration, string? Url);

public sealed record ViewerInfo(string Id, string Name, string? Device, bool IsHost, double? Rtt, bool Buffering)
{
    /// <summary>Custom role ids.</summary>
    public string[] Roles { get; init; } = [];

    /// <summary>1-based co-host priority, or null.</summary>
    public int? CoHost { get; init; }

    /// <summary>Standing in for the absent host.</summary>
    public bool ActingHost { get; init; }

    public bool Muted { get; init; }

    /// <summary>Per-viewer overrides: deny beats allow beats roles.</summary>
    public string[] Allow { get; init; } = [];
    public string[] Deny { get; init; } = [];

    /// <summary>Effective permissions, computed by the theatre.</summary>
    public string[] Permissions { get; init; } = [];

    public bool Can(string permission) => Permissions.Contains(permission);

    /// <summary>Defines the timeline: the host, or the acting host while the host is away.</summary>
    public bool IsTimelineAuthority => IsHost || ActingHost;
}

public sealed record Role(string Id, string Name, string? Colour, int Position, string[] Permissions, bool Builtin);

public sealed record ScreenInfo(string Code, string Name, string Visibility, int MaxViewers, bool ShowDevices);

public sealed record Welcome(ViewerInfo Self, ScreenInfo Screen, ViewerInfo[] Viewers, Item? Item, Anchor Anchor, double ServerTime)
{
    public Role[] Roles { get; init; } = [];
    public CoHostInfo[] CoHosts { get; init; } = [];
}

/// <summary>A co-host in priority order, present or not.</summary>
public sealed record CoHostInfo(string Id, string Name);

public sealed record Pong(double T0, double Ts);

public sealed record ServerError(string Code, string Message);

// ---- HTTP API ---------------------------------------------------------------------------

public sealed record TheatreInfo(string Id, string Name, int Protocol, int MinProtocol, string Status, double Time);

public sealed record CreateScreenRequest(string Name, string Visibility, string? Password = null, bool? Hidden = null, int? MaxViewers = null);

public sealed record CreateScreenResponse(string Theatre, string Code, string ScreenId, string Claim);

public sealed record ResolveResponse(string Code, string ScreenId, string Name, string Visibility, bool RequiresPassword);

public sealed record LobbyScreen(string Code, string Name, string Visibility, int Viewers, int MaxViewers, string? Host);

public sealed record LobbyResponse(TheatreRef Theatre, LobbyScreen[] Screens);

public sealed record TheatreRef(string Id, string Name);

/// <summary>A decoded theatre → client message. Unknown types are kept (and ignored) for forward compatibility.</summary>
public abstract record ServerMessage
{
    public sealed record WelcomeMsg(Welcome Data) : ServerMessage;
    public sealed record PongMsg(Pong Data) : ServerMessage;
    public sealed record AnchorMsg(Anchor Data) : ServerMessage;
    public sealed record ItemMsg(Item? Data) : ServerMessage;
    public sealed record ViewersMsg(ViewerInfo[] Data) : ServerMessage;
    public sealed record RolesMsg(Role[] Data) : ServerMessage;
    public sealed record CoHostsMsg(CoHostInfo[] Data) : ServerMessage;
    public sealed record ErrorMsg(ServerError Data) : ServerMessage;
    public sealed record Unknown(string Type) : ServerMessage;

    public static ServerMessage Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        string type = root.GetProperty("type").GetString() ?? "";
        var data = root.TryGetProperty("data", out var d) ? d : default;
        var o = ProtocolInfo.Json;

        return type switch
        {
            "welcome" => new WelcomeMsg(data.Deserialize<Welcome>(o)!),
            "pong" => new PongMsg(data.Deserialize<Pong>(o)!),
            "anchor" => new AnchorMsg(data.Deserialize<Anchor>(o)!),
            "item" => new ItemMsg(data.ValueKind == JsonValueKind.Null ? null : data.Deserialize<Item>(o)),
            "viewers" => new ViewersMsg(data.Deserialize<ViewerInfo[]>(o)!),
            "roles" => new RolesMsg(data.Deserialize<Role[]>(o)!),
            "cohosts" => new CoHostsMsg(data.Deserialize<CoHostInfo[]>(o)!),
            "error" => new ErrorMsg(data.Deserialize<ServerError>(o)!),
            _ => new Unknown(type),
        };
    }
}

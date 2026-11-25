using Kino.Core.Protocol;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Kino;

internal static class RoleColours
{
    public const string Host = "#d64545";

    /// <summary>Choices offered in the role editor (null = no colour).</summary>
    public static readonly (string Label, string? Hex)[] Presets =
    [
        ("None", null), ("Red", "#d64545"), ("Orange", "#e8a33d"), ("Yellow", "#d9c23a"), ("Green", "#3dae5b"),
        ("Teal", "#2fa6a0"), ("Blue", "#3d8be8"), ("Purple", "#8f5bd6"), ("Pink", "#d65ba6"),
    ];

    public static Brush Brush(string? hex)
    {
        if (hex is not { Length: 7 } || hex[0] != '#') return new SolidColorBrush(Colors.Gray);
        byte C(int i) => Convert.ToByte(hex.Substring(i, 2), 16);
        return new SolidColorBrush(Color.FromArgb(255, C(1), C(3), C(5)));
    }
}

/// <summary>Display model for one viewer in the side panel (x:Bind source).</summary>
public sealed class ViewerRow
{
    public ViewerRow(ViewerInfo viewer, IReadOnlyList<Role> roles, bool isSelf)
    {
        Viewer = viewer;
        Name = viewer.Name + (isSelf ? " (you)" : "");

        var custom = roles.Where(r => !r.Builtin && viewer.Roles.Contains(r.Id)).OrderByDescending(r => r.Position).ToList();
        var badges = new List<string>();
        if (viewer.IsHost) badges.Add("Host");
        if (viewer.ActingHost) badges.Add("Acting host");
        if (viewer.CoHost is int n) badges.Add($"Co-host #{n}");
        badges.AddRange(custom.Select(r => r.Name));
        Badges = string.Join(" · ", badges);

        string? colour = viewer.IsHost ? RoleColours.Host
            : viewer.CoHost is not null || viewer.ActingHost ? roles.FirstOrDefault(r => r.Id == Permissions.CoHostRole)?.Colour
            : custom.FirstOrDefault(r => r.Colour is not null)?.Colour;
        ColourBrush = RoleColours.Brush(colour);

        var detail = new List<string>();
        if (viewer.Device is { } d) detail.Add(char.ToUpperInvariant(d[0]) + d[1..]);
        detail.Add(viewer.Rtt is double rtt ? $"{rtt:0} ms" : "– ms");
        if (viewer.Buffering) detail.Add("buffering");
        Detail = string.Join(" · ", detail);
    }

    public ViewerInfo Viewer { get; }
    public string Name { get; }
    public string Badges { get; }
    public string Detail { get; }
    public Brush ColourBrush { get; }
    public Visibility BadgesVisibility => Badges.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    public Visibility MutedVisibility => Viewer.Muted ? Visibility.Visible : Visibility.Collapsed;
}

/// <summary>Display model for one role in the roles page (x:Bind source).</summary>
public sealed class RoleRow(Role role)
{
    public Role Role { get; } = role;
    public string Name => Role.Name;

    public string Detail => Role.Id switch
    {
        Permissions.CoHostRole => "Built-in · every permission · set by the host",
        Permissions.ViewerRole => $"Built-in · everyone · {Role.Permissions.Length} permissions",
        _ => $"Rank {Role.Position} · {Role.Permissions.Length} permission{(Role.Permissions.Length == 1 ? "" : "s")}",
    };

    public Brush ColourBrush => RoleColours.Brush(Role.Colour);
}

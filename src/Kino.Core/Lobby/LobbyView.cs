using Kino.Core.Net;
using Kino.Core.Protocol;

namespace Kino.Core.Lobby;

/// <summary>One row of the unified lobby: a screen plus the theatre it lives on (spec §10.2).</summary>
public sealed record LobbyEntry(TheatreEntry Theatre, LobbyScreen Screen, double? PingMs)
{
    public bool IsPrivate => Screen.Visibility == "private";
    public bool IsFull => Screen.Viewers >= Screen.MaxViewers;

    /// <summary>Stable identity across refreshes: codes are unique within a theatre.</summary>
    public string Key => $"{Theatre.Id}/{Screen.Code}";
}

public enum LobbySort { Viewers, Ping, Name }

public enum VisibilityFilter { All, Public, Private }

public sealed record LobbyQuery(
    string Search = "",
    VisibilityFilter Visibility = VisibilityFilter.All,
    string? TheatreId = null,
    LobbySort Sort = LobbySort.Viewers);

/// <summary>Theatre-level status for the lobby's status strip.</summary>
public sealed record TheatreStatus(TheatreEntry Theatre, double? PingMs, string? Failure);

/// <summary>Merges per-theatre lobby results and applies search, filters and sort. Pure, so it is unit tested.</summary>
public static class LobbyView
{
    /// <param name="pings">Best known ping per theatre id. Used when a theatre's own request time is unknown.</param>
    public static IReadOnlyList<LobbyEntry> Merge(IEnumerable<TheatreLobby> lobbies, IReadOnlyDictionary<string, double>? pings = null) =>
        lobbies
            .Where(l => l.Available)
            .SelectMany(l => l.Screens.Select(s => new LobbyEntry(l.Theatre, s, Ping(l, pings))))
            .ToList();

    public static IReadOnlyList<TheatreStatus> Statuses(IEnumerable<TheatreLobby> lobbies, IReadOnlyDictionary<string, double>? pings = null) =>
        lobbies.Select(l => new TheatreStatus(l.Theatre, l.Available ? Ping(l, pings) : null, l.Failure)).ToList();

    private static double? Ping(TheatreLobby l, IReadOnlyDictionary<string, double>? pings) =>
        pings is not null && pings.TryGetValue(l.Theatre.Id, out var p) ? p : l.RequestMs;

    public static IReadOnlyList<LobbyEntry> Apply(IEnumerable<LobbyEntry> entries, LobbyQuery q)
    {
        var search = q.Search.Trim();
        var filtered = entries.Where(e =>
            (q.Visibility == VisibilityFilter.All || e.IsPrivate == (q.Visibility == VisibilityFilter.Private)) &&
            (q.TheatreId is null || e.Theatre.Id == q.TheatreId) &&
            (search.Length == 0 ||
             e.Screen.Name.Contains(search, StringComparison.CurrentCultureIgnoreCase) ||
             (e.Screen.Host?.Contains(search, StringComparison.CurrentCultureIgnoreCase) ?? false) ||
             e.Screen.Code.Equals(search, StringComparison.OrdinalIgnoreCase)));

        // Ties break by name, then key, so the order is stable between refreshes.
        IOrderedEnumerable<LobbyEntry> sorted = q.Sort switch
        {
            LobbySort.Ping => filtered.OrderBy(e => e.PingMs ?? double.MaxValue),
            LobbySort.Name => filtered.OrderBy(e => e.Screen.Name, StringComparer.CurrentCultureIgnoreCase),
            _ => filtered.OrderBy(e => e.IsFull).ThenByDescending(e => e.Screen.Viewers),
        };
        return sorted
            .ThenBy(e => e.Screen.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(e => e.Key, StringComparer.Ordinal)
            .ToList();
    }
}

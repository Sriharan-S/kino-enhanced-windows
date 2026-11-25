using System.Text.Json;
using Kino.Core.Protocol;

namespace Kino.Core.Net;

/// <summary>One theatre as listed in <c>servers.json</c> (spec §4.2).</summary>
public sealed record TheatreEntry(string Id, string Name, string Url)
{
    public Uri Uri => new(Url);
}

public sealed record DirectoryFile(int Version, IReadOnlyList<TheatreEntry> Theatres);

public enum DirectorySource { Live, Cached, Bundled, Override }

public sealed record TheatreList(IReadOnlyList<TheatreEntry> Theatres, DirectorySource Source);

/// <summary>
/// Loads the theatre list: live <c>servers.json</c>, else the last good cached copy, else the list bundled in the app.
/// </summary>
public static class TheatreDirectory
{
    public const int SupportedVersion = 1;

    public static readonly Uri DefaultUrl = new("https://kino-directory.srie.workers.dev/servers.json");

    /// <summary>Used when the directory has never been reachable from this device.</summary>
    public static readonly DirectoryFile Bundled = new(1,
    [
        new("aurora", "Aurora", "https://kino-aurora.srie.workers.dev"),
        new("nova", "Nova", "https://kino-nova.sri-me.workers.dev"),
    ]);

    public static string DefaultCachePath => KinoPaths.For("servers.json");

    public static async Task<TheatreList> LoadAsync(HttpClient http, Uri url, string cachePath, CancellationToken ct = default)
    {
        try
        {
            string json = await http.GetStringAsync(url, ct);
            if (Parse(json) is { } live)
            {
                TryWriteCache(cachePath, json);
                return new(live.Theatres, DirectorySource.Live);
            }
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            // Offline, blocked or timed out: fall through to the cache.
        }

        try
        {
            if (File.Exists(cachePath) && Parse(await File.ReadAllTextAsync(cachePath, ct)) is { } cached)
                return new(cached.Theatres, DirectorySource.Cached);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }

        return new(Bundled.Theatres, DirectorySource.Bundled);
    }

    /// <summary>Parses and validates a directory file. Returns null if it is unusable.</summary>
    public static DirectoryFile? Parse(string json)
    {
        DirectoryFile? file;
        try { file = JsonSerializer.Deserialize<DirectoryFile>(json, ProtocolInfo.Json); }
        catch (JsonException) { return null; }

        if (file is null || file.Version != SupportedVersion || file.Theatres is null) return null;

        var valid = file.Theatres
            .Where(t => t is { Id.Length: > 0, Name.Length: > 0, Url: not null } && IsAllowedUrl(t.Url))
            .DistinctBy(t => t.Id)
            .ToList();
        return valid.Count == 0 ? null : file with { Theatres = valid };
    }

    /// <summary>HTTPS only, except loopback for local development.</summary>
    public static bool IsAllowedUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) &&
        (u.Scheme == Uri.UriSchemeHttps || (u.Scheme == Uri.UriSchemeHttp && u.IsLoopback));

    private static void TryWriteCache(string path, string json)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, path, overwrite: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

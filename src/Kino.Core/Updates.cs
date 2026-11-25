using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;

namespace Kino.Core;

/// <summary>
/// Release versions and the update check. Releases are published on the Windows repo's GitHub Releases page; the newest
/// stable one is "latest" (pre-releases such as 0.2.0-beta.1 never are).
/// </summary>
public static class Updates
{
    public const string Repository = "Sriharan-S/kino-enhanced-windows";
    public static readonly Uri LatestReleasePage = new($"https://github.com/{Repository}/releases/latest");
    private static readonly Uri LatestApi = new($"https://api.github.com/repos/{Repository}/releases/latest");

    /// <summary>This build's version, e.g. "0.1.0" (from Directory.Build.props).</summary>
    public static string CurrentVersion { get; } =
        (Assembly.GetEntryAssembly() ?? typeof(Updates).Assembly).GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion.Split('+')[0] ?? "0.0.0";

    public sealed record Release(string Version, Uri Page);

    /// <summary>The latest stable release if it is newer than <paramref name="current"/>, else null. Never throws for network errors.</summary>
    public static async Task<Release?> NewerReleaseAsync(HttpClient http, string current, CancellationToken ct = default)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, LatestApi);
            req.Headers.UserAgent.Add(new ProductInfoHeaderValue("Kino", current));
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            using var res = await http.SendAsync(req, ct);
            if (!res.IsSuccessStatusCode) return null; // 404 until the first release exists
            using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
            return Parse(doc.RootElement, current);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException)
        {
            return null;
        }
    }

    internal static Release? Parse(JsonElement release, string current)
    {
        if (release.TryGetProperty("draft", out var d) && d.GetBoolean()) return null;
        if (release.TryGetProperty("prerelease", out var p) && p.GetBoolean()) return null;
        string? tag = release.TryGetProperty("tag_name", out var t) ? t.GetString() : null;
        string? url = release.TryGetProperty("html_url", out var u) ? u.GetString() : null;
        if (tag is null || !SemVer.TryParse(tag, out _) || SemVer.Compare(tag, current) <= 0) return null;
        return new Release(tag.TrimStart('v', 'V'), url is not null && Uri.TryCreate(url, UriKind.Absolute, out var page) ? page : LatestReleasePage);
    }
}

/// <summary>Semantic Versioning 2.0 precedence (build metadata ignored).</summary>
public readonly record struct SemVer(int Major, int Minor, int Patch, string[] Pre)
{
    public static bool TryParse(string text, out SemVer v)
    {
        v = default;
        string s = text.Trim().TrimStart('v', 'V').Split('+')[0];
        string[] head = s.Split('-', 2);
        string[] nums = head[0].Split('.');
        if (nums.Length != 3 || !int.TryParse(nums[0], out int a) || !int.TryParse(nums[1], out int b) || !int.TryParse(nums[2], out int c)) return false;
        v = new SemVer(a, b, c, head.Length > 1 ? head[1].Split('.') : []);
        return true;
    }

    /// <summary>Negative if a &lt; b. Unparseable versions sort lowest.</summary>
    public static int Compare(string a, string b)
    {
        bool okA = TryParse(a, out var x), okB = TryParse(b, out var y);
        if (!okA || !okB) return okA.CompareTo(okB);
        int c = x.Major.CompareTo(y.Major);
        if (c == 0) c = x.Minor.CompareTo(y.Minor);
        if (c == 0) c = x.Patch.CompareTo(y.Patch);
        if (c != 0) return c;
        if (x.Pre.Length == 0 || y.Pre.Length == 0) return y.Pre.Length.CompareTo(x.Pre.Length); // a release beats its pre-releases
        for (int i = 0; i < Math.Min(x.Pre.Length, y.Pre.Length); i++)
        {
            bool nx = int.TryParse(x.Pre[i], out int ix), ny = int.TryParse(y.Pre[i], out int iy);
            c = nx && ny ? ix.CompareTo(iy) : nx ? -1 : ny ? 1 : string.CompareOrdinal(x.Pre[i], y.Pre[i]);
            if (c != 0) return c;
        }
        return x.Pre.Length.CompareTo(y.Pre.Length);
    }
}

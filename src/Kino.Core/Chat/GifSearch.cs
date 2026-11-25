using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Kino.Core.Protocol;

namespace Kino.Core.Chat;

/// <summary>One search result: a small preview to show in the picker, and the GIF to send.</summary>
public sealed record GifResult(string PreviewUrl, Gif Gif);

/// <summary>
/// GIF search via the GIPHY API, called directly by the client: GIF traffic never touches the theatre (spec §18, §20).
/// Needs a free GIPHY API key (developers.giphy.com), kept in <see cref="KinoSettings"/>.
/// </summary>
public sealed class GiphyClient(string apiKey, HttpClient? http = null)
{
    private static readonly Uri Base = new("https://api.giphy.com/v1/gifs/");
    private readonly HttpClient _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(8) };

    public Task<IReadOnlyList<GifResult>> TrendingAsync(int limit = 24, CancellationToken ct = default) =>
        QueryAsync($"trending?api_key={Uri.EscapeDataString(apiKey)}&limit={limit}&rating=pg-13", ct);

    public Task<IReadOnlyList<GifResult>> SearchAsync(string query, int limit = 24, CancellationToken ct = default) =>
        QueryAsync($"search?api_key={Uri.EscapeDataString(apiKey)}&q={Uri.EscapeDataString(query)}&limit={limit}&rating=pg-13&lang=en", ct);

    private async Task<IReadOnlyList<GifResult>> QueryAsync(string relative, CancellationToken ct)
    {
        var response = await _http.GetFromJsonAsync<Response>(new Uri(Base, relative), Json, ct);
        return Parse(response);
    }

    internal static IReadOnlyList<GifResult> Parse(Response? response) =>
        (response?.Data ?? [])
            .Select(d => (d, preview: d.Images?.FixedWidthSmall ?? d.Images?.FixedWidth, send: d.Images?.FixedHeight ?? d.Images?.Original))
            .Where(x => x.preview?.Url is not null && x.send?.Url is not null)
            .Select(x => new GifResult(x.preview!.Url!, new Gif(x.send!.Url!, ParseInt(x.send.Width), ParseInt(x.send.Height), string.IsNullOrWhiteSpace(x.d.Title) ? null : x.d.Title)))
            .ToList();

    private static int? ParseInt(string? s) => int.TryParse(s, out int v) && v > 0 ? v : null;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    internal sealed record Response(Datum[]? Data);
    internal sealed record Datum(string? Title, Images? Images);
    internal sealed record Images(
        [property: JsonPropertyName("fixed_height")] Rendition? FixedHeight,
        [property: JsonPropertyName("fixed_width")] Rendition? FixedWidth,
        [property: JsonPropertyName("fixed_width_small")] Rendition? FixedWidthSmall,
        Rendition? Original);
    internal sealed record Rendition(string? Url, string? Width, string? Height);

    /// <summary>Same allow-list as the theatre: https GIF links from GIPHY, Tenor or KLIPY media hosts.</summary>
    public static bool IsGifLink(string text) =>
        Uri.TryCreate(text.Trim(), UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps && u.IsDefaultPort &&
        string.IsNullOrEmpty(u.UserInfo) &&
        (System.Text.RegularExpressions.Regex.IsMatch(u.Host, @"^media[0-9]?\.giphy\.com$") || u.Host is "i.giphy.com" or "media.tenor.com" or "c.tenor.com" or "static.klipy.com");
}

/// <summary>Per-user app settings in %LOCALAPPDATA%\Kino\settings.json (not per profile).</summary>
public sealed class KinoSettings
{
    public string? GiphyApiKey { get; set; }

    private static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Kino", "settings.json");
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static KinoSettings Load()
    {
        try
        {
            var settings = File.Exists(FilePath) ? JsonSerializer.Deserialize<KinoSettings>(File.ReadAllText(FilePath), Json) ?? new() : new();
            settings.GiphyApiKey ??= Environment.GetEnvironmentVariable("KINO_GIPHY_KEY");
            return settings;
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            return new KinoSettings { GiphyApiKey = Environment.GetEnvironmentVariable("KINO_GIPHY_KEY") };
        }
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Json));
    }
}

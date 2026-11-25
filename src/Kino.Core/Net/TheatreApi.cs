using System.Net.Http.Json;
using Kino.Core.Protocol;

namespace Kino.Core.Net;

/// <summary>HTTP calls to one theatre (protocol §2).</summary>
public sealed class TheatreApi(Uri baseUrl, HttpClient? http = null)
{
    private readonly HttpClient _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

    public Uri BaseUrl { get; } = baseUrl;

    public Task<TheatreInfo?> InfoAsync(CancellationToken ct = default) =>
        _http.GetFromJsonAsync<TheatreInfo>(new Uri(BaseUrl, "/info"), ProtocolInfo.Json, ct);

    public Task<LobbyResponse?> LobbyAsync(CancellationToken ct = default) =>
        _http.GetFromJsonAsync<LobbyResponse>(new Uri(BaseUrl, "/lobby"), ProtocolInfo.Json, ct);

    public async Task<CreateScreenResponse> CreateScreenAsync(CreateScreenRequest req, CancellationToken ct = default)
    {
        using var res = await _http.PostAsJsonAsync(new Uri(BaseUrl, "/screens"), req, ProtocolInfo.Json, ct);
        await EnsureOk(res, ct);
        return (await res.Content.ReadFromJsonAsync<CreateScreenResponse>(ProtocolInfo.Json, ct))!;
    }

    public async Task<ResolveResponse?> ResolveAsync(string code, CancellationToken ct = default)
    {
        using var res = await _http.GetAsync(new Uri(BaseUrl, $"/resolve/{Uri.EscapeDataString(code)}"), ct);
        if (res.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        await EnsureOk(res, ct);
        return await res.Content.ReadFromJsonAsync<ResolveResponse>(ProtocolInfo.Json, ct);
    }

    /// <summary>Shares a subtitle file: gzip-compressed, at most 1 MB (spec §17.2). Needs <c>subtitles.share</c>.</summary>
    public async Task<SubMeta> UploadSubtitleAsync(string code, string token, string itemId, string name, string format, byte[] content, CancellationToken ct = default)
    {
        byte[] gz = Gzip(content);
        if (gz.Length > MaxSubtitleBytes) throw new TheatreException(413, "subtitle file is too big (over 1 MB compressed)");
        var url = new Uri(BaseUrl, $"/screens/{Uri.EscapeDataString(code)}/subs?item={Uri.EscapeDataString(itemId)}&format={Uri.EscapeDataString(format)}&name={Uri.EscapeDataString(name)}");
        using var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = new ByteArrayContent(gz) };
        req.Headers.Authorization = new("Bearer", token);
        using var res = await _http.SendAsync(req, ct);
        await EnsureOk(res, ct);
        return (await res.Content.ReadFromJsonAsync<SubMeta>(ProtocolInfo.Json, ct))!;
    }

    /// <summary>Downloads and decompresses a shared subtitle file. Members only.</summary>
    public async Task<byte[]> DownloadSubtitleAsync(string code, string token, string id, CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, new Uri(BaseUrl, $"/screens/{Uri.EscapeDataString(code)}/subs/{Uri.EscapeDataString(id)}"));
        req.Headers.Authorization = new("Bearer", token);
        using var res = await _http.SendAsync(req, ct);
        await EnsureOk(res, ct);
        await using var gz = new System.IO.Compression.GZipStream(await res.Content.ReadAsStreamAsync(ct), System.IO.Compression.CompressionMode.Decompress);
        using var ms = new MemoryStream();
        await gz.CopyToAsync(ms, ct);
        return ms.ToArray();
    }

    public const int MaxSubtitleBytes = 1024 * 1024;

    public static byte[] Gzip(byte[] data)
    {
        using var ms = new MemoryStream();
        using (var gz = new System.IO.Compression.GZipStream(ms, System.IO.Compression.CompressionLevel.SmallestSize)) gz.Write(data);
        return ms.ToArray();
    }

    public Uri WebSocketUrl(string code)
    {
        var b = new UriBuilder(new Uri(BaseUrl, $"/screens/{Uri.EscapeDataString(code)}/ws"));
        b.Scheme = b.Scheme == "https" ? "wss" : "ws";
        return b.Uri;
    }

    private static async Task EnsureOk(HttpResponseMessage res, CancellationToken ct)
    {
        if (res.IsSuccessStatusCode) return;
        string body = await res.Content.ReadAsStringAsync(ct);
        throw new TheatreException((int)res.StatusCode, body);
    }
}

/// <summary>An HTTP error (<see cref="Status"/> set) or a WebSocket rejection (<see cref="Code"/> set, e.g. "wrong_password").</summary>
public sealed class TheatreException(int status, string message, string? code = null)
    : Exception(code is null ? $"Theatre returned {status}: {message}" : message)
{
    public int Status { get; } = status;
    public string? Code { get; } = code;
}

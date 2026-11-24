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

public sealed class TheatreException(int status, string body) : Exception($"Theatre returned {status}: {body}")
{
    public int Status { get; } = status;
}

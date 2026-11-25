using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Kino.Core.Protocol;

namespace Kino.Core.Net;

/// <summary>How a connection ended. <see cref="Status"/> is the WebSocket close code, if the theatre sent one.</summary>
public sealed record ConnectionClosed(int? Status, string Reason)
{
    /// <summary>4001 = rejected (reason is the error code), 4000 = replaced by a newer connection. Don't reconnect.</summary>
    public bool IsFinal => Status is 4000 or 4001;
}

/// <summary>One WebSocket to a screen. Raises decoded messages on a background thread. Not reusable.</summary>
public sealed class ScreenConnection : IAsyncDisposable
{
    private readonly ClientWebSocket _ws = new();
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly CancellationTokenSource _cts = new();
    private Task? _receiveLoop;

    public event Action<ServerMessage>? Message;
    public event Action<ConnectionClosed>? Closed;

    public bool IsOpen => _ws.State == WebSocketState.Open;

    /// <summary>Throws <see cref="TheatreException"/> with the HTTP status if the theatre refused the upgrade (e.g. 404).</summary>
    public async Task ConnectAsync(Uri url, CancellationToken ct = default)
    {
        _ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(30);
        _ws.Options.CollectHttpResponseDetails = true;
        try
        {
            await _ws.ConnectAsync(url, ct);
        }
        catch (WebSocketException e) when (_ws.HttpStatusCode != 0)
        {
            throw new TheatreException((int)_ws.HttpStatusCode, e.Message);
        }
        _receiveLoop = Task.Run(ReceiveLoop);
    }

    public async Task SendAsync(string type, object? data = null)
    {
        if (!IsOpen) return;
        string json = JsonSerializer.Serialize(new { type, data = data ?? new { } }, ProtocolInfo.Json);
        var bytes = Encoding.UTF8.GetBytes(json);
        await _sendLock.WaitAsync();
        try
        {
            await _ws.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, _cts.Token);
        }
        catch (Exception) when (!IsOpen) { /* closing */ }
        finally
        {
            _sendLock.Release();
        }
    }

    private async Task ReceiveLoop()
    {
        var buffer = new byte[64 * 1024];
        var message = new MemoryStream();
        string reason = "closed";
        int? status = null;
        try
        {
            while (!_cts.IsCancellationRequested && _ws.State == WebSocketState.Open)
            {
                var result = await _ws.ReceiveAsync(buffer, _cts.Token);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    reason = _ws.CloseStatusDescription ?? "closed";
                    status = _ws.CloseStatus is { } cs ? (int)cs : null;
                    break;
                }
                message.Write(buffer, 0, result.Count);
                if (!result.EndOfMessage) continue;

                string text = Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length);
                message.SetLength(0);

                ServerMessage msg;
                try { msg = ServerMessage.Parse(text); }
                catch (JsonException) { continue; } // ignore malformed messages
                Message?.Invoke(msg);
            }
        }
        catch (OperationCanceledException) { reason = "disconnected"; }
        catch (WebSocketException e) { reason = e.Message; }
        Closed?.Invoke(new ConnectionClosed(status, reason));
    }

    /// <summary>Test hook: drops the connection the way a network failure would (no close handshake).</summary>
    internal void Abort() => _ws.Abort();

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_ws.State == WebSocketState.Open)
                await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(2));
        }
        catch (Exception) { /* best effort */ }
        _cts.Cancel();
        if (_receiveLoop is not null) await Task.WhenAny(_receiveLoop, Task.Delay(2000));
        _ws.Dispose();
    }
}

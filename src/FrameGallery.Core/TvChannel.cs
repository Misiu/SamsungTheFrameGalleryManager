using System.Buffers;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace FrameGallery.Core;

/// <summary>
/// A WebSocket channel to the TV: wss://ip:8002/api/v2/channels/{channel}. Used for pairing (samsung.remote.control)
/// and for Art Mode (com.samsung.art-app). Reading is sequential: the caller pulls messages one by one.
/// </summary>
public sealed class TvChannel : IAsyncDisposable
{
    public const string RemoteChannel = "samsung.remote.control";
    public const string ArtChannel = "com.samsung.art-app";
    const string AppName = "FrameGallery";
    const int MaxMessageBytes = 8 * 1024 * 1024;

    readonly ClientWebSocket _ws;
    public string? Token { get; private set; }

    TvChannel(ClientWebSocket ws) => _ws = ws;

    /// <param name="handshakeTimeout">On first pairing allow time for the user to press "Allow" on the TV (e.g. 60 s).</param>
    public static async Task<TvChannel> ConnectAsync(string ip, string channel, string? token, TlsPolicy tls,
        bool waitReady, TimeSpan handshakeTimeout, CancellationToken ct)
    {
        Lan.RequirePrivateIPv4(ip);
        var ws = new ClientWebSocket();
        ws.Options.RemoteCertificateValidationCallback = tls.Validate;
        var name = Uri.EscapeDataString(Convert.ToBase64String(Encoding.UTF8.GetBytes(AppName)));
        var url = $"wss://{ip}:8002/api/v2/channels/{channel}?name={name}"
                  + (string.IsNullOrEmpty(token) ? "" : $"&token={Uri.EscapeDataString(token)}");
        TvLog.Write($"[WS] connecting {channel} to {ip} (token: {(string.IsNullOrEmpty(token) ? "none" : "present")})");

        var ch = new TvChannel(ws);
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(handshakeTimeout);
            await ws.ConnectAsync(new Uri(url), cts.Token);
            await ch.HandshakeAsync(waitReady, cts.Token);
            return ch;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            await ch.DisposeAsync();
            throw new TvException("Err_HandshakeTimeout");
        }
        catch (WebSocketException e)
        {
            await ch.DisposeAsync();
            throw new TvException("Err_ConnectFailed", e.Message);
        }
        catch { await ch.DisposeAsync(); throw; }
    }

    async Task HandshakeAsync(bool waitReady, CancellationToken ct)
    {
        var connected = false;
        while (true)
        {
            var m = await ReceiveAsync(ct);
            switch (Event(m))
            {
                case "ms.channel.connect":
                    connected = true;
                    if (m.TryGetProperty("data", out var d) && d.ValueKind == JsonValueKind.Object
                        && d.TryGetProperty("token", out var t) && t.ValueKind == JsonValueKind.String)
                        Token = t.GetString();
                    TvLog.Write($"[WS] connect (token {(Token is null ? "not sent" : "received")})");
                    if (!waitReady) return;
                    break;
                case "ms.channel.ready":
                    TvLog.Write("[WS] ready");
                    if (connected || waitReady) return;
                    break;
                case "ms.channel.unauthorized":
                    throw new TvException("Err_Unauthorized");
                case "ms.channel.timeOut":
                    throw new TvException("Err_ChannelTimeout");
            }
        }
    }

    public static string? Event(JsonElement m) =>
        m.ValueKind == JsonValueKind.Object && m.TryGetProperty("event", out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() : null;

    /// <summary>The next JSON text message (binary and malformed messages are skipped).</summary>
    public async Task<JsonElement> ReceiveAsync(CancellationToken ct)
    {
        while (true)
        {
            var buf = new ArrayBufferWriter<byte>();
            ValueWebSocketReceiveResult r;
            do
            {
                try { r = await _ws.ReceiveAsync(buf.GetMemory(8192), ct); }
                catch (WebSocketException e) { throw new TvConnectionLostException("Err_ConnLost", e.Message); }
                if (r.MessageType == WebSocketMessageType.Close) throw new TvConnectionLostException("Err_ConnClosed");
                buf.Advance(r.Count);
                if (buf.WrittenCount > MaxMessageBytes) throw new TvException("Err_MessageTooLarge");
            } while (!r.EndOfMessage);

            if (r.MessageType != WebSocketMessageType.Text) { TvLog.Trace($"WS << (binary, {buf.WrittenCount} B, skipped)"); continue; }
            TvLog.Trace($"WS << {Encoding.UTF8.GetString(buf.WrittenSpan)}");
            try { using var doc = JsonDocument.Parse(buf.WrittenMemory); return doc.RootElement.Clone(); }
            catch (JsonException) { TvLog.Write("[WS] skipped a malformed JSON message"); }
        }
    }

    public async Task SendJsonAsync(object message, CancellationToken ct)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(message);
        TvLog.Trace($"WS >> {Encoding.UTF8.GetString(bytes)}");
        try { await _ws.SendAsync(bytes, WebSocketMessageType.Text, true, ct); }
        catch (Exception e) when (e is WebSocketException or InvalidOperationException or ObjectDisposedException)
        {
            throw new TvConnectionLostException("Err_ConnLost", e.Message);
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_ws.State == WebSocketState.Open)
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, null, cts.Token);
            }
        }
        catch { /* best-effort close */ }
        _ws.Dispose();
    }
}

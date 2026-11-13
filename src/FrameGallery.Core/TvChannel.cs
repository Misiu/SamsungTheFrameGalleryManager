using System.Buffers;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace FrameGallery.Core;

/// <summary>
/// Kanał WebSocket TV: wss://ip:8002/api/v2/channels/{kanał}. Używany do parowania (samsung.remote.control)
/// i do Art Mode (com.samsung.art-app). Odczyt jest sekwencyjny: wołający pobiera wiadomości po kolei.
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

    /// <param name="handshakeTimeout">Przy pierwszym parowaniu daj czas na kliknięcie „Zezwól” na TV (np. 60 s).</param>
    public static async Task<TvChannel> ConnectAsync(string ip, string channel, string? token, TlsPolicy tls,
        bool waitReady, TimeSpan handshakeTimeout, CancellationToken ct)
    {
        Lan.RequirePrivateIPv4(ip);
        var ws = new ClientWebSocket();
        ws.Options.RemoteCertificateValidationCallback = tls.Validate;
        var name = Uri.EscapeDataString(Convert.ToBase64String(Encoding.UTF8.GetBytes(AppName)));
        var url = $"wss://{ip}:8002/api/v2/channels/{channel}?name={name}"
                  + (string.IsNullOrEmpty(token) ? "" : $"&token={Uri.EscapeDataString(token)}");
        TvLog.Write($"[WS] łączę {channel} z {ip} (token: {(string.IsNullOrEmpty(token) ? "brak" : "jest")})");

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
            throw new TvException("TV nie odpowiedziało na czas. Jeśli to pierwsze połączenie, zatwierdź je na TV pilotem.");
        }
        catch (WebSocketException e)
        {
            await ch.DisposeAsync();
            throw new TvException($"Nie udało się połączyć z TV: {e.Message}. Jeśli certyfikat TV się zmienił, usuń TV z listy i dodaj ponownie.");
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
                    TvLog.Write($"[WS] connect (token {(Token is null ? "nie nadesłany" : "otrzymany")})");
                    if (!waitReady) return;
                    break;
                case "ms.channel.ready":
                    TvLog.Write("[WS] ready");
                    if (connected || waitReady) return;
                    break;
                case "ms.channel.unauthorized":
                    throw new TvException("TV odrzuciło połączenie (brak zgody). Zezwól aplikacji na TV albo usuń TV z listy i spróbuj ponownie.");
                case "ms.channel.timeOut":
                    throw new TvException("TV zgłosiło przekroczenie czasu na zatwierdzenie połączenia.");
            }
        }
    }

    public static string? Event(JsonElement m) =>
        m.ValueKind == JsonValueKind.Object && m.TryGetProperty("event", out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() : null;

    /// <summary>Następna wiadomość tekstowa JSON (binarne i niepoprawne są pomijane).</summary>
    public async Task<JsonElement> ReceiveAsync(CancellationToken ct)
    {
        while (true)
        {
            var buf = new ArrayBufferWriter<byte>();
            ValueWebSocketReceiveResult r;
            do
            {
                try { r = await _ws.ReceiveAsync(buf.GetMemory(8192), ct); }
                catch (WebSocketException e) { throw new TvConnectionLostException($"Połączenie z TV zostało zerwane ({e.Message})."); }
                if (r.MessageType == WebSocketMessageType.Close) throw new TvConnectionLostException("TV zamknęło połączenie.");
                buf.Advance(r.Count);
                if (buf.WrittenCount > MaxMessageBytes) throw new TvException("Wiadomość od TV jest zbyt duża.");
            } while (!r.EndOfMessage);

            if (r.MessageType != WebSocketMessageType.Text) { TvLog.Trace($"WS << (binarna, {buf.WrittenCount} B, pominięta)"); continue; }
            TvLog.Trace($"WS << {Encoding.UTF8.GetString(buf.WrittenSpan)}");
            try { using var doc = JsonDocument.Parse(buf.WrittenMemory); return doc.RootElement.Clone(); }
            catch (JsonException) { TvLog.Write("[WS] pominięto niepoprawny JSON"); }
        }
    }

    public async Task SendJsonAsync(object message, CancellationToken ct)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(message);
        TvLog.Trace($"WS >> {Encoding.UTF8.GetString(bytes)}");
        try { await _ws.SendAsync(bytes, WebSocketMessageType.Text, true, ct); }
        catch (Exception e) when (e is WebSocketException or InvalidOperationException or ObjectDisposedException)
        {
            throw new TvConnectionLostException($"Połączenie z TV zostało zerwane ({e.Message}).");
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
        catch { /* zamykamy best-effort */ }
        _ws.Dispose();
    }
}

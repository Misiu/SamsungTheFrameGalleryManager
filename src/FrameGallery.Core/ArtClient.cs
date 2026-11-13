using System.Buffers.Binary;
using System.Net.Security;
using System.Net.Sockets;
using System.Text.Json;

namespace FrameGallery.Core;

public sealed record ArtItem(string ContentId, string? CategoryId, string? MatteId, int Width, int Height, string? ImageDate);

/// <summary>Dane gniazda TCP, przez które TV przyjmuje/wydaje pliki (odpowiedź <c>conn_info</c>).</summary>
public sealed record ConnInfo(int Port, string Key, bool Secured);

/// <summary>
/// Art Mode: żądania <c>art_app_request</c> na kanale com.samsung.art-app, odpowiedzi w
/// <c>d2d_service_message</c>. Pliki (upload, miniatury) idą osobnym gniazdem TCP wskazanym przez TV.
/// Operacje są serializowane, bo kanał czyta się sekwencyjnie.
/// </summary>
public sealed class ArtClient : IAsyncDisposable
{
    /// <summary>Kategoria „Moje zdjęcia” (własne uploady użytkownika).</summary>
    public const string MyPhotosCategory = "MY-C0002";

    static readonly TimeSpan Short = TimeSpan.FromSeconds(10), Long = TimeSpan.FromSeconds(30);

    TvChannel _ch;
    readonly string _ip;
    readonly string? _token;
    readonly TlsPolicy _tls;
    readonly SemaphoreSlim _gate = new(1, 1);

    ArtClient(TvChannel ch, string ip, string? token, TlsPolicy tls) { _ch = ch; _ip = ip; _token = token; _tls = tls; }

    public static async Task<ArtClient> ConnectAsync(string ip, string? token, TlsPolicy tls, CancellationToken ct) =>
        new(await OpenChannelAsync(ip, token, tls, ct), ip, token, tls);

    static Task<TvChannel> OpenChannelAsync(string ip, string? token, TlsPolicy tls, CancellationToken ct) =>
        TvChannel.ConnectAsync(ip, TvChannel.ArtChannel, token, tls, waitReady: true, TimeSpan.FromSeconds(15), ct);

    // ---- operacje publiczne ----

    public Task<IReadOnlyList<ArtItem>> ListAsync(CancellationToken ct) => Locked(async () =>
    {
        var id = NewId();
        var r = await RequestAsync(id, "get_content_list", new() { ["category"] = null }, IsContentListResponse, Short, ct);
        return ParseContentList(r);
    }, ct);

    /// <returns>Miniatury (JPEG) wg content_id; brakujące pomijane.</returns>
    public Task<IReadOnlyDictionary<string, byte[]>> GetThumbnailsAsync(IReadOnlyList<string> contentIds, CancellationToken ct) => Locked<IReadOnlyDictionary<string, byte[]>>(async () =>
    {
        var result = new Dictionary<string, byte[]>();
        if (contentIds.Count == 0) return result;

        var id = NewId();
        var args = new Dictionary<string, object?>
        {
            ["content_id_list"] = contentIds.Select(c => new { content_id = c }).ToArray(),
            ["conn_info"] = NewConnRequest(id),
        };
        var r = await RequestAsync(id, "get_thumbnail_list", args, i => i.TryGetProperty("conn_info", out _), Long, ct);

        await using var s = await OpenDataStreamAsync(ParseConnInfo(r), ct);
        for (var index = 0; ; index++)
        {
            var (header, data) = await ReadFrameAsync(s, ct);
            var fileId = header.TryGetProperty("fileID", out var f) && f.ValueKind == JsonValueKind.String ? f.GetString()! : "";
            // Jeśli TV poda inny identyfikator niż content_id, przypisz wg kolejności zapytania.
            var key = contentIds.Contains(fileId) ? fileId : index < contentIds.Count ? contentIds[index] : fileId;
            result[key] = data;

            var total = header.TryGetProperty("total", out var t) ? ReadInt(t) : contentIds.Count;
            if (index + 1 >= total || result.Count >= contentIds.Count) break;
        }
        return result;
    }, ct);

    /// <summary>Wyświetla obraz na TV (<paramref name="show"/> = true przełącza też TV na Art Mode).</summary>
    public Task SelectImageAsync(string contentId, bool show, CancellationToken ct) => Locked(async () =>
    {
        var id = NewId();
        var args = new Dictionary<string, object?> { ["content_id"] = contentId, ["show"] = show };
        // TV odpowiada zdarzeniem select_image z naszym request_id; art_mode_changed, image_selected i recently_set_updated
        // to niezależne powiadomienia (bez id), więc ich nie akceptujemy. Przełączenie TV w Art Mode potrafi trwać kilka sekund.
        await RequestAsync(id, "select_image", args, i => TvChannel.Event(i) == "select_image", Long, ct);
        return 0;
    }, ct);

    /// <returns>content_id obrazu, który TV wyświetla teraz (null, gdy TV go nie poda).</returns>
    public Task<string?> GetCurrentArtworkAsync(CancellationToken ct) => Locked<string?>(async () =>
    {
        var id = NewId();
        var r = await RequestAsync(id, "get_current_artwork", new(), i => TvChannel.Event(i) == "get_current_artwork", Short, ct);
        return r.TryGetProperty("content_id", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() : null;
    }, ct);

    public Task DeleteAsync(IReadOnlyList<string> contentIds, CancellationToken ct) => Locked(async () =>
    {
        var id = NewId();
        var args = new Dictionary<string, object?> { ["content_id_list"] = contentIds.Select(c => new { content_id = c }).ToArray() };
        await RequestAsync(id, "delete_image_list", args, i => TvChannel.Event(i) == "image_deleted" || HasId(i, id), Short, ct);
        return 0;
    }, ct);

    /// <returns>content_id nowego obrazu.</returns>
    public Task<string> UploadAsync(byte[] jpeg, CancellationToken ct) => Locked(async () =>
    {
        var id = NewId();
        var args = new Dictionary<string, object?>
        {
            ["file_type"] = "jpg",
            ["conn_info"] = NewConnRequest(id),
            ["image_date"] = DateTime.Now.ToString("yyyy:MM:dd HH:mm:ss"),
            ["matte_id"] = "none",           // kadr 16:9 wypełnia ekran, bez passe-partout
            ["portrait_matte_id"] = "none",
            ["file_size"] = jpeg.Length,
        };
        var ready = await RequestAsync(id, "send_image", args, i => TvChannel.Event(i) == "ready_to_use", Long, ct);
        var info = ParseConnInfo(ready);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        await using (var s = await OpenDataStreamAsync(info, ct))
        {
            var frame = BuildUploadFrame(jpeg, info.Key, "jpg");
            TvLog.Trace($"DATA >> ramka uploadu: nagłówek {System.Text.Encoding.UTF8.GetString(frame, 4, frame.Length - jpeg.Length - 4)}, dane {jpeg.Length} B");
            await s.WriteAsync(frame, ct);
            await s.FlushAsync(ct);
        }
        TvLog.Write($"[ART] wysłano {jpeg.Length} B w {sw.ElapsedMilliseconds} ms, czekam na image_added");

        var added = await WaitAsync(id, i => TvChannel.Event(i) == "image_added", Long, ct);
        return added.TryGetProperty("content_id", out var c) && c.ValueKind == JsonValueKind.String
            ? c.GetString()! : throw new TvException("TV potwierdziło upload, ale nie podało identyfikatora obrazu.");
    }, ct);

    public ValueTask DisposeAsync() => _ch.DisposeAsync();

    // ---- protokół (internal = testowalne) ----

    /// <summary>Ramka uploadu: 4 bajty (big-endian) długości nagłówka JSON, nagłówek, dane pliku.</summary>
    internal static byte[] BuildUploadFrame(byte[] data, string secKey, string fileType)
    {
        var header = JsonSerializer.SerializeToUtf8Bytes(new
        {
            num = 0, total = 1, fileLength = data.Length,
            fileName = $"{Guid.NewGuid():N}.{fileType}", fileType, secKey, version = "4.3.4.0",
        });
        var frame = new byte[4 + header.Length + data.Length];
        BinaryPrimitives.WriteInt32BigEndian(frame, header.Length);
        header.CopyTo(frame, 4);
        data.CopyTo(frame, 4 + header.Length);
        return frame;
    }

    /// <summary><c>conn_info</c> bywa obiektem albo stringiem z JSON-em; port liczbą albo tekstem.</summary>
    internal static ConnInfo ParseConnInfo(JsonElement response)
    {
        if (!response.TryGetProperty("conn_info", out var ci)) throw new TvException("TV nie podało danych połączenia (conn_info).");
        using var parsed = ci.ValueKind == JsonValueKind.String ? JsonDocument.Parse(ci.GetString()!) : null;
        var o = parsed?.RootElement ?? ci;
        if (o.ValueKind != JsonValueKind.Object || !o.TryGetProperty("port", out var p) || !o.TryGetProperty("key", out var k))
            throw new TvException("Nieprawidłowe dane połączenia od TV (conn_info).");

        var port = ReadInt(p);
        if (port is < 1 or > 65535) throw new TvException("TV podało nieprawidłowy port.");
        var secured = o.TryGetProperty("secured", out var s)
            && (s.ValueKind == JsonValueKind.True || (s.ValueKind == JsonValueKind.String && s.GetString() == "true"));
        return new ConnInfo(port, k.ValueKind == JsonValueKind.String ? k.GetString()! : k.ToString(), secured);
    }

    /// <summary>TV odpowiada zdarzeniem o nazwie żądania (get_content_list), a dane niesie pole content_list; rozpoznajemy po polu.</summary>
    internal static bool IsContentListResponse(JsonElement i) => i.TryGetProperty("content_list", out _);

    internal static IReadOnlyList<ArtItem> ParseContentList(JsonElement response)
    {
        if (!response.TryGetProperty("content_list", out var cl)) throw new TvException("TV nie zwróciło listy obrazów.");
        using var parsed = cl.ValueKind == JsonValueKind.String ? JsonDocument.Parse(cl.GetString()!) : null;
        var arr = parsed?.RootElement ?? cl;
        var items = new List<ArtItem>();
        if (arr.ValueKind != JsonValueKind.Array) return items;

        foreach (var e in arr.EnumerateArray())
        {
            string? S(string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
            int N(string n) => e.TryGetProperty(n, out var v) ? ReadInt(v) : 0;
            if (S("content_id") is { } id) items.Add(new ArtItem(id, S("category_id"), S("matte_id"), N("width"), N("height"), S("image_date")));
        }
        return items;
    }

    // ---- wnętrze ----

    static string NewId() => Guid.NewGuid().ToString();

    static object NewConnRequest(string id) =>
        new { d2d_mode = "socket", connection_id = Random.Shared.NextInt64(0, 4_294_967_296), id };

    static int ReadInt(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.Number when e.TryGetInt32(out var n) => n,
        JsonValueKind.String when int.TryParse(e.GetString(), out var n) => n,
        _ => 0,
    };

    static bool HasId(JsonElement i, string id) =>
        (i.TryGetProperty("request_id", out var a) && a.ValueKind == JsonValueKind.String && a.GetString() == id)
        || (i.TryGetProperty("id", out var b) && b.ValueKind == JsonValueKind.String && b.GetString() == id);

    /// <summary>Odpowiedź należy do innego żądania tylko wtedy, gdy niesie inny id; brak id nie dyskwalifikuje.</summary>
    static bool IsForOtherRequest(JsonElement i, string id) =>
        (i.TryGetProperty("request_id", out var a) && a.ValueKind == JsonValueKind.String && a.GetString() != id)
        || (i.TryGetProperty("id", out var b) && b.ValueKind == JsonValueKind.String && b.GetString() != id);

    /// <summary>
    /// Serializuje operacje. TV zrywa kanał po ok. 100 s bezczynności, więc zerwane połączenie to normalna sytuacja:
    /// łączymy się ponownie i ponawiamy operację raz. (Upload ponowiony po zerwaniu tuż po wysłaniu pliku
    /// mógłby dodać duplikat; to rzadki przypadek, a skutek niegroźny.)
    /// </summary>
    async Task<T> Locked<T>(Func<Task<T>> action, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            try { return await action(); }
            catch (TvConnectionLostException e)
            {
                TvLog.Write($"[ART] połączenie zerwane ({e.Message}); łączę ponownie i ponawiam operację");
                await _ch.DisposeAsync();
                _ch = await OpenChannelAsync(_ip, _token, _tls, ct);
                return await action();
            }
        }
        finally { _gate.Release(); }
    }

    async Task<JsonElement> RequestAsync(string id, string request, Dictionary<string, object?> args,
        Func<JsonElement, bool> accept, TimeSpan timeout, CancellationToken ct)
    {
        var data = new Dictionary<string, object?>(args) { ["request"] = request, ["id"] = id, ["request_id"] = id };
        TvLog.Write($"[ART] -> {request}");
        await _ch.SendJsonAsync(new
        {
            method = "ms.channel.emit",
            @params = new { @event = "art_app_request", to = "host", data = JsonSerializer.Serialize(data) },
        }, ct);
        return await WaitAsync(id, accept, timeout, ct);
    }

    async Task<JsonElement> WaitAsync(string id, Func<JsonElement, bool> accept, TimeSpan timeout, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            while (true)
            {
                var m = await _ch.ReceiveAsync(cts.Token);
                if (TvChannel.Event(m) != "d2d_service_message" || !m.TryGetProperty("data", out var d)) continue;

                using var parsed = d.ValueKind == JsonValueKind.String ? JsonDocument.Parse(d.GetString()!) : null;
                var inner = (parsed?.RootElement ?? d).Clone();
                if (inner.ValueKind != JsonValueKind.Object || IsForOtherRequest(inner, id)) continue;

                TvLog.Write($"[ART] <- {TvChannel.Event(inner) ?? "(bez event)"} po {sw.ElapsedMilliseconds} ms");
                if (TvChannel.Event(inner) == "error" || inner.TryGetProperty("error_code", out _))
                {
                    var code = inner.TryGetProperty("error_code", out var c) ? c.ToString() : "?";
                    throw new TvException($"TV zwróciło błąd (kod {code}).");
                }
                if (accept(inner)) return inner;
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            TvLog.Write($"[ART] timeout po {sw.ElapsedMilliseconds} ms (żądanie {id})");
            throw new TvException("TV nie odpowiedziało na czas.");
        }
    }

    /// <summary>Gniazdo danych zawsze łączy się z IP TV z listy (nie z adresem z odpowiedzi), TLS gdy TV tak każe.</summary>
    async Task<Stream> OpenDataStreamAsync(ConnInfo info, CancellationToken ct)
    {
        var tcp = new TcpClient();
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(Short);
            TvLog.Trace($"DATA: łączę {_ip}:{info.Port} (TLS={info.Secured})");
            await tcp.ConnectAsync(_ip, info.Port, cts.Token);
            Stream s = tcp.GetStream();
            if (info.Secured)
            {
                // ponytail: gniazdo danych TV ma własny certyfikat, którego nie przypinamy; autoryzuje je klucz secKey
                // i fakt, że łączymy się wyłącznie z IP sparowanego TV. Upgrade: przypiąć drugi odcisk.
                var ssl = new SslStream(s, false, (_, _, _, _) => true);
                await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost = _ip }, cts.Token);
                s = ssl;
            }
            return new OwnedStream(s, tcp);
        }
        catch { tcp.Dispose(); throw; }
    }

    /// <summary>Jedna ramka miniatury: 4 bajty długości nagłówka, nagłówek JSON (fileLength, fileID…), dane.</summary>
    static async Task<(JsonElement Header, byte[] Data)> ReadFrameAsync(Stream s, CancellationToken ct)
    {
        var len = new byte[4];
        await s.ReadExactlyAsync(len, ct);
        var hl = BinaryPrimitives.ReadInt32BigEndian(len);
        if (hl is < 2 or > 64 * 1024) throw new TvException("Nieprawidłowy nagłówek danych od TV.");

        var hb = new byte[hl];
        await s.ReadExactlyAsync(hb, ct);
        using var headerDoc = JsonDocument.Parse(hb);
        var header = headerDoc.RootElement.Clone();
        TvLog.Trace($"DATA << nagłówek ramki ({hl} B): {System.Text.Encoding.UTF8.GetString(hb)}");
        var n = header.TryGetProperty("fileLength", out var f) ? ReadInt(f) : throw new TvException("Brak fileLength w nagłówku od TV.");
        if (n is < 0 or > 16 * 1024 * 1024) throw new TvException("Nieprawidłowy rozmiar pliku od TV.");

        var data = new byte[n];
        await s.ReadExactlyAsync(data, ct);
        return (header, data);
    }

    /// <summary>Strumień, który przy zamknięciu zwalnia też <see cref="TcpClient"/>.</summary>
    sealed class OwnedStream(Stream inner, TcpClient owner) : Stream
    {
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => inner.CanWrite;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => inner.Flush();
        public override Task FlushAsync(CancellationToken ct) => inner.FlushAsync(ct);
        public override int Read(byte[] b, int o, int c) => inner.Read(b, o, c);
        public override ValueTask<int> ReadAsync(Memory<byte> b, CancellationToken ct = default) => inner.ReadAsync(b, ct);
        public override void Write(byte[] b, int o, int c) => inner.Write(b, o, c);
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> b, CancellationToken ct = default) => inner.WriteAsync(b, ct);
        public override long Seek(long o, SeekOrigin so) => throw new NotSupportedException();
        public override void SetLength(long v) => throw new NotSupportedException();
        protected override void Dispose(bool disposing)
        {
            if (disposing) { inner.Dispose(); owner.Dispose(); }
            base.Dispose(disposing);
        }
    }
}

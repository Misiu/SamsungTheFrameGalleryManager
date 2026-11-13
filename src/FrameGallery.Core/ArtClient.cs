using System.Buffers.Binary;
using System.Net.Security;
using System.Net.Sockets;
using System.Text.Json;

namespace FrameGallery.Core;

public sealed record ArtItem(string ContentId, string? CategoryId, string? MatteId, int Width, int Height, string? ImageDate);

/// <summary>The TCP socket through which the TV accepts or returns files (the <c>conn_info</c> response).</summary>
public sealed record ConnInfo(int Port, string Key, bool Secured);

/// <summary>
/// Art Mode: <c>art_app_request</c> requests on the com.samsung.art-app channel, answered in
/// <c>d2d_service_message</c>. Files (uploads, thumbnails) travel over a separate TCP socket chosen by the TV.
/// Operations are serialized because the channel is read sequentially.
/// </summary>
public sealed class ArtClient : IArtClient
{
    /// <summary>The "My photos" category (the user's own uploads).</summary>
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

    // ---- public operations ----

    public Task<IReadOnlyList<ArtItem>> ListAsync(CancellationToken ct) => Locked(async () =>
    {
        var id = NewId();
        var r = await RequestAsync(id, "get_content_list", new() { ["category"] = null }, IsContentListResponse, Short, ct);
        return ParseContentList(r);
    }, ct);

    /// <returns>Thumbnails (JPEG) by content_id; missing ones are skipped.</returns>
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
            // If the TV reports an identifier other than the content_id, assign by the order of the request.
            var key = contentIds.Contains(fileId) ? fileId : index < contentIds.Count ? contentIds[index] : fileId;
            result[key] = data;

            var total = header.TryGetProperty("total", out var t) ? ReadInt(t) : contentIds.Count;
            if (index + 1 >= total || result.Count >= contentIds.Count) break;
        }
        return result;
    }, ct);

    /// <summary>Shows an image on the TV (<paramref name="show"/> = true also switches the TV to Art Mode).</summary>
    public Task SelectImageAsync(string contentId, bool show, CancellationToken ct) => Locked(async () =>
    {
        var id = NewId();
        var args = new Dictionary<string, object?> { ["content_id"] = contentId, ["show"] = show };
        // The TV answers with a select_image event carrying our request_id; art_mode_changed, image_selected and
        // recently_set_updated are independent notifications (no id), so they are not accepted. Switching the TV
        // into Art Mode can take a few seconds.
        await RequestAsync(id, "select_image", args, i => TvChannel.Event(i) == "select_image", Long, ct);
        return 0;
    }, ct);

    /// <returns>The content_id of the image the TV is showing now (null if the TV does not say).</returns>
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

    /// <returns>The content_id of the new image.</returns>
    /// <param name="progress">Receives the fraction (0..1) of the file already sent over the data socket.</param>
    public Task<string> UploadAsync(byte[] jpeg, CancellationToken ct, IProgress<double>? progress = null) => Locked(async () =>
    {
        var id = NewId();
        var args = new Dictionary<string, object?>
        {
            ["file_type"] = "jpg",
            ["conn_info"] = NewConnRequest(id),
            ["image_date"] = DateTime.Now.ToString("yyyy:MM:dd HH:mm:ss"),
            ["matte_id"] = "none",           // a 16:9 crop fills the screen, so no matte
            ["portrait_matte_id"] = "none",
            ["file_size"] = jpeg.Length,
        };
        var ready = await RequestAsync(id, "send_image", args, i => TvChannel.Event(i) == "ready_to_use", Long, ct);
        var info = ParseConnInfo(ready);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        await using (var s = await OpenDataStreamAsync(info, ct))
        {
            var frame = BuildUploadFrame(jpeg, info.Key, "jpg");
            TvLog.Trace($"DATA >> upload frame: header {System.Text.Encoding.UTF8.GetString(frame, 4, frame.Length - jpeg.Length - 4)}, data {jpeg.Length} B");
            await WriteWithProgressAsync(s, frame, progress, ct);
            await s.FlushAsync(ct);
        }
        progress?.Report(1);
        TvLog.Write($"[ART] sent {jpeg.Length} B in {sw.ElapsedMilliseconds} ms, waiting for image_added");

        var added = await WaitAsync(id, i => TvChannel.Event(i) == "image_added", Long, ct);
        return added.TryGetProperty("content_id", out var c) && c.ValueKind == JsonValueKind.String
            ? c.GetString()! : throw new TvException("Err_UploadNoId");
    }, ct);

    public ValueTask DisposeAsync() => _ch.DisposeAsync();

    // ---- protocol (internal = testable) ----

    /// <summary>Writes in chunks so the caller can show a progress bar.</summary>
    static async Task WriteWithProgressAsync(Stream s, byte[] data, IProgress<double>? progress, CancellationToken ct)
    {
        const int Chunk = 64 * 1024;
        for (var offset = 0; offset < data.Length; offset += Chunk)
        {
            var n = Math.Min(Chunk, data.Length - offset);
            await s.WriteAsync(data.AsMemory(offset, n), ct);
            progress?.Report((double)(offset + n) / data.Length);
        }
    }

    /// <summary>Upload frame: 4 bytes (big-endian) with the JSON header length, the header, then the file data.</summary>
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

    /// <summary><c>conn_info</c> may be an object or a string holding JSON; the port may be a number or text.</summary>
    internal static ConnInfo ParseConnInfo(JsonElement response)
    {
        if (!response.TryGetProperty("conn_info", out var ci)) throw new TvException("Err_NoConnInfo");
        using var parsed = ci.ValueKind == JsonValueKind.String ? JsonDocument.Parse(ci.GetString()!) : null;
        var o = parsed?.RootElement ?? ci;
        if (o.ValueKind != JsonValueKind.Object || !o.TryGetProperty("port", out var p) || !o.TryGetProperty("key", out var k))
            throw new TvException("Err_BadConnInfo");

        var port = ReadInt(p);
        if (port is < 1 or > 65535) throw new TvException("Err_BadPort");
        var secured = o.TryGetProperty("secured", out var s)
            && (s.ValueKind == JsonValueKind.True || (s.ValueKind == JsonValueKind.String && s.GetString() == "true"));
        return new ConnInfo(port, k.ValueKind == JsonValueKind.String ? k.GetString()! : k.ToString(), secured);
    }

    /// <summary>The TV answers with an event named after the request (get_content_list) and carries the data in the content_list field; we recognise the response by that field.</summary>
    internal static bool IsContentListResponse(JsonElement i) => i.TryGetProperty("content_list", out _);

    internal static IReadOnlyList<ArtItem> ParseContentList(JsonElement response)
    {
        if (!response.TryGetProperty("content_list", out var cl)) throw new TvException("Err_NoContentList");
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

    // ---- internals ----

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

    /// <summary>A response belongs to another request only if it carries a different id; having no id does not disqualify it.</summary>
    static bool IsForOtherRequest(JsonElement i, string id) =>
        (i.TryGetProperty("request_id", out var a) && a.ValueKind == JsonValueKind.String && a.GetString() != id)
        || (i.TryGetProperty("id", out var b) && b.ValueKind == JsonValueKind.String && b.GetString() != id);

    /// <summary>
    /// Serializes operations. The TV drops the channel after about 100 s of inactivity, so a dropped connection is normal:
    /// we reconnect and retry the operation once. (An upload retried after a drop right after the file was sent
    /// could add a duplicate; that is rare and harmless.)
    /// </summary>
    async Task<T> Locked<T>(Func<Task<T>> action, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            try { return await action(); }
            catch (TvConnectionLostException e)
            {
                TvLog.Write($"[ART] connection lost ({TvLog.Describe(e)}); reconnecting and retrying the operation");
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

                TvLog.Write($"[ART] <- {TvChannel.Event(inner) ?? "(no event)"} after {sw.ElapsedMilliseconds} ms");
                if (TvChannel.Event(inner) == "error" || inner.TryGetProperty("error_code", out _))
                {
                    var code = inner.TryGetProperty("error_code", out var c) ? c.ToString() : "?";
                    throw new TvException("Err_TvError", code);
                }
                if (accept(inner)) return inner;
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            TvLog.Write($"[ART] timeout after {sw.ElapsedMilliseconds} ms (request {id})");
            throw new TvException("Err_Timeout");
        }
    }

    /// <summary>The data socket always connects to the TV's known IP (not to an address from the response), with TLS when the TV asks for it.</summary>
    async Task<Stream> OpenDataStreamAsync(ConnInfo info, CancellationToken ct)
    {
        var tcp = new TcpClient();
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(Short);
            TvLog.Trace($"DATA: connecting to {_ip}:{info.Port} (TLS={info.Secured})");
            await tcp.ConnectAsync(_ip, info.Port, cts.Token);
            Stream s = tcp.GetStream();
            if (info.Secured)
            {
                // ponytail: the TV's data socket has its own certificate, which we do not pin; the connection is authorised
                // by the secKey and by connecting only to the paired TV's IP. Upgrade: pin a second thumbprint.
                var ssl = new SslStream(s, false, (_, _, _, _) => true);
                await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost = _ip }, cts.Token);
                s = ssl;
            }
            return new OwnedStream(s, tcp);
        }
        catch { tcp.Dispose(); throw; }
    }

    /// <summary>One thumbnail frame: 4 bytes with the header length, a JSON header (fileLength, fileID…), then the data.</summary>
    static async Task<(JsonElement Header, byte[] Data)> ReadFrameAsync(Stream s, CancellationToken ct)
    {
        var len = new byte[4];
        await s.ReadExactlyAsync(len, ct);
        var hl = BinaryPrimitives.ReadInt32BigEndian(len);
        if (hl is < 2 or > 64 * 1024) throw new TvException("Err_BadDataHeader");

        var hb = new byte[hl];
        await s.ReadExactlyAsync(hb, ct);
        using var headerDoc = JsonDocument.Parse(hb);
        var header = headerDoc.RootElement.Clone();
        TvLog.Trace($"DATA << frame header ({hl} B): {System.Text.Encoding.UTF8.GetString(hb)}");
        var n = header.TryGetProperty("fileLength", out var f) ? ReadInt(f) : throw new TvException("Err_NoFileLength");
        if (n is < 0 or > 16 * 1024 * 1024) throw new TvException("Err_BadFileSize");

        var data = new byte[n];
        await s.ReadExactlyAsync(data, ct);
        return (header, data);
    }

    /// <summary>A stream that also disposes the <see cref="TcpClient"/> when it is closed.</summary>
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

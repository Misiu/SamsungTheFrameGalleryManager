using System.Net;
using System.Text.Json;

namespace FrameGallery.Core;

/// <summary>Dane z nieautoryzowanego endpointu REST TV (https://ip:8002/api/v2/).</summary>
public sealed record TvInfo(string Id, string Name, string ModelName, string Ip, bool IsFrame, int Width, int Height);

public static class TvInfoClient
{
    /// <summary>
    /// Pobiera opis TV. Najpierw HTTPS:8002, awaryjnie HTTP:8001. Odpowiedź służy tylko do wyświetlenia
    /// i do decyzji „czy to Frame”, więc przy 8002 certyfikat jest tylko rejestrowany w <paramref name="tls"/>.
    /// </summary>
    public static async Task<TvInfo> GetAsync(string ip, TlsPolicy tls, CancellationToken ct = default)
    {
        Lan.RequirePrivateIPv4(ip);
        using var http = new HttpClient(new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            ConnectTimeout = TimeSpan.FromSeconds(3),
            SslOptions = { RemoteCertificateValidationCallback = tls.Validate },
        })
        { Timeout = TimeSpan.FromSeconds(6), MaxResponseContentBufferSize = 256 * 1024 };

        string json;
        try { json = await http.GetStringAsync($"https://{ip}:8002/api/v2/", ct); }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            TvLog.Write($"[INFO] {ip}:8002 niedostępny ({e.Message}), próbuję 8001");
            try { json = await http.GetStringAsync($"http://{ip}:8001/api/v2/", ct); }
            catch (Exception e2) when (e2 is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
            {
                throw new TvException($"Nie można połączyć się z {ip} na portach 8001/8002. Sprawdź adres i czy TV jest włączony.");
            }
        }
        TvLog.Trace($"REST << /api/v2/ {ip}: {json}");
        return Parse(json, ip);
    }

    public static TvInfo Parse(string json, string ip)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("device", out var d)) throw new TvException("Urządzenie nie odpowiada jak TV Samsung.");

            // TV zwraca teksty zakodowane jak HTML (np. 65&quot; The Frame), więc je dekodujemy.
            string S(JsonElement e, string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? WebUtility.HtmlDecode(v.GetString()!) : "";
            var id = S(d, "id") is { Length: > 0 } i ? i : S(root, "id");
            var name = S(d, "name") is { Length: > 0 } n ? n : S(root, "name");
            var (w, h) = ParseResolution(S(d, "resolution"));
            return new TvInfo(
                Id: id.Length > 0 ? id : ip,
                Name: name.Length > 0 ? name : $"Samsung TV ({ip})",
                ModelName: S(d, "modelName"),
                Ip: ip,
                IsFrame: string.Equals(S(d, "FrameTVSupport"), "true", StringComparison.OrdinalIgnoreCase),
                Width: w, Height: h);
        }
        catch (JsonException) { throw new TvException("Nieprawidłowa odpowiedź urządzenia (to nie jest TV Samsung?)."); }
    }

    /// <summary>„3840x2160” → (3840, 2160); przy braku/śmieciach 4K.</summary>
    static (int, int) ParseResolution(string s)
    {
        var p = s.Split('x');
        return p.Length == 2 && int.TryParse(p[0], out var w) && int.TryParse(p[1], out var h) && w >= 640 && h >= 360 && w <= 8192 && h <= 8192
            ? (w, h) : (3840, 2160);
    }
}

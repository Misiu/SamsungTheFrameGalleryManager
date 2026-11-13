using System.Net;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace FrameGallery.Core;

/// <summary>Błąd rozmowy z TV, z komunikatem nadającym się do pokazania użytkownikowi.</summary>
public class TvException(string message) : Exception(message);

/// <summary>TV zerwało kanał (np. po bezczynności). <see cref="ArtClient"/> łączy się wtedy ponownie i ponawia operację.</summary>
public sealed class TvConnectionLostException(string message) : TvException(message);

/// <summary>
/// Log diagnostyczny do pliku i zdarzenia <see cref="Line"/>. <see cref="Write"/> to zdarzenia aplikacji,
/// <see cref="Trace"/> to surowy ruch protokołu (do porównywania z innymi implementacjami i szukania różnic).
/// Każda linia przechodzi przez <see cref="Redact"/> i jest przycinana, więc log można bezpiecznie wkleić do zgłoszenia.
/// </summary>
public static class TvLog
{
    const int MaxLineChars = 4000;
    static readonly object Gate = new();
    static string? _path;

    public static event Action<string>? Line;

    public static void Write(string message) => Emit("INFO ", message);
    public static void Trace(string message) => Emit("TRACE", message);

    /// <summary>Zapisuje log w pliku; przy przekroczeniu <paramref name="maxBytes"/> stary plik trafia do *.old.</summary>
    public static void AttachFile(string path, string sessionHeader, long maxBytes = 5_000_000)
    {
        lock (Gate)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                if (File.Exists(path) && new FileInfo(path).Length > maxBytes) File.Move(path, path + ".old", overwrite: true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* log nie może wywalić aplikacji */ }
            _path = path;
        }
        Emit("INFO ", $"=== sesja: {sessionHeader} ===");
    }

    static void Emit(string level, string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {Truncate(Redact(message))}";
        Line?.Invoke(line);
        lock (Gate)
        {
            if (_path is null) return;
            try { File.AppendAllText(_path, line + Environment.NewLine); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
    }

    static string Truncate(string s) => s.Length <= MaxLineChars ? s : s[..MaxLineChars] + $"…(+{s.Length - MaxLineChars} znaków)";

    static readonly System.Text.RegularExpressions.Regex Secrets = new(
        @"(\\?""(?:token|key|secKey|ssid|wifiMac|duid)\\?""\s*:\s*\\?"")[^""\\]*",
        System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>Maskuje wartości sekretów i danych identyfikujących sieć, także w JSON-ie zagnieżdżonym w stringu.</summary>
    internal static string Redact(string s) => Secrets.Replace(s, "$1***");
}

/// <summary>
/// Weryfikacja certyfikatu TV. TV ma certyfikat samopodpisany, więc zamiast wyłączać walidację
/// przypinamy odcisk SHA-256 (TOFU): bez pinu akceptujemy i zapamiętujemy <see cref="Observed"/>,
/// z pinem akceptujemy tylko ten sam certyfikat.
/// </summary>
public sealed class TlsPolicy(string? pinnedSha256)
{
    public string? Pinned { get; } = pinnedSha256;
    public string? Observed { get; private set; }

    public bool Validate(object? sender, X509Certificate? cert, X509Chain? chain, SslPolicyErrors errors)
    {
        if (cert is null) return false;
        Observed = Convert.ToHexString(SHA256.HashData(cert.GetRawCertData()));
        return Pinned is null || string.Equals(Pinned, Observed, StringComparison.OrdinalIgnoreCase);
    }
}

public static class Lan
{
    /// <summary>Tylko adresy z sieci lokalnej: 10/8, 172.16/12, 192.168/16 i link-local 169.254/16.</summary>
    public static bool IsPrivate(IPAddress ip)
    {
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        if (ip.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork) return false;
        var b = ip.GetAddressBytes();
        return b[0] == 10
            || (b[0] == 172 && b[1] is >= 16 and <= 31)
            || (b[0] == 192 && b[1] == 168)
            || (b[0] == 169 && b[1] == 254);
    }

    /// <summary>Waliduje adres wpisany przez użytkownika; rzuca <see cref="TvException"/> dla nie-LAN.</summary>
    public static IPAddress RequirePrivateIPv4(string text)
    {
        if (!IPAddress.TryParse(text.Trim(), out var ip) || ip.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
            throw new TvException("Podaj poprawny adres IPv4, np. 192.168.1.50.");
        if (!IsPrivate(ip))
            throw new TvException("Adres musi należeć do sieci lokalnej (10.x, 172.16-31.x, 192.168.x).");
        return ip;
    }
}

/// <summary>Kadr o stałych proporcjach w pikselach obrazu źródłowego.</summary>
public readonly record struct CropRect(double X, double Y, double W, double H);

public static class CropMath
{
    /// <summary>Największy kadr o zadanych proporcjach (W/H), wycentrowany.</summary>
    public static CropRect Initial(double imgW, double imgH, double aspect)
    {
        var w = Math.Min(imgW, imgH * aspect);
        var h = w / aspect;
        return new CropRect((imgW - w) / 2, (imgH - h) / 2, w, h);
    }

    public static CropRect Move(CropRect r, double dx, double dy, double imgW, double imgH) =>
        r with
        {
            X = Math.Clamp(r.X + dx, 0, imgW - r.W),
            Y = Math.Clamp(r.Y + dy, 0, imgH - r.H),
        };

    /// <summary>Skaluje kadr wokół środka (factor &lt; 1 przybliża), w granicach obrazu i z minimum 10% maksimum.</summary>
    public static CropRect Scale(CropRect r, double factor, double imgW, double imgH, double aspect)
    {
        var max = Math.Min(imgW, imgH * aspect);
        var w = Math.Clamp(r.W * factor, max * 0.1, max);
        var h = w / aspect;
        var scaled = new CropRect(r.X + (r.W - w) / 2, r.Y + (r.H - h) / 2, w, h);
        return Move(scaled, 0, 0, imgW, imgH);
    }
}

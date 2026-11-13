using System.Globalization;
using System.Net;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using FrameGallery.Localization;

namespace FrameGallery.Core;

/// <summary>
/// An error from talking to the TV. <see cref="Exception.Message"/> is localized and fit to show to the user;
/// <see cref="EnglishMessage"/> is the same text in English, so logs stay in English whatever the UI language is.
/// </summary>
public class TvException : Exception
{
    public string EnglishMessage { get; }

    /// <param name="resourceKey">Key of the message in the localization resources.</param>
    public TvException(string resourceKey, params object?[] args) : base(Loc.Get(resourceKey, args))
        => EnglishMessage = Loc.GetFor(CultureInfo.InvariantCulture, resourceKey, args);
}

/// <summary>The TV dropped the channel (e.g. after inactivity). <see cref="ArtClient"/> reconnects and retries the operation.</summary>
public sealed class TvConnectionLostException(string resourceKey, params object?[] args) : TvException(resourceKey, args);

/// <summary>
/// Diagnostic log written to a file and raised as the <see cref="Line"/> event. <see cref="Write"/> logs application events,
/// <see cref="Trace"/> logs raw protocol traffic (for comparing with other implementations and spotting differences).
/// Every line goes through <see cref="Redact"/> and is truncated, so a log is safe to attach to a bug report.
/// </summary>
public static class TvLog
{
    const int MaxLineChars = 4000;
    static readonly object Gate = new();
    static string? _path;

    public static event Action<string>? Line;

    public static void Write(string message) => Emit("INFO ", message);
    public static void Trace(string message) => Emit("TRACE", message);

    /// <summary>Exception type and its English message (never localized), for log lines.</summary>
    public static string Describe(Exception e) => $"{e.GetType().Name}: {(e is TvException t ? t.EnglishMessage : e.Message)}";

    /// <summary>Starts logging to a file; an existing file larger than <paramref name="maxBytes"/> is moved to *.old.</summary>
    public static void AttachFile(string path, string sessionHeader, long maxBytes = 5_000_000)
    {
        lock (Gate)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                if (File.Exists(path) && new FileInfo(path).Length > maxBytes) File.Move(path, path + ".old", overwrite: true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* logging must never crash the app */ }
            _path = path;
        }
        Emit("INFO ", $"=== session: {sessionHeader} ===");
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

    static string Truncate(string s) => s.Length <= MaxLineChars ? s : s[..MaxLineChars] + $"…(+{s.Length - MaxLineChars} chars)";

    static readonly System.Text.RegularExpressions.Regex Secrets = new(
        @"(\\?""(?:token|key|secKey|ssid|wifiMac|duid)\\?""\s*:\s*\\?"")[^""\\]*",
        System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>Masks secrets and network-identifying values, also inside JSON nested in a string.</summary>
    internal static string Redact(string s) => Secrets.Replace(s, "$1***");
}

/// <summary>
/// Certificate validation for the TV. The TV uses a self-signed certificate, so instead of disabling validation
/// we pin its SHA-256 thumbprint (trust on first use): without a pin we accept and remember <see cref="Observed"/>,
/// with a pin we accept only the same certificate.
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
    /// <summary>Local-network addresses only: 10/8, 172.16/12, 192.168/16 and link-local 169.254/16.</summary>
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

    /// <summary>Validates an address typed by the user; throws <see cref="TvException"/> for anything outside the LAN.</summary>
    public static IPAddress RequirePrivateIPv4(string text)
    {
        if (!IPAddress.TryParse(text.Trim(), out var ip) || ip.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
            throw new TvException("Err_InvalidIpv4");
        if (!IsPrivate(ip))
            throw new TvException("Err_NotLan");
        return ip;
    }
}

/// <summary>A crop rectangle with a fixed aspect ratio, in pixels of the source image.</summary>
public readonly record struct CropRect(double X, double Y, double W, double H);

public static class CropMath
{
    /// <summary>The largest centred crop with the given aspect ratio (W/H).</summary>
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

    /// <summary>Scales the crop around its centre (factor &lt; 1 zooms in), within the image and no smaller than 10% of the maximum.</summary>
    public static CropRect Scale(CropRect r, double factor, double imgW, double imgH, double aspect)
    {
        var max = Math.Min(imgW, imgH * aspect);
        var w = Math.Clamp(r.W * factor, max * 0.1, max);
        var h = w / aspect;
        var scaled = new CropRect(r.X + (r.W - w) / 2, r.Y + (r.H - h) / 2, w, h);
        return Move(scaled, 0, 0, imgW, imgH);
    }
}

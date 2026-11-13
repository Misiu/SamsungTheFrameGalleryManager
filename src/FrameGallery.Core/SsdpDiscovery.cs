using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;

namespace FrameGallery.Core;

/// <summary>
/// Wykrywanie TV Samsung przez SSDP (UPnP): multicast M-SEARCH na 239.255.255.250:1900 z każdego
/// interfejsu LAN. To tylko wstępny filtr: ostateczne potwierdzenie, że to Frame, daje <see cref="TvInfoClient"/>.
/// </summary>
public static class SsdpDiscovery
{
    const int MaxResponseBytes = 4096;
    static readonly IPEndPoint Group = new(IPAddress.Parse("239.255.255.250"), 1900);
    static readonly string[] SearchTargets = ["urn:samsung.com:device:RemoteControlReceiver:1", "upnp:rootdevice"];

    /// <returns>Adresy IP urządzeń, które odpowiedziały jak TV Samsunga.</returns>
    public static async Task<IReadOnlyCollection<IPAddress>> ScanAsync(TimeSpan? duration = null, CancellationToken ct = default)
    {
        var found = new ConcurrentDictionary<IPAddress, byte>();
        var d = duration ?? TimeSpan.FromSeconds(5);
        await Task.WhenAll(LocalAddresses().Select(a => ScanFromAsync(a, d, found, ct)));
        return found.Keys.ToList();
    }

    /// <summary>
    /// Adresy interfejsów LAN, z których wysyłamy zapytania. Gdy platforma nie pozwala ich wyliczyć (np. Android bez uprawnień)
    /// albo nic nie znaleziono, jedno gniazdo na dowolnym interfejsie (system wybierze trasę).
    /// Uwaga platformowa: Android wymaga MulticastLock, a iOS uprawnienia sieci lokalnej i entitlementu multicast;
    /// bez nich skan nic nie znajdzie, a dodanie TV po IP nadal działa.
    /// </summary>
    static List<IPAddress> LocalAddresses()
    {
        try
        {
            var list = NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up && n.SupportsMulticast
                            && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .SelectMany(n => n.GetIPProperties().UnicastAddresses)
                .Select(a => a.Address)
                .Where(Lan.IsPrivate)
                .Distinct()
                .ToList();
            if (list.Count > 0) return list;
        }
        catch (Exception e) when (e is NetworkInformationException or PlatformNotSupportedException or UnauthorizedAccessException)
        {
            TvLog.Write($"[SSDP] nie można wyliczyć interfejsów: {e.Message}");
        }
        return [IPAddress.Any];
    }

    static async Task ScanFromAsync(IPAddress local, TimeSpan duration, ConcurrentDictionary<IPAddress, byte> found, CancellationToken ct)
    {
        try
        {
            using var udp = new UdpClient(new IPEndPoint(local, 0));
            if (!local.Equals(IPAddress.Any))
                udp.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastInterface, local.GetAddressBytes());
            udp.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, 2);

            // UDP gubi pakiety, więc każde zapytanie idzie dwa razy.
            for (var round = 0; round < 2; round++)
                foreach (var st in SearchTargets)
                    await udp.SendAsync(Encoding.ASCII.GetBytes(BuildSearch(st)), Group, ct);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(duration);
            var loggedOthers = new HashSet<IPAddress>();
            while (true)
            {
                UdpReceiveResult r;
                try { r = await udp.ReceiveAsync(cts.Token); }
                catch (SocketException e) when (e.SocketErrorCode == SocketError.ConnectionReset) { continue; } // Windows: ICMP po wcześniejszym send

                if (r.Buffer.Length > MaxResponseBytes || !Lan.IsPrivate(r.RemoteEndPoint.Address))
                {
                    TvLog.Trace($"SSDP << {r.RemoteEndPoint} odrzucone ({r.Buffer.Length} B, adres spoza LAN lub za duże)");
                    continue;
                }
                var text = Encoding.UTF8.GetString(r.Buffer);
                var samsung = IsSamsungTvResponse(text);
                if (samsung || loggedOthers.Add(r.RemoteEndPoint.Address))   // inne urządzenia (np. router) odpowiadają wielokrotnie: tylko pierwsza odpowiedź
                    TvLog.Trace($"SSDP << {r.RemoteEndPoint} samsungTV={samsung} {text.ReplaceLineEndings(" | ")}");
                if (samsung && found.TryAdd(r.RemoteEndPoint.Address, 0))
                    TvLog.Write($"[SSDP] Samsung na {r.RemoteEndPoint.Address} (interfejs {local})");
            }
        }
        catch (OperationCanceledException) { }
        catch (SocketException e) { TvLog.Write($"[SSDP] Interfejs {local}: {e.Message}"); }
    }

    static string BuildSearch(string st) =>
        $"M-SEARCH * HTTP/1.1\r\nHOST: 239.255.255.250:1900\r\nMAN: \"ssdp:discover\"\r\nMX: 2\r\nST: {st}\r\n\r\n";

    /// <summary>
    /// Czy odpowiedź SSDP wygląda na TV Samsunga: usługa RemoteControlReceiver w ST/USN albo
    /// nagłówek Server ze słowem „samsung” (z wykluczeniem routerów, drukarek i skanerów).
    /// </summary>
    public static bool IsSamsungTvResponse(string response)
    {
        var h = ParseHeaders(response);
        if (h is null || !response.Contains("samsung", StringComparison.OrdinalIgnoreCase)) return false;

        var stUsn = (h.GetValueOrDefault("st") + " " + h.GetValueOrDefault("usn")).ToLowerInvariant();
        if (stUsn.Contains("remotecontrolreceiver") || stUsn.Contains("remotereceiver")) return true;

        var server = h.GetValueOrDefault("server")?.ToLowerInvariant() ?? "";
        return server.Contains("samsung")
            && !(server.Contains("router") || server.Contains("printer") || server.Contains("scanner"));
    }

    static Dictionary<string, string>? ParseHeaders(string response)
    {
        var lines = response.Split("\r\n");
        if (lines.Length == 0 || !lines[0].StartsWith("HTTP/1.1 200", StringComparison.Ordinal)) return null;
        var h = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines.Skip(1))
        {
            var i = line.IndexOf(':');
            if (i > 0) h[line[..i].Trim()] = line[(i + 1)..].Trim();
        }
        return h;
    }
}

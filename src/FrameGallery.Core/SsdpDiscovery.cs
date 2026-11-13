using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;

namespace FrameGallery.Core;

/// <summary>
/// Samsung TV discovery over SSDP (UPnP): multicast M-SEARCH to 239.255.255.250:1900 from every LAN interface.
/// This is only a first filter: <see cref="TvInfoClient"/> gives the final confirmation that the device is a Frame.
/// </summary>
public static class SsdpDiscovery
{
    const int MaxResponseBytes = 4096;
    static readonly IPEndPoint Group = new(IPAddress.Parse("239.255.255.250"), 1900);
    static readonly string[] SearchTargets = ["urn:samsung.com:device:RemoteControlReceiver:1", "upnp:rootdevice"];

    /// <returns>IP addresses of devices that answered like a Samsung TV.</returns>
    public static async Task<IReadOnlyCollection<IPAddress>> ScanAsync(TimeSpan? duration = null, CancellationToken ct = default)
    {
        var found = new ConcurrentDictionary<IPAddress, byte>();
        var d = duration ?? TimeSpan.FromSeconds(5);
        await Task.WhenAll(LocalAddresses().Select(a => ScanFromAsync(a, d, found, ct)));
        return found.Keys.ToList();
    }

    /// <summary>
    /// Addresses of the LAN interfaces to send queries from. If the platform does not let us enumerate them
    /// (e.g. Android without permissions) or nothing is found, one socket on any interface is used (the OS picks the route).
    /// Platform note: Android needs a MulticastLock, and iOS needs the local network permission and the multicast entitlement;
    /// without them a scan finds nothing, while adding a TV by IP still works.
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
            TvLog.Write($"[SSDP] cannot enumerate network interfaces: {e.Message}");
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

            // UDP drops packets, so every query is sent twice.
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
                catch (SocketException e) when (e.SocketErrorCode == SocketError.ConnectionReset) { continue; } // Windows: ICMP error after an earlier send

                if (r.Buffer.Length > MaxResponseBytes || !Lan.IsPrivate(r.RemoteEndPoint.Address))
                {
                    TvLog.Trace($"SSDP << {r.RemoteEndPoint} rejected ({r.Buffer.Length} B, address outside the LAN or response too large)");
                    continue;
                }
                var text = Encoding.UTF8.GetString(r.Buffer);
                var samsung = IsSamsungTvResponse(text);
                // Other devices (e.g. the router) answer many times: log only their first response.
                if (samsung || loggedOthers.Add(r.RemoteEndPoint.Address))
                    TvLog.Trace($"SSDP << {r.RemoteEndPoint} samsungTV={samsung} {text.ReplaceLineEndings(" | ")}");
                if (samsung && found.TryAdd(r.RemoteEndPoint.Address, 0))
                    TvLog.Write($"[SSDP] Samsung device at {r.RemoteEndPoint.Address} (interface {local})");
            }
        }
        catch (OperationCanceledException) { }
        catch (SocketException e) { TvLog.Write($"[SSDP] interface {local}: {e.Message}"); }
    }

    static string BuildSearch(string st) =>
        $"M-SEARCH * HTTP/1.1\r\nHOST: 239.255.255.250:1900\r\nMAN: \"ssdp:discover\"\r\nMX: 2\r\nST: {st}\r\n\r\n";

    /// <summary>
    /// Whether an SSDP response looks like a Samsung TV: a RemoteControlReceiver service in ST/USN, or a Server header
    /// containing "samsung" (excluding routers, printers and scanners).
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

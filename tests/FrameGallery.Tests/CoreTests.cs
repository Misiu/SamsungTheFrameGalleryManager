using System.Buffers.Binary;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using FrameGallery.Core;

namespace FrameGallery.Tests;

public class SsdpTests
{
    const string SamsungTv =
        "HTTP/1.1 200 OK\r\nCACHE-CONTROL: max-age=1800\r\nLOCATION: http://192.168.1.50:7676/smp_7_\r\n" +
        "SERVER: SHP, UPnP/1.0, Samsung UPnP SDK/1.0\r\nST: urn:samsung.com:device:RemoteControlReceiver:1\r\n" +
        "USN: uuid:abc::urn:samsung.com:device:RemoteControlReceiver:1\r\n\r\n";

    [Fact] public void Samsung_remote_receiver_is_accepted() => Assert.True(SsdpDiscovery.IsSamsungTvResponse(SamsungTv));

    [Fact]
    public void Samsung_printer_is_rejected() => Assert.False(SsdpDiscovery.IsSamsungTvResponse(
        "HTTP/1.1 200 OK\r\nST: upnp:rootdevice\r\nSERVER: Samsung printer UPnP/1.0\r\nUSN: uuid:x::upnp:rootdevice\r\n\r\n"));

    [Fact]
    public void Non_samsung_and_non_response_are_rejected()
    {
        Assert.False(SsdpDiscovery.IsSamsungTvResponse("HTTP/1.1 200 OK\r\nST: upnp:rootdevice\r\nSERVER: Linux UPnP/1.0 router\r\n\r\n"));
        Assert.False(SsdpDiscovery.IsSamsungTvResponse(SamsungTv.Replace("HTTP/1.1 200 OK", "NOTIFY * HTTP/1.1")));
    }

    [Theory]
    [InlineData("192.168.1.5", true)] [InlineData("10.0.0.1", true)] [InlineData("172.16.0.1", true)] [InlineData("172.32.0.1", false)]
    [InlineData("8.8.8.8", false)] [InlineData("127.0.0.1", false)] [InlineData("169.254.1.1", true)]
    public void Lan_check(string ip, bool expected) => Assert.Equal(expected, Lan.IsPrivate(IPAddress.Parse(ip)));

    [Fact] public void Manual_ip_must_be_lan() => Assert.Throws<TvException>(() => Lan.RequirePrivateIPv4("8.8.8.8"));
}

public class CropTests
{
    [Fact]
    public void Initial_crop_is_centered_16x9()
    {
        var r = CropMath.Initial(4000, 3000, 16.0 / 9);
        Assert.Equal(4000, r.W, 3); Assert.Equal(2250, r.H, 3); Assert.Equal(0, r.X, 3); Assert.Equal(375, r.Y, 3);
    }

    [Fact]
    public void Move_and_scale_stay_inside_image()
    {
        var r = CropMath.Initial(4000, 3000, 16.0 / 9);
        var moved = CropMath.Move(r, 999, 999, 4000, 3000);
        Assert.Equal(750, moved.Y, 3);          // 3000 - 2250
        Assert.Equal(0, moved.X, 3);

        var s = CropMath.Scale(r, 0.5, 4000, 3000, 16.0 / 9);
        Assert.Equal(2000, s.W, 3);
        Assert.InRange(s.X, 0, 4000 - s.W); Assert.InRange(s.Y, 0, 3000 - s.H);
        Assert.Equal(16.0 / 9, s.W / s.H, 6);

        Assert.Equal(400, CropMath.Scale(r, 0.0001, 4000, 3000, 16.0 / 9).W, 3);  // minimum 10%
        Assert.Equal(4000, CropMath.Scale(r, 9, 4000, 3000, 16.0 / 9).W, 3);      // maksimum = szerokość obrazu
    }
}

public class ArtProtocolTests
{
    [Fact]
    public void Upload_frame_is_length_prefixed_header_then_data()
    {
        byte[] data = [1, 2, 3, 4, 5];
        var frame = ArtClient.BuildUploadFrame(data, "KEY123", "jpg");

        var hl = BinaryPrimitives.ReadInt32BigEndian(frame);
        using var h = JsonDocument.Parse(frame.AsMemory(4, hl));
        Assert.Equal(5, h.RootElement.GetProperty("fileLength").GetInt32());
        Assert.Equal("KEY123", h.RootElement.GetProperty("secKey").GetString());
        Assert.Equal("jpg", h.RootElement.GetProperty("fileType").GetString());
        Assert.Equal(data, frame[(4 + hl)..]);
    }

    [Theory]
    [InlineData("""{"conn_info":"{\"ip\":\"1.2.3.4\",\"port\":\"7000\",\"key\":\"K\",\"secured\":true}"}""")]
    [InlineData("""{"conn_info":{"ip":"1.2.3.4","port":7000,"key":"K","secured":"true"}}""")]
    public void Conn_info_accepts_string_or_object(string json)
    {
        using var d = JsonDocument.Parse(json);
        Assert.Equal(new ConnInfo(7000, "K", true), ArtClient.ParseConnInfo(d.RootElement));
    }

    [Fact]
    public void Conn_info_rejects_bad_port()
    {
        using var d = JsonDocument.Parse("""{"conn_info":{"port":99999,"key":"K"}}""");
        Assert.Throws<TvException>(() => ArtClient.ParseConnInfo(d.RootElement));
    }

    [Fact]
    public void Content_list_parses_string_encoded_array()
    {
        var inner = """[{"content_id":"MY_F0001","category_id":"MY-C0002","width":3840,"height":2160,"image_date":"2024:01:01 10:00:00"},{"nope":1}]""";
        using var d = JsonDocument.Parse(JsonSerializer.Serialize(new { content_list = inner }));
        var items = ArtClient.ParseContentList(d.RootElement);
        var item = Assert.Single(items);
        Assert.Equal("MY_F0001", item.ContentId); Assert.Equal(ArtClient.MyPhotosCategory, item.CategoryId); Assert.Equal(3840, item.Width);
    }
}

public class RealTvResponseTests
{
    // Struktura odpowiedzi prawdziwego The Frame (z logu): zdarzenie = nazwa żądania, content_list jako string.
    const string Inner = """
        {"event":"get_content_list","request_id":"r1","content_list":"[{\"content_id\":\"MY_F0004\",\"category_id\":\"MY-C0008\",\"matte_id\":\"none\",\"width\":3840,\"height\":2160,\"image_date\":\"2026:11:13 10:07:1\"},{\"content_id\":\"MY_F0004\",\"category_id\":\"MY-C0002\",\"matte_id\":\"none\",\"width\":3840,\"height\":2160,\"image_date\":\"2026:11:13 10:07:1\"},{\"content_id\":\"SAM-F0222\",\"category_id\":\"MY-C0008\",\"image_date\":\"\"}]"}
        """;

    [Fact]
    public void Content_list_response_is_recognised_by_field_not_event_name()
    {
        using var d = JsonDocument.Parse(Inner);
        Assert.True(ArtClient.IsContentListResponse(d.RootElement));
        Assert.Equal(3, ArtClient.ParseContentList(d.RootElement).Count);
        Assert.False(ArtClient.IsContentListResponse(JsonDocument.Parse("""{"event":"ms.channel.ready"}""").RootElement));
    }
}

public class TlsPolicyTests
{
    static X509Certificate2 SelfSigned()
    {
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest("CN=tv", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
    }

    [Fact]
    public void Pin_is_recorded_then_enforced()
    {
        using var a = SelfSigned(); using var b = SelfSigned();

        var first = new TlsPolicy(null);
        Assert.True(first.Validate(null, a, null, default));
        Assert.NotNull(first.Observed);

        var pinned = new TlsPolicy(first.Observed);
        Assert.True(pinned.Validate(null, a, null, default));
        Assert.False(pinned.Validate(null, b, null, default));   // inny certyfikat = odrzucony
        Assert.False(pinned.Validate(null, null, null, default));
    }
}

public class LogTests
{
    [Fact]
    public void Secrets_are_masked_in_plain_and_nested_json()
    {
        var plain = TvLog.Redact("""{"data":{"token":"12345678","ssid":"MojaSiec","wifiMac":"AA:BB","name":"TV"}}""");
        Assert.DoesNotContain("12345678", plain); Assert.DoesNotContain("MojaSiec", plain); Assert.DoesNotContain("AA:BB", plain);
        Assert.Contains("\"name\":\"TV\"", plain);   // reszta zostaje

        // JSON zagnieżdżony w stringu (tak TV opakowuje art_app_request/d2d_service_message)
        var nested = TvLog.Redact("""{"data":"{\"conn_info\":{\"key\":\"SECRET\",\"port\":7000},\"secKey\":\"SEC2\"}"}""");
        Assert.DoesNotContain("SECRET", nested); Assert.DoesNotContain("SEC2", nested);
        Assert.Contains("7000", nested);
    }

    [Fact]
    public void Log_goes_to_file_masked_and_truncated()
    {
        var path = Path.Combine(Path.GetTempPath(), $"fg-log-{Guid.NewGuid():N}.txt");
        try
        {
            TvLog.AttachFile(path, "test");
            TvLog.Trace("""WS << {"token":"TOPSECRET"}""" + new string('x', 5000));
            var text = File.ReadAllText(path);
            Assert.Contains("=== sesja: test ===", text);
            Assert.Contains("[TRACE]", text);
            Assert.DoesNotContain("TOPSECRET", text);
            Assert.Contains("znaków)", text);   // przycięte
        }
        finally { try { File.Delete(path); } catch { } }
    }
}

public class TvInfoTests
{
    [Fact]
    public void Parses_frame_device_info()
    {
        var json = """{"device":{"FrameTVSupport":"true","id":"uuid:1","name":"[TV] Frame","modelName":"QE55LS03","resolution":"3840x2160"}}""";
        var i = TvInfoClient.Parse(json, "192.168.1.50");
        Assert.True(i.IsFrame); Assert.Equal("uuid:1", i.Id); Assert.Equal((3840, 2160), (i.Width, i.Height));
    }

    [Fact]
    public void Html_encoded_name_is_decoded()
    {
        var i = TvInfoClient.Parse("""{"device":{"id":"uuid:1","name":"65&quot; The Frame"}}""", "192.168.1.50");
        Assert.Equal("65\" The Frame", i.Name);
    }

    [Fact] public void Garbage_is_rejected() => Assert.Throws<TvException>(() => TvInfoClient.Parse("<html/>", "192.168.1.50"));
}

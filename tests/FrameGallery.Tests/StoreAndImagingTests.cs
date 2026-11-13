using FrameGallery.Core;
using FrameGallery.Imaging;
using SkiaSharp;

namespace FrameGallery.Tests;

public class TvStoreTests
{
    /// <summary>A fake protector: reverses the text and adds a prefix, so a plain token can never appear in the file.</summary>
    sealed class FakeProtector : ISecretProtector
    {
        public string Protect(string plainText) => "enc:" + new string(plainText.Reverse().ToArray());
        public string? Unprotect(string protectedText) => protectedText.StartsWith("enc:") ? new string(protectedText[4..].Reverse().ToArray()) : null;
    }

    static TvInfo Info(string id, string ip) => new(id, "TV", "QE65", ip, true, 3840, 2160);

    [Fact]
    public void Token_is_encrypted_in_file_and_survives_reload()
    {
        var path = Path.Combine(Path.GetTempPath(), $"fg-store-{Guid.NewGuid():N}.json");
        try
        {
            var store = new TvStore(path, new FakeProtector());
            var tv = store.Upsert(Info("uuid:1", "192.168.1.5"), "PINABC");
            store.SetToken(tv, "SECRET-TOKEN");
            store.Save();

            Assert.DoesNotContain("SECRET-TOKEN", File.ReadAllText(path));

            var reloaded = new TvStore(path, new FakeProtector());
            var tv2 = Assert.Single(reloaded.Items);
            Assert.True(tv2.IsPaired);
            Assert.Equal("SECRET-TOKEN", reloaded.GetToken(tv2));
            Assert.Equal("PINABC", tv2.CertSha256);
        }
        finally { try { File.Delete(path); } catch { } }
    }

    [Fact]
    public void Upsert_keeps_token_and_pin_and_replaces_other_device_on_same_ip()
    {
        var store = new TvStore(Path.Combine(Path.GetTempPath(), $"fg-store-{Guid.NewGuid():N}.json"), new FakeProtector());
        var tv = store.Upsert(Info("uuid:1", "192.168.1.5"), "PIN1");
        store.SetToken(tv, "T");

        store.Upsert(Info("uuid:1", "192.168.1.9"), "PIN-INNY");          // the same TV, a new address from DHCP
        Assert.Equal("192.168.1.9", tv.Ip);
        Assert.Equal("PIN1", tv.CertSha256);                               // the pin stays (trust on first use)
        Assert.Equal("T", store.GetToken(tv));

        store.Upsert(Info("uuid:2", "192.168.1.9"), null);                 // a different device at that address
        Assert.Equal("uuid:2", Assert.Single(store.Items).Id);
    }

    [Fact]
    public void Token_that_cannot_be_decrypted_is_treated_as_unpaired()
    {
        var store = new TvStore(Path.Combine(Path.GetTempPath(), $"fg-store-{Guid.NewGuid():N}.json"), new FakeProtector());
        var tv = store.Upsert(Info("uuid:1", "192.168.1.5"), null);
        tv.TokenProtected = "obcy-format";                                 // e.g. a file from another computer
        Assert.Null(store.GetToken(tv));
    }
}

public class ImagingTests
{
    static SKBitmap TwoPixels()
    {
        var bmp = new SKBitmap(2, 1);
        bmp.SetPixel(0, 0, SKColors.Red);
        bmp.SetPixel(1, 0, SKColors.Blue);
        return bmp;
    }

    [Fact]
    public void Rotate_90_clockwise_puts_left_pixel_on_top()
    {
        using var src = TwoPixels();
        using var r = ImagePipeline.ApplyOrigin(src, SKEncodedOrigin.RightTop);
        Assert.Equal((1, 2), (r.Width, r.Height));
        Assert.Equal(SKColors.Red, r.GetPixel(0, 0));
        Assert.Equal(SKColors.Blue, r.GetPixel(0, 1));
    }

    [Fact]
    public void Rotate_90_counterclockwise_puts_right_pixel_on_top()
    {
        using var src = TwoPixels();
        using var r = ImagePipeline.ApplyOrigin(src, SKEncodedOrigin.LeftBottom);
        Assert.Equal((1, 2), (r.Width, r.Height));
        Assert.Equal(SKColors.Blue, r.GetPixel(0, 0));
        Assert.Equal(SKColors.Red, r.GetPixel(0, 1));
    }

    [Fact]
    public void Rotate_180_swaps_pixels_horizontally()
    {
        using var src = TwoPixels();
        using var r = ImagePipeline.ApplyOrigin(src, SKEncodedOrigin.BottomRight);
        Assert.Equal((2, 1), (r.Width, r.Height));
        Assert.Equal(SKColors.Blue, r.GetPixel(0, 0));
        Assert.Equal(SKColors.Red, r.GetPixel(1, 0));
    }

    [Fact]
    public void Crop_produces_jpeg_of_exact_target_size()
    {
        using var src = new SKBitmap(400, 300);
        src.Erase(SKColors.SteelBlue);
        var crop = CropMath.Initial(400, 300, 16.0 / 9);

        var jpeg = ImagePipeline.CropToJpeg(src, crop, 192, 108);

        Assert.True(jpeg.Length > 2 && jpeg[0] == 0xFF && jpeg[1] == 0xD8);   // the JPEG header
        using var back = SKBitmap.Decode(jpeg);
        Assert.Equal((192, 108), (back.Width, back.Height));
    }

    [Fact]
    public void Garbage_is_not_an_image() =>
        Assert.Throws<InvalidDataException>(() => ImagePipeline.Load(new MemoryStream([1, 2, 3, 4])));
}

using FrameGallery.Core;
using SkiaSharp;

namespace FrameGallery.Desktop.Demo;

/// <summary>
/// A fake gallery for the demo mode (<c>FrameGallery --demo</c>): generated pictures and deliberate delays, so the spinners,
/// the fade-in and the upload progress can be seen without a TV.
/// </summary>
sealed class DemoArtClient : IArtClient
{
    readonly List<ArtItem> _items = [];
    int _nextNumber = 15;

    public DemoArtClient()
    {
        for (var i = 1; i <= 14; i++)
            _items.Add(new ArtItem($"MY_F{i:D4}", ArtClient.MyPhotosCategory, "none", 3840, 2160, $"2026:11:{i + 5:D2} 1{i % 10}:{i * 7 % 60:D2}:00"));
        for (var i = 1; i <= 5; i++)
            _items.Add(new ArtItem($"SAM-S{1000 + i}", "MY-C0004", "NONE", 3840, 2160, ""));
    }

    public async Task<IReadOnlyList<ArtItem>> ListAsync(CancellationToken ct)
    {
        await Task.Delay(1200, ct);
        return _items.ToList();
    }

    public async Task<IReadOnlyDictionary<string, byte[]>> GetThumbnailsAsync(IReadOnlyList<string> contentIds, CancellationToken ct)
    {
        await Task.Delay(1400, ct);
        return contentIds.ToDictionary(id => id, id => MakePicture(id.GetHashCode()));
    }

    public Task SelectImageAsync(string contentId, bool show, CancellationToken ct) => Task.Delay(700, ct);

    public Task<string?> GetCurrentArtworkAsync(CancellationToken ct) => Task.FromResult<string?>(null);

    public async Task DeleteAsync(IReadOnlyList<string> contentIds, CancellationToken ct)
    {
        await Task.Delay(900, ct);
        _items.RemoveAll(i => contentIds.Contains(i.ContentId));
    }

    public async Task<string> UploadAsync(byte[] jpeg, CancellationToken ct, IProgress<double>? progress = null)
    {
        for (var step = 1; step <= 40; step++)
        {
            await Task.Delay(60, ct);
            progress?.Report(step / 40.0);
        }
        await Task.Delay(1500, ct);   // the TV saving the image
        var id = $"MY_F{_nextNumber++:D4}";
        _items.Insert(0, new ArtItem(id, ArtClient.MyPhotosCategory, "none", 3840, 2160, DateTime.Now.ToString("yyyy:MM:dd HH:mm:ss")));
        return id;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    /// <summary>A small landscape: a sky gradient, a sun and a mountain range, in a colour that depends on the seed.</summary>
    static byte[] MakePicture(int seed)
    {
        var hue = (uint)(Math.Abs(seed) % 360);
        using var bitmap = new SKBitmap(240, 135);
        using var canvas = new SKCanvas(bitmap);
        using var paint = new SKPaint { IsAntialias = true };

        paint.Shader = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(0, 135),
            [SKColor.FromHsv(hue, 40, 98), SKColor.FromHsv((hue + 50) % 360, 65, 80)], SKShaderTileMode.Clamp);
        canvas.DrawRect(0, 0, 240, 135, paint);

        paint.Shader = null;
        paint.Color = new SKColor(255, 255, 255, 215);
        canvas.DrawCircle(30 + Math.Abs(seed) % 180, 38, 15, paint);

        using var ridge = new SKPath();
        ridge.MoveTo(0, 135); ridge.LineTo(0, 98); ridge.LineTo(55, 62 + Math.Abs(seed) % 18); ridge.LineTo(110, 102);
        ridge.LineTo(165, 72); ridge.LineTo(240, 108); ridge.LineTo(240, 135); ridge.Close();
        paint.Color = SKColor.FromHsv(hue, 55, 42);
        canvas.DrawPath(ridge, paint);

        using var image = SKImage.FromBitmap(bitmap);
        return image.Encode(SKEncodedImageFormat.Jpeg, 85).ToArray();
    }
}

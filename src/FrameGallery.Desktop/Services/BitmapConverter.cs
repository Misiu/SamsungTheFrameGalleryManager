using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using SkiaSharp;

namespace FrameGallery.Desktop;

static class BitmapConverter
{
    /// <summary>
    /// Copies a SkiaSharp bitmap into a bitmap Avalonia can draw, scaled down so that its longest side is at most
    /// <paramref name="maxSide"/> (a preview does not need the full resolution of a 24-megapixel photo).
    /// </summary>
    public static Bitmap ToAvalonia(SKBitmap source, int maxSide = 2048)
    {
        var scale = Math.Min(1.0, (double)maxSide / Math.Max(source.Width, source.Height));
        var info = new SKImageInfo(Math.Max(1, (int)(source.Width * scale)), Math.Max(1, (int)(source.Height * scale)),
            SKColorType.Bgra8888, SKAlphaType.Premul);
        using var scaled = source.Resize(info, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear))
                           ?? throw new InvalidOperationException("The preview could not be created.");

        var bitmap = new WriteableBitmap(new PixelSize(info.Width, info.Height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        using var buffer = bitmap.Lock();
        var pixels = scaled.Bytes;
        for (var y = 0; y < info.Height; y++)
            Marshal.Copy(pixels, y * scaled.RowBytes, buffer.Address + y * buffer.RowBytes, info.Width * 4);
        return bitmap;
    }
}

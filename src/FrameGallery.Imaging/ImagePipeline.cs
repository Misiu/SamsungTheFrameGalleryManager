using FrameGallery.Core;
using SkiaSharp;

namespace FrameGallery.Imaging;

/// <summary>Loads a photo (honouring EXIF orientation) and prepares a JPEG with exactly the TV screen's resolution.</summary>
public static class ImagePipeline
{
    /// <summary>Loads an image and rotates it according to EXIF, as the user sees it.</summary>
    /// <exception cref="InvalidDataException">The file is not a supported image (e.g. HEIC without a codec).</exception>
    public static SKBitmap Load(Stream stream)
    {
        using var codec = SKCodec.Create(stream) ?? throw new InvalidDataException("Unsupported image format.");
        var bitmap = SKBitmap.Decode(codec) ?? throw new InvalidDataException("The image could not be decoded.");
        if (codec.EncodedOrigin == SKEncodedOrigin.TopLeft) return bitmap;
        using (bitmap) return ApplyOrigin(bitmap, codec.EncodedOrigin);
    }

    public static SKBitmap Load(string path)
    {
        using var fs = File.OpenRead(path);
        return Load(fs);
    }

    /// <summary>Cuts out <paramref name="crop"/>, scales it to exactly width×height and encodes it as JPEG.</summary>
    public static byte[] CropToJpeg(SKBitmap source, CropRect crop, int width, int height, int quality = 92)
    {
        using var dst = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Opaque));
        using (var canvas = new SKCanvas(dst))
        {
            canvas.Clear(SKColors.Black);   // transparent PNGs get a black background
            using var image = SKImage.FromBitmap(source);
            var from = new SKRect((float)crop.X, (float)crop.Y, (float)(crop.X + crop.W), (float)(crop.Y + crop.H));
            canvas.DrawImage(image, from, new SKRect(0, 0, width, height),
                new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear), paint: null);
        }
        using var result = SKImage.FromBitmap(dst);
        using var data = result.Encode(SKEncodedImageFormat.Jpeg, quality) ?? throw new InvalidOperationException("JPEG encoding failed.");
        return data.ToArray();
    }

    /// <summary>Applies the EXIF orientation (8 variants); for 5-8 the width and height swap.</summary>
    internal static SKBitmap ApplyOrigin(SKBitmap src, SKEncodedOrigin origin)
    {
        float w = src.Width, h = src.Height;
        var swap = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;

        // The matrices map source coordinates to destination ones: x' = a·x + b·y + tx, y' = c·x + d·y + ty.
        var (a, b, tx, c, d, ty) = origin switch
        {
            SKEncodedOrigin.TopRight => (-1f, 0f, w, 0f, 1f, 0f),        // mirror horizontally
            SKEncodedOrigin.BottomRight => (-1f, 0f, w, 0f, -1f, h),     // rotate 180°
            SKEncodedOrigin.BottomLeft => (1f, 0f, 0f, 0f, -1f, h),      // mirror vertically
            SKEncodedOrigin.LeftTop => (0f, 1f, 0f, 1f, 0f, 0f),         // transpose
            SKEncodedOrigin.RightTop => (0f, -1f, h, 1f, 0f, 0f),        // rotate 90° clockwise
            SKEncodedOrigin.RightBottom => (0f, -1f, h, -1f, 0f, w),     // transverse
            SKEncodedOrigin.LeftBottom => (0f, 1f, 0f, -1f, 0f, w),      // rotate 90° counter-clockwise
            _ => (1f, 0f, 0f, 0f, 1f, 0f),
        };

        var dst = new SKBitmap(swap ? src.Height : src.Width, swap ? src.Width : src.Height, src.ColorType, src.AlphaType);
        using var canvas = new SKCanvas(dst);
        canvas.SetMatrix(new SKMatrix(a, b, tx, c, d, ty, 0, 0, 1));
        using var image = SKImage.FromBitmap(src);
        canvas.DrawImage(image, 0, 0, new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None), paint: null);   // an integer transform = a 1:1 copy
        return dst;
    }
}

using FrameGallery.Core;
using SkiaSharp;

namespace FrameGallery.Imaging;

/// <summary>Wczytanie zdjęcia (z orientacją EXIF) i przygotowanie JPEG-a o dokładnie takiej rozdzielczości jak ekran TV.</summary>
public static class ImagePipeline
{
    /// <summary>Wczytuje obraz i obraca go zgodnie z EXIF, tak jak widzi go użytkownik.</summary>
    /// <exception cref="InvalidDataException">Plik nie jest obsługiwanym obrazem (np. HEIC bez kodeka).</exception>
    public static SKBitmap Load(Stream stream)
    {
        using var codec = SKCodec.Create(stream) ?? throw new InvalidDataException("Nieobsługiwany format obrazu.");
        var bitmap = SKBitmap.Decode(codec) ?? throw new InvalidDataException("Nie udało się zdekodować obrazu.");
        if (codec.EncodedOrigin == SKEncodedOrigin.TopLeft) return bitmap;
        using (bitmap) return ApplyOrigin(bitmap, codec.EncodedOrigin);
    }

    public static SKBitmap Load(string path)
    {
        using var fs = File.OpenRead(path);
        return Load(fs);
    }

    /// <summary>Wycina <paramref name="crop"/>, skaluje do dokładnie w×h i koduje jako JPEG.</summary>
    public static byte[] CropToJpeg(SKBitmap source, CropRect crop, int width, int height, int quality = 92)
    {
        using var dst = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Opaque));
        using (var canvas = new SKCanvas(dst))
        {
            canvas.Clear(SKColors.Black);   // przezroczyste PNG-i dostają czarne tło
            using var image = SKImage.FromBitmap(source);
            var from = new SKRect((float)crop.X, (float)crop.Y, (float)(crop.X + crop.W), (float)(crop.Y + crop.H));
            canvas.DrawImage(image, from, new SKRect(0, 0, width, height),
                new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear), paint: null);
        }
        using var result = SKImage.FromBitmap(dst);
        using var data = result.Encode(SKEncodedImageFormat.Jpeg, quality) ?? throw new InvalidOperationException("Kodowanie JPEG nie powiodło się.");
        return data.ToArray();
    }

    /// <summary>Stosuje orientację EXIF (8 wariantów); dla 5-8 szerokość i wysokość się zamieniają.</summary>
    internal static SKBitmap ApplyOrigin(SKBitmap src, SKEncodedOrigin origin)
    {
        float w = src.Width, h = src.Height;
        var swap = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;

        // Macierze przekształcają współrzędne źródła na docelowe: x' = a·x + b·y + tx, y' = c·x + d·y + ty.
        var (a, b, tx, c, d, ty) = origin switch
        {
            SKEncodedOrigin.TopRight => (-1f, 0f, w, 0f, 1f, 0f),        // odbicie w poziomie
            SKEncodedOrigin.BottomRight => (-1f, 0f, w, 0f, -1f, h),     // obrót o 180°
            SKEncodedOrigin.BottomLeft => (1f, 0f, 0f, 0f, -1f, h),      // odbicie w pionie
            SKEncodedOrigin.LeftTop => (0f, 1f, 0f, 1f, 0f, 0f),         // transpozycja
            SKEncodedOrigin.RightTop => (0f, -1f, h, 1f, 0f, 0f),        // obrót o 90° w prawo
            SKEncodedOrigin.RightBottom => (0f, -1f, h, -1f, 0f, w),     // transwersja
            SKEncodedOrigin.LeftBottom => (0f, 1f, 0f, -1f, 0f, w),      // obrót o 90° w lewo
            _ => (1f, 0f, 0f, 0f, 1f, 0f),
        };

        var dst = new SKBitmap(swap ? src.Height : src.Width, swap ? src.Width : src.Height, src.ColorType, src.AlphaType);
        using var canvas = new SKCanvas(dst);
        canvas.SetMatrix(new SKMatrix(a, b, tx, c, d, ty, 0, 0, 1));
        using var image = SKImage.FromBitmap(src);
        canvas.DrawImage(image, 0, 0, new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None), paint: null);   // przekształcenie całkowite = kopia 1:1
        return dst;
    }
}

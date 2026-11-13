// Builds the application icon from the artwork. Run it from the repository root:
//
//     dotnet run assets/icon/BuildIcon.cs
//
// Input : assets/icon/source.webp  the original artwork (1254x1254, opaque, a rounded tile on a white background with a soft shadow)
// Output: assets/icon/icon.png     the artwork cropped to the tile with a transparent background (512x512), used by the README
//         assets/icon/AppIcon.ico  the Windows icon with the sizes 16, 24, 32, 48, 64, 128 and 256
//
// The source has no alpha channel, so the transparency is rebuilt from the tile's geometry. The tile is a "squircle", the
// superellipse |x/a|^n + |y/b|^n = 1, whose outline was measured on the artwork: left 89, right 1162, top 77.5, bottom 1154,
// n = 4.6 (source pixels). Everything outside it (the white background and the drop shadow) becomes transparent.
// If the artwork changes, re-measure these values.
#:package SkiaSharp@4.156.0
#:property PublishAot=false

using SkiaSharp;

const double Left = 89, Right = 1162, Top = 77.5, Bottom = 1154, N = 4.6, Inset = 1.0;   // the outline is kept inside by a pixel
const double Fill = 0.94;                                                                // the tile fills this much of the icon
const int Canvas = 512;
int[] sizes = [16, 24, 32, 48, 64, 128, 256];

var dir = args.Length > 0 ? args[0] : Path.Combine("assets", "icon");
using var source = SKBitmap.Decode(Path.Combine(dir, "source.webp")) ?? throw new InvalidOperationException("Cannot read source.webp");

// 1. The artwork with an alpha channel: the signed distance (in pixels) to the squircle gives an antialiased edge.
double cx = (Left + Right) / 2, cy = (Top + Bottom) / 2, a = (Right - Left) / 2 - Inset, b = (Bottom - Top) / 2 - Inset;
using var full = new SKBitmap(new SKImageInfo(source.Width, source.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul));
for (var y = 0; y < source.Height; y++)
    for (var x = 0; x < source.Width; x++)
    {
        double u = Math.Abs((x + 0.5 - cx) / a), v = Math.Abs((y + 0.5 - cy) / b);
        double sum = Math.Pow(u, N) + Math.Pow(v, N);
        double g = Math.Pow(sum, 1 / N);                                                  // 1 on the outline
        // gradient of g with respect to pixels, to turn (g - 1) into a distance
        double gx = Math.Pow(u, N - 1) / Math.Pow(sum, (N - 1) / N) / a, gy = Math.Pow(v, N - 1) / Math.Pow(sum, (N - 1) / N) / b;
        double dist = (g - 1) / Math.Max(Math.Sqrt(gx * gx + gy * gy), 1e-9);              // negative inside
        var alpha = Math.Clamp(0.5 - dist, 0, 1);
        var c = source.GetPixel(x, y);
        full.SetPixel(x, y, new SKColor(c.Red, c.Green, c.Blue, (byte)Math.Round(alpha * 255)));
    }

// 2. The 512x512 master: a square around the tile, so the tile fills most of the icon.
var side = (float)((Right - Left) / Fill);
var crop = new SKRect((float)cx - side / 2, (float)cy - side / 2, (float)cx + side / 2, (float)cy + side / 2);
using var fullImage = SKImage.FromBitmap(full);
using var masterSurface = SKSurface.Create(new SKImageInfo(Canvas, Canvas, SKColorType.Rgba8888, SKAlphaType.Premul));
masterSurface.Canvas.Clear(SKColors.Transparent);
masterSurface.Canvas.DrawImage(fullImage, crop, new SKRect(0, 0, Canvas, Canvas), new SKSamplingOptions(SKCubicResampler.Mitchell), paint: null);
using var masterImage = masterSurface.Snapshot();
File.WriteAllBytes(Path.Combine(dir, "icon.png"), masterImage.Encode(SKEncodedImageFormat.Png, 100).ToArray());

// 3. One image per size. Sizes below 256 are stored as classic 32-bit bitmaps (understood by every Windows tool),
//    256 as PNG, which is the usual convention for Windows icons.
var entries = new List<(int Size, byte[] Data)>();
foreach (var size in sizes)
{
    using var surface = SKSurface.Create(new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Premul));
    surface.Canvas.Clear(SKColors.Transparent);
    surface.Canvas.DrawImage(masterImage, new SKRect(0, 0, size, size), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear), paint: null);
    using var scaled = surface.Snapshot();

    if (size == 256) { entries.Add((size, scaled.Encode(SKEncodedImageFormat.Png, 100).ToArray())); continue; }

    // un-premultiplied BGRA, top-down
    using var bgra = new SKBitmap(new SKImageInfo(size, size, SKColorType.Bgra8888, SKAlphaType.Unpremul));
    scaled.ReadPixels(bgra.Info, bgra.GetPixels(), bgra.RowBytes, 0, 0);
    var pixels = bgra.Bytes;

    var maskRow = (size + 31) / 32 * 4;                       // the AND mask: 1 bit per pixel, rows padded to 4 bytes (all zero: alpha is used)
    using var ms = new MemoryStream();
    using var w = new BinaryWriter(ms);
    w.Write(40); w.Write(size); w.Write(size * 2); w.Write((short)1); w.Write((short)32);   // BITMAPINFOHEADER: height counts image + mask
    w.Write(0); w.Write(size * size * 4 + maskRow * size); w.Write(0); w.Write(0); w.Write(0); w.Write(0);
    for (var y = size - 1; y >= 0; y--) w.Write(pixels, y * size * 4, size * 4);              // bitmap rows are stored bottom-up
    w.Write(new byte[maskRow * size]);
    entries.Add((size, ms.ToArray()));
}

// 4. The ICO container: header, one directory entry per image, then the image data.
using var ico = new MemoryStream();
using var iw = new BinaryWriter(ico);
iw.Write((short)0); iw.Write((short)1); iw.Write((short)entries.Count);
var offset = 6 + 16 * entries.Count;
foreach (var (size, data) in entries)
{
    iw.Write((byte)(size == 256 ? 0 : size)); iw.Write((byte)(size == 256 ? 0 : size));     // 0 means 256
    iw.Write((byte)0); iw.Write((byte)0); iw.Write((short)1); iw.Write((short)32);
    iw.Write(data.Length); iw.Write(offset);
    offset += data.Length;
}
foreach (var (_, data) in entries) iw.Write(data);
File.WriteAllBytes(Path.Combine(dir, "AppIcon.ico"), ico.ToArray());

Console.WriteLine($"icon.png 512x512 and AppIcon.ico with sizes {string.Join(", ", sizes)} written to {dir} ({ico.Length / 1024} KB).");

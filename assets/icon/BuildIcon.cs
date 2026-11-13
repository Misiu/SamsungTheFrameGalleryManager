// Builds the application icon from the artwork. Run it from the repository root:
//
//     dotnet run assets/icon/BuildIcon.cs
//
// Input : assets/icon/source.png   the original artwork (512x512, opaque, on a near-white background)
// Output: assets/icon/icon.png     the same artwork with a transparent background (512x512), used by the README
//         assets/icon/AppIcon.ico  the Windows icon with the sizes 16, 24, 32, 48, 64, 128 and 256
//
// The source has no alpha channel, so the transparency is rebuilt from the tile's geometry. The tile is a rounded
// rectangle whose outline was measured on the artwork: left 16, right 480, top 39, bottom 487, corner radius 96
// (all in source pixels). Everything outside it (the white background and the drop shadow) becomes transparent.
// If the artwork changes, re-measure these values.
#:package SkiaSharp@4.156.0
#:property PublishAot=false

using System.Buffers.Binary;
using SkiaSharp;

const float Left = 15.5f, Top = 38.5f, Right = 480.5f, Bottom = 487.5f, Radius = 96.5f;   // half a pixel outside the outline, so it stays inside
const int Canvas = 512;
int[] sizes = [16, 24, 32, 48, 64, 128, 256];

var dir = args.Length > 0 ? args[0] : Path.Combine("assets", "icon");
using var source = SKBitmap.Decode(Path.Combine(dir, "source.png")) ?? throw new InvalidOperationException("Cannot read source.png");
if (source.Width != Canvas || source.Height != Canvas) throw new InvalidOperationException($"source.png must be {Canvas}x{Canvas}.");

// 1. Transparent master: the tile is centred on the canvas with whole-pixel shifts, so the artwork is not resampled.
var shiftX = (int)MathF.Round(Canvas / 2f - (Left + Right) / 2f);
var shiftY = (int)MathF.Round(Canvas / 2f - (Top + Bottom) / 2f);
float cx = (Left + Right) / 2f, cy = (Top + Bottom) / 2f, hx = (Right - Left) / 2f, hy = (Bottom - Top) / 2f;

using var master = new SKBitmap(new SKImageInfo(Canvas, Canvas, SKColorType.Rgba8888, SKAlphaType.Unpremul));
for (var y = 0; y < Canvas; y++)
    for (var x = 0; x < Canvas; x++)
    {
        int sx = x - shiftX, sy = y - shiftY;
        if (sx < 0 || sy < 0 || sx >= Canvas || sy >= Canvas) { master.SetPixel(x, y, SKColors.Transparent); continue; }

        // signed distance (in pixels) from the pixel centre to the rounded rectangle; negative inside
        float qx = MathF.Abs(sx + 0.5f - cx) - (hx - Radius), qy = MathF.Abs(sy + 0.5f - cy) - (hy - Radius);
        var sd = MathF.Sqrt(MathF.Max(qx, 0) * MathF.Max(qx, 0) + MathF.Max(qy, 0) * MathF.Max(qy, 0)) + MathF.Min(MathF.Max(qx, qy), 0) - Radius;
        var alpha = Math.Clamp(0.5f - sd, 0f, 1f);

        var c = source.GetPixel(sx, sy);
        master.SetPixel(x, y, new SKColor(c.Red, c.Green, c.Blue, (byte)MathF.Round(alpha * 255)));
    }

using var masterImage = SKImage.FromBitmap(master);
File.WriteAllBytes(Path.Combine(dir, "icon.png"), masterImage.Encode(SKEncodedImageFormat.Png, 100).ToArray());

// 2. One image per size. Sizes below 256 are stored as classic 32-bit bitmaps (understood by every Windows tool),
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

// 3. The ICO container: header, one directory entry per image, then the image data.
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

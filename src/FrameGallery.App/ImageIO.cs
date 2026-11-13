using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using FrameGallery.Core;

namespace FrameGallery.App;

static class ImageIO
{
    /// <summary>Wczytuje obraz niezależnie od pliku (bez blokady) i uwzględnia orientację EXIF.</summary>
    public static Bitmap Load(string path)
    {
        using var fs = File.OpenRead(path);
        using var src = Image.FromStream(fs, useEmbeddedColorManagement: false, validateImageData: true);
        if (src.PropertyIdList.Contains(0x0112))
        {
            var rotate = BitConverter.ToUInt16(src.GetPropertyItem(0x0112)!.Value!, 0) switch
            {
                2 => RotateFlipType.RotateNoneFlipX,
                3 => RotateFlipType.Rotate180FlipNone,
                4 => RotateFlipType.Rotate180FlipX,
                5 => RotateFlipType.Rotate90FlipX,
                6 => RotateFlipType.Rotate90FlipNone,
                7 => RotateFlipType.Rotate270FlipX,
                8 => RotateFlipType.Rotate270FlipNone,
                _ => RotateFlipType.RotateNoneFlipNone,
            };
            src.RotateFlip(rotate);
        }
        return new Bitmap(src);
    }

    /// <summary>Wycina kadr, skaluje do dokładnie w×h i koduje jako JPEG (jakość 92).</summary>
    public static byte[] CropToJpeg(Bitmap src, CropRect crop, int w, int h)
    {
        using var dst = new Bitmap(w, h, PixelFormat.Format24bppRgb);
        using (var g = Graphics.FromImage(dst))
        {
            g.Clear(Color.Black);   // przezroczyste PNG-i dostają czarne tło
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.CompositingQuality = CompositingQuality.HighQuality;
            using var attrs = new ImageAttributes();
            attrs.SetWrapMode(WrapMode.TileFlipXY);   // bez jasnej obwódki na krawędziach
            g.DrawImage(src, new Rectangle(0, 0, w, h), (float)crop.X, (float)crop.Y, (float)crop.W, (float)crop.H, GraphicsUnit.Pixel, attrs);
        }
        var jpeg = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
        using var ep = new EncoderParameters(1);
        ep.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 92L);
        using var ms = new MemoryStream();
        dst.Save(ms, jpeg, ep);
        return ms.ToArray();
    }

    /// <summary>Miniatura w ramce w×h z paskami (bez przycinania).</summary>
    public static Bitmap Letterbox(Image src, int w, int h)
    {
        var bmp = new Bitmap(w, h);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.FromArgb(40, 40, 40));
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        var s = Math.Min((double)w / src.Width, (double)h / src.Height);
        var dw = (int)(src.Width * s); var dh = (int)(src.Height * s);
        g.DrawImage(src, (w - dw) / 2, (h - dh) / 2, dw, dh);
        return bmp;
    }
}

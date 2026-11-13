using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using FrameGallery.Core;

namespace FrameGallery.Desktop.Controls;

/// <summary>
/// A preview of the photo with a crop frame of a fixed aspect ratio. Drag to move the frame, use the mouse wheel to resize it,
/// click outside it to centre it there. The frame is kept in pixels of the original image, so the preview can be smaller.
/// </summary>
public sealed class CropCanvas : Control
{
    static readonly IBrush Dim = new SolidColorBrush(Color.FromArgb(150, 0, 0, 0));
    static readonly IPen Frame = new Pen(Brushes.White, 2);
    static readonly IPen Thirds = new Pen(new SolidColorBrush(Color.FromArgb(90, 255, 255, 255)), 1);

    Bitmap? _preview;
    double _imageWidth, _imageHeight, _aspect;
    CropRect _crop;
    bool _dragging;
    Point _last;

    public CropRect Crop => _crop;
    public event EventHandler? CropChanged;

    public CropCanvas()
    {
        Cursor = new Cursor(StandardCursorType.SizeAll);
        ClipToBounds = true;
    }

    /// <param name="preview">What is drawn (it may be a scaled-down copy).</param>
    /// <param name="imageWidth">Width of the original image in pixels; the frame is expressed in these.</param>
    public void SetImage(Bitmap preview, int imageWidth, int imageHeight, double aspect)
    {
        _preview = preview;
        _imageWidth = imageWidth;
        _imageHeight = imageHeight;
        _aspect = aspect;
        _crop = CropMath.Initial(imageWidth, imageHeight, aspect);
        Changed();
    }

    (double Scale, double X, double Y) Fit()
    {
        var s = Math.Min(Bounds.Width / _imageWidth, Bounds.Height / _imageHeight);
        return (s, (Bounds.Width - _imageWidth * s) / 2, (Bounds.Height - _imageHeight * s) / 2);
    }

    void Changed()
    {
        InvalidateVisual();
        CropChanged?.Invoke(this, EventArgs.Empty);
    }

    public override void Render(DrawingContext context)
    {
        context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));   // so the whole area receives pointer events
        if (_preview is null) return;

        var (s, ox, oy) = Fit();
        var full = new Rect(ox, oy, _imageWidth * s, _imageHeight * s);
        context.DrawImage(_preview, new Rect(_preview.Size), full);

        var r = new Rect(ox + _crop.X * s, oy + _crop.Y * s, _crop.W * s, _crop.H * s);
        context.FillRectangle(Dim, new Rect(full.Left, full.Top, full.Width, r.Top - full.Top));
        context.FillRectangle(Dim, new Rect(full.Left, r.Bottom, full.Width, full.Bottom - r.Bottom));
        context.FillRectangle(Dim, new Rect(full.Left, r.Top, r.Left - full.Left, r.Height));
        context.FillRectangle(Dim, new Rect(r.Right, r.Top, full.Right - r.Right, r.Height));

        for (var i = 1; i <= 2; i++)   // rule-of-thirds guides
        {
            context.DrawLine(Thirds, new Point(r.Left + r.Width * i / 3, r.Top), new Point(r.Left + r.Width * i / 3, r.Bottom));
            context.DrawLine(Thirds, new Point(r.Left, r.Top + r.Height * i / 3), new Point(r.Right, r.Top + r.Height * i / 3));
        }
        context.DrawRectangle(Frame, r);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (_preview is null || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

        var p = e.GetPosition(this);
        var (s, ox, oy) = Fit();
        double ix = (p.X - ox) / s, iy = (p.Y - oy) / s;
        var inside = ix >= _crop.X && ix <= _crop.X + _crop.W && iy >= _crop.Y && iy <= _crop.Y + _crop.H;
        if (!inside)   // a click outside the crop centres it there
        {
            _crop = CropMath.Move(_crop, ix - (_crop.X + _crop.W / 2), iy - (_crop.Y + _crop.H / 2), _imageWidth, _imageHeight);
            Changed();
        }
        _dragging = true;
        _last = p;
        e.Pointer.Capture(this);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!_dragging || _preview is null) return;

        var p = e.GetPosition(this);
        var (s, _, _) = Fit();
        _crop = CropMath.Move(_crop, (p.X - _last.X) / s, (p.Y - _last.Y) / s, _imageWidth, _imageHeight);
        _last = p;
        Changed();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _dragging = false;
        e.Pointer.Capture(null);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (_preview is null) return;
        _crop = CropMath.Scale(_crop, e.Delta.Y > 0 ? 0.93 : 1 / 0.93, _imageWidth, _imageHeight, _aspect);
        Changed();
    }
}

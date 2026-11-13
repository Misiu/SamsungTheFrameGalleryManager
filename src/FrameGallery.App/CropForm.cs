using FrameGallery.Core;
using FrameGallery.Localization;

namespace FrameGallery.App;

/// <summary>A crop dialog fixed to the TV screen's aspect ratio. The result (a JPEG at the target resolution) is in <see cref="Result"/>.</summary>
sealed class CropForm : Form
{
    readonly Bitmap _img;
    readonly int _tw, _th;
    readonly CropBox _box = new() { Dock = DockStyle.Fill };
    readonly Label _info = new() { AutoSize = true, Anchor = AnchorStyles.Left, Padding = new Padding(0, 8, 0, 0) };

    public byte[] Result { get; private set; } = [];

    public CropForm(Bitmap img, int targetW, int targetH, string fileName)
    {
        _img = img; _tw = targetW; _th = targetH;
        Text = Loc.Get("Crop_Title", fileName);
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(1000, 700);
        MinimumSize = new Size(560, 420);

        var ok = new Button { Text = Loc.Get("Crop_Use"), AutoSize = true, DialogResult = DialogResult.None };
        var skip = new Button { Text = Loc.Get("Crop_Skip"), AutoSize = true, DialogResult = DialogResult.Cancel };
        ok.Click += (_, _) => Accept();

        var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 44, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8) };
        bar.Controls.AddRange([skip, ok]);
        var hint = new Label { Dock = DockStyle.Bottom, Height = 28, Padding = new Padding(8, 6, 0, 0), Text = Loc.Get("Crop_Hint") };
        var top = new Panel { Dock = DockStyle.Bottom, Height = 30, Padding = new Padding(8, 0, 0, 0) };
        top.Controls.Add(_info);

        Controls.Add(_box);
        Controls.Add(top);
        Controls.Add(hint);
        Controls.Add(bar);
        AcceptButton = ok; CancelButton = skip;

        _box.CropChanged += (_, _) => UpdateInfo();
        _box.SetImage(img, (double)targetW / targetH);
    }

    void UpdateInfo()
    {
        var c = _box.Crop;
        var low = c.W < _tw * 0.6;
        _info.Text = Loc.Get("Crop_Info", c.W, c.H, _tw, _th) + (low ? Loc.Get("Crop_Low") : "");
        _info.ForeColor = low ? Color.DarkOrange : SystemColors.ControlText;
    }

    void Accept()
    {
        using var _ = new WaitCursor(this);
        Result = ImageIO.CropToJpeg(_img, _box.Crop, _tw, _th);
        DialogResult = DialogResult.OK;
    }

    sealed class WaitCursor : IDisposable
    {
        readonly Form _f;
        public WaitCursor(Form f) { _f = f; f.Cursor = Cursors.WaitCursor; }
        public void Dispose() => _f.Cursor = Cursors.Default;
    }
}

/// <summary>A preview of the image with a crop frame of a fixed aspect ratio.</summary>
sealed class CropBox : Control
{
    Bitmap? _img;
    double _aspect;
    CropRect _crop;
    bool _drag;
    Point _last;

    public CropRect Crop => _crop;
    public event EventHandler? CropChanged;

    public CropBox()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.ResizeRedraw, true);
        BackColor = Color.FromArgb(28, 28, 28);
        Cursor = Cursors.SizeAll;
    }

    public void SetImage(Bitmap img, double aspect)
    {
        _img = img; _aspect = aspect;
        _crop = CropMath.Initial(img.Width, img.Height, aspect);
        Changed();
    }

    (double Scale, double X, double Y) Fit()
    {
        var s = Math.Min((double)ClientSize.Width / _img!.Width, (double)ClientSize.Height / _img.Height);
        return (s, (ClientSize.Width - _img.Width * s) / 2, (ClientSize.Height - _img.Height * s) / 2);
    }

    void Changed() { Invalidate(); CropChanged?.Invoke(this, EventArgs.Empty); }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (_img is null) return;
        var (s, ox, oy) = Fit();
        var g = e.Graphics;
        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBilinear;
        var full = new RectangleF((float)ox, (float)oy, (float)(_img.Width * s), (float)(_img.Height * s));
        g.DrawImage(_img, full);

        var r = new RectangleF((float)(ox + _crop.X * s), (float)(oy + _crop.Y * s), (float)(_crop.W * s), (float)(_crop.H * s));
        using var dim = new SolidBrush(Color.FromArgb(150, 0, 0, 0));
        g.FillRectangle(dim, full.Left, full.Top, full.Width, r.Top - full.Top);
        g.FillRectangle(dim, full.Left, r.Bottom, full.Width, full.Bottom - r.Bottom);
        g.FillRectangle(dim, full.Left, r.Top, r.Left - full.Left, r.Height);
        g.FillRectangle(dim, r.Right, r.Top, full.Right - r.Right, r.Height);
        using var pen = new Pen(Color.White, 2);
        g.DrawRectangle(pen, r.X, r.Y, r.Width, r.Height);
    }

    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); Focus(); }   // so the mouse wheel works without clicking first

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (_img is null || e.Button != MouseButtons.Left) return;
        var (s, ox, oy) = Fit();
        var ix = (e.X - ox) / s; var iy = (e.Y - oy) / s;
        var inside = ix >= _crop.X && ix <= _crop.X + _crop.W && iy >= _crop.Y && iy <= _crop.Y + _crop.H;
        if (!inside)   // a click outside the crop centres it there
        {
            _crop = CropMath.Move(_crop, ix - (_crop.X + _crop.W / 2), iy - (_crop.Y + _crop.H / 2), _img.Width, _img.Height);
            Changed();
        }
        _drag = true; _last = e.Location; Capture = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_drag || _img is null) return;
        var (s, _, _) = Fit();
        _crop = CropMath.Move(_crop, (e.X - _last.X) / s, (e.Y - _last.Y) / s, _img.Width, _img.Height);
        _last = e.Location;
        Changed();
    }

    protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); _drag = false; Capture = false; }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        if (_img is null) return;
        _crop = CropMath.Scale(_crop, e.Delta > 0 ? 0.93 : 1 / 0.93, _img.Width, _img.Height, _aspect);
        Changed();
    }
}

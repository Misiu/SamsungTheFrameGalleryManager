using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace FrameGallery.Desktop.Controls;

/// <summary>
/// An indeterminate progress ring, drawn by hand: a faint track and an arc that keeps turning. The arc grows by moving its head
/// forward and shrinks by moving its tail forward, so neither end ever moves backwards. This is the "advance" behaviour of Material
/// Design's circular indeterminate indicator (see CircularIndeterminateAdvanceAnimatorDelegate in material-components-android, Apache-2.0);
/// the timing and the angles below follow it. There is no ready-made spinner in Avalonia (its Spinner is only the base class of
/// ButtonSpinner, the arrows of a numeric up/down), and its ProgressBar is a bar.
/// While attached it asks for a frame each time but only redraws when it is actually visible. Size it with Width and Height.
/// </summary>
public sealed class Spinner : Control
{
    const double CycleSeconds = 1.35;          // one cycle: the arc grows, then shrinks
    const double AdvancePerCycle = 250;        // degrees: how far the head (while growing) and then the tail (while shrinking) move per cycle
    const double TurnDegreesPerSecond = 1520 / 5.4;   // the constant turning of the whole ring

    readonly Stopwatch _clock = Stopwatch.StartNew();
    bool _attached;

    /// <summary>
    /// Positions of the arc's tail and head in degrees, measured clockwise from the cycle's own reference and not counting the constant
    /// turning. Both are non-decreasing in time; the head leads the tail by 0 to <see cref="AdvancePerCycle"/> degrees.
    /// </summary>
    internal static (double Tail, double Head) Positions(double seconds)
    {
        var cycle = Math.Floor(seconds / CycleSeconds);
        var phase = seconds / CycleSeconds - cycle;                 // 0..1 within the cycle
        var origin = cycle * AdvancePerCycle;                       // each cycle starts where the previous one ended

        return phase < 0.5
            ? (origin, origin + AdvancePerCycle * FastOutSlowIn(phase * 2))                       // growing: the head moves, the tail waits
            : (origin + AdvancePerCycle * FastOutSlowIn(phase * 2 - 1), origin + AdvancePerCycle); // shrinking: the tail catches up
    }

    /// <summary>The cubic Bézier (0.4, 0, 0.2, 1) of Material's "fast out, slow in" easing.</summary>
    internal static double FastOutSlowIn(double x)
    {
        const double x1 = 0.4, y1 = 0, x2 = 0.2, y2 = 1;
        double lo = 0, hi = 1, t = x;
        for (var i = 0; i < 24; i++)   // find the curve parameter whose x equals the requested one
        {
            t = (lo + hi) / 2;
            var bx = 3 * (1 - t) * (1 - t) * t * x1 + 3 * (1 - t) * t * t * x2 + t * t * t;
            if (bx < x) lo = t; else hi = t;
        }
        return 3 * (1 - t) * (1 - t) * t * y1 + 3 * (1 - t) * t * t * y2 + t * t * t;
    }

    protected override Size MeasureOverride(Size availableSize) => new(
        double.IsInfinity(availableSize.Width) ? 40 : Math.Min(availableSize.Width, 40),
        double.IsInfinity(availableSize.Height) ? 40 : Math.Min(availableSize.Height, 40));

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _attached = true;
        RequestFrame();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _attached = false;
    }

    void RequestFrame()
    {
        if (!_attached || TopLevel.GetTopLevel(this) is not { } top) return;
        top.RequestAnimationFrame(_ =>
        {
            if (!_attached) return;
            if (IsEffectivelyVisible) InvalidateVisual();
            RequestFrame();
        });
    }

    public override void Render(DrawingContext context)
    {
        var size = Math.Min(Bounds.Width, Bounds.Height);
        if (size <= 0) return;

        var thickness = Math.Clamp(size / 10, 3, 6);
        var radius = (size - thickness) / 2;
        var centre = new Point(Bounds.Width / 2, Bounds.Height / 2);

        var accent = Color.Parse("#2F6BFF");
        if (this.TryGetResource("AccentBrush", ActualThemeVariant, out var found) && found is ISolidColorBrush solid) accent = solid.Color;

        // the faint track
        context.DrawEllipse(null, new Pen(new SolidColorBrush(accent, 0.18), thickness), centre, radius, radius);

        var t = _clock.Elapsed.TotalSeconds;
        var (tail, head) = Positions(t);
        var rotation = t * TurnDegreesPerSecond - 90;                // 0 degrees is the top
        var start = rotation + tail;
        var sweep = head - tail;

        var brush = new SolidColorBrush(accent);
        if (sweep < 1)   // the arc has all but vanished: a dot, as the round cap would draw
        {
            context.DrawEllipse(brush, null, PointOnCircle(centre, radius, start), thickness / 2, thickness / 2);
            return;
        }

        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            g.BeginFigure(PointOnCircle(centre, radius, start), false);
            g.ArcTo(PointOnCircle(centre, radius, start + sweep), new Size(radius, radius), 0, sweep > 180, SweepDirection.Clockwise);
            g.EndFigure(false);
        }
        context.DrawGeometry(null, new Pen(brush, thickness, lineCap: PenLineCap.Round), geometry);
    }

    static Point PointOnCircle(Point centre, double radius, double degrees)
    {
        var a = degrees * Math.PI / 180;
        return new Point(centre.X + radius * Math.Cos(a), centre.Y + radius * Math.Sin(a));
    }
}

using System.Windows;
using System.Windows.Media;

namespace GameLoopBooster.App.Controls;

/// <summary>Circular progress ring with a gradient arc.</summary>
public sealed class ScoreRing : FrameworkElement
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(ScoreRing),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public double Thickness { get; set; } = 12;

    protected override void OnRender(DrawingContext dc)
    {
        var size = Math.Min(ActualWidth, ActualHeight);
        if (size <= Thickness) return;
        var r = (size - Thickness) / 2;
        var c = new Point(ActualWidth / 2, ActualHeight / 2);

        var track = new Pen(new SolidColorBrush(Color.FromRgb(0x1E, 0x23, 0x38)), Thickness);
        dc.DrawEllipse(null, track, c, r, r);

        var pct = Math.Clamp(Value, 0, 100) / 100.0;
        if (pct <= 0) return;
        var brush = new LinearGradientBrush(Color.FromRgb(0x00, 0xE5, 0xFF), Color.FromRgb(0x8B, 0x5C, 0xF6), 45);
        var pen = new Pen(brush, Thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };

        if (pct >= 0.999)
        {
            dc.DrawEllipse(null, pen, c, r, r);
            return;
        }
        var angle = pct * 360;
        var start = new Point(c.X, c.Y - r);
        var rad = (angle - 90) * Math.PI / 180;
        var end = new Point(c.X + r * Math.Cos(rad), c.Y + r * Math.Sin(rad));
        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            ctx.BeginFigure(start, false, false);
            ctx.ArcTo(end, new Size(r, r), 0, angle > 180, SweepDirection.Clockwise, true, false);
        }
        geo.Freeze();
        dc.DrawGeometry(null, pen, geo);
    }
}

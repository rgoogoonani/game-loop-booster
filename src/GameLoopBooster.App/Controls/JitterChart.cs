using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace GameLoopBooster.App.Controls;

/// <summary>Bar chart of timer wake-up jitter; bars over 1 ms / 4 ms turn amber / red.</summary>
public sealed class JitterChart : FrameworkElement
{
    public static readonly DependencyProperty ValuesProperty = DependencyProperty.Register(
        nameof(Values), typeof(double[]), typeof(JitterChart),
        new FrameworkPropertyMetadata(Array.Empty<double>(), FrameworkPropertyMetadataOptions.AffectsRender));

    public double[] Values
    {
        get => (double[])GetValue(ValuesProperty);
        set => SetValue(ValuesProperty, value);
    }

    private static readonly Brush Ok = Frozen("#00E5FF");
    private static readonly Brush Warn = Frozen("#FFB020");
    private static readonly Brush Bad = Frozen("#FF4D6A");
    private static readonly Pen Grid = new Pen(Frozen("#262C45"), 1) { DashStyle = DashStyles.Dash };
    private static readonly Brush LabelBrush = Frozen("#8B91B3");

    private static Brush Frozen(string hex)
    {
        var b = (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
        b.Freeze();
        return b;
    }

    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth;
        var h = ActualHeight;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h));
        var values = Values ?? Array.Empty<double>();

        var max = Math.Max(2.5, values.Length == 0 ? 0 : values.Max() * 1.1);
        foreach (var line in new[] { 1.0, 2.0, 4.0, 10.0, 20.0 }.Where(l => l < max))
        {
            var y = h - line / max * h;
            dc.DrawLine(Grid, new Point(0, y), new Point(w, y));
            var text = new FormattedText($"{line} ms", CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface("Segoe UI"), 10, LabelBrush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
            dc.DrawText(text, new Point(4, y - 13));
        }

        if (values.Length == 0) return;
        var barW = Math.Max(1, w / values.Length);
        for (var i = 0; i < values.Length; i++)
        {
            var v = values[i];
            var bh = Math.Max(1, v / max * h);
            var brush = v > 4.0 ? Bad : v > 1.0 ? Warn : Ok;
            dc.DrawRectangle(brush, null, new Rect(i * w / values.Length, h - bh, Math.Max(1, barW - 0.5), bh));
        }
    }
}

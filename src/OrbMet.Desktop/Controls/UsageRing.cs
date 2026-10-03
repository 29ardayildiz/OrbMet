using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace OrbMet.Desktop.Controls;

/// <summary>
/// The usage ring from design.md: a filled arc clockwise from 12 o'clock, a
/// neutral track when the value is missing, and threshold colours that never
/// carry the meaning alone. The arc caps at a full circle while the text keeps
/// the provider's raw value, so a reported 130% is not rewritten as 100%.
/// </summary>
public sealed class UsageRing : FrameworkElement
{
    private const double Thickness = 3.0;

    private static readonly Brush TrackBrush = Freeze(Color.FromRgb(0x41, 0x49, 0x57));
    private static readonly Brush NormalBrush = Freeze(Color.FromRgb(0x55, 0xD6, 0xA0));
    private static readonly Brush CautionBrush = Freeze(Color.FromRgb(0xF3, 0xC5, 0x6B));
    private static readonly Brush HighBrush = Freeze(Color.FromRgb(0xF5, 0x82, 0x82));
    private static readonly Brush BlockedBrush = Freeze(Color.FromRgb(0xE8, 0x5B, 0x6B));
    private static readonly Brush GlyphBrush = Freeze(Color.FromRgb(0xF4, 0xF6, 0xFA));

    public static readonly DependencyProperty PercentProperty = DependencyProperty.Register(
        nameof(Percent),
        typeof(double?),
        typeof(UsageRing),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty IsExhaustedProperty = DependencyProperty.Register(
        nameof(IsExhausted),
        typeof(bool),
        typeof(UsageRing),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty GlyphProperty = DependencyProperty.Register(
        nameof(Glyph),
        typeof(string),
        typeof(UsageRing),
        new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Provider-reported used percentage, or null when it is missing.</summary>
    public double? Percent
    {
        get => (double?)GetValue(PercentProperty);
        set => SetValue(PercentProperty, value);
    }

    /// <summary>True only when the provider explicitly reports being blocked.</summary>
    public bool IsExhausted
    {
        get => (bool)GetValue(IsExhaustedProperty);
        set => SetValue(IsExhaustedProperty, value);
    }

    /// <summary>A short letter shown in the centre until real icons exist.</summary>
    public string Glyph
    {
        get => (string)GetValue(GlyphProperty);
        set => SetValue(GlyphProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        var size = Math.Min(ActualWidth, ActualHeight);
        var radius = (size - Thickness) / 2;
        if (radius <= 0)
        {
            return;
        }

        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        drawingContext.DrawEllipse(null, new Pen(TrackBrush, Thickness), center, radius, radius);

        if (Percent is { } percent && percent > 0)
        {
            var fraction = Math.Min(percent, 100) / 100.0;
            var pen = new Pen(BrushFor(percent), Thickness)
            {
                StartLineCap = PenLineCap.Flat,
                EndLineCap = PenLineCap.Flat,
            };

            if (fraction >= 0.999)
            {
                drawingContext.DrawEllipse(null, pen, center, radius, radius);
            }
            else
            {
                drawingContext.DrawGeometry(null, pen, BuildArc(center, radius, fraction));
            }
        }

        DrawGlyph(drawingContext, center);
    }

    private Brush BrushFor(double percent)
    {
        if (IsExhausted)
        {
            return BlockedBrush;
        }

        if (percent >= 75)
        {
            return HighBrush;
        }

        return percent >= 50 ? CautionBrush : NormalBrush;
    }

    private void DrawGlyph(DrawingContext drawingContext, Point center)
    {
        if (string.IsNullOrEmpty(Glyph))
        {
            return;
        }

        var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var text = new FormattedText(
            Glyph,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface("Segoe UI"),
            11,
            GlyphBrush,
            pixelsPerDip);

        drawingContext.DrawText(
            text,
            new Point(center.X - text.Width / 2, center.Y - text.Height / 2));
    }

    private static Geometry BuildArc(Point center, double radius, double fraction)
    {
        const double startDegrees = -90;
        var sweep = 360.0 * fraction;

        var start = PointOnCircle(center, radius, startDegrees);
        var end = PointOnCircle(center, radius, startDegrees + sweep);

        var figure = new PathFigure { StartPoint = start, IsClosed = false, IsFilled = false };
        figure.Segments.Add(new ArcSegment(
            end,
            new Size(radius, radius),
            0,
            isLargeArc: sweep > 180,
            SweepDirection.Clockwise,
            isStroked: true));

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        geometry.Freeze();
        return geometry;
    }

    private static Point PointOnCircle(Point center, double radius, double degrees)
    {
        var radians = degrees * Math.PI / 180.0;
        return new Point(center.X + radius * Math.Cos(radians), center.Y + radius * Math.Sin(radians));
    }

    private static Brush Freeze(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}

using System.Windows;
using System.Windows.Media;
using OrbMet.Desktop.Infrastructure;

namespace OrbMet.Desktop.Controls;

/// <summary>
/// The detail card's outline (design.md 7.1): a rounded body and a tail
/// pointing at the strip, unioned into one shape so the two can never drift
/// apart. The tail is kept clear of the body's corners so it always leaves a
/// straight edge. Adapted from Pulse's UsageBubbleShape (Apache 2.0, see
/// THIRD_PARTY_NOTICES.md).
/// </summary>
internal static class DetailBubble
{
    // Bezier handles of each tail flank, as shares of its reach and half-height.
    private const double NearAlong = 0.24;
    private const double NearAcross = 0.44;
    private const double FarAlong = 0.55;
    private const double FarAcross = 0.24;

    /// <param name="pointerCenter">Where the tail's tip sits, measured from the card's top.</param>
    /// <param name="tailOnRight">True when the strip is to the card's right.</param>
    public static Geometry Build(double width, double height, double pointerCenter, bool tailOnRight)
    {
        var tailWidth = PanelMetrics.CardTailWidth;
        var radius = PanelMetrics.CardCornerRadius;
        var half = PanelMetrics.CardTailHeight / 2;

        var bodyWidth = Math.Max(width - tailWidth, 0);
        var body = new Rect(tailOnRight ? 0 : tailWidth, 0, bodyWidth, height);
        var bodyGeometry = new RectangleGeometry(body, radius, radius);

        var low = radius + half;
        var high = Math.Max(height - radius - half, low);
        var centre = Math.Clamp(pointerCenter, low, high);

        var baseX = tailOnRight ? body.Right : body.Left;
        var tipX = tailOnRight ? width : 0;
        var reach = tipX - baseX;
        var sweep = half;

        var tail = new PathFigure { StartPoint = new Point(baseX, centre - sweep), IsClosed = true, IsFilled = true };
        tail.Segments.Add(new BezierSegment(
            new Point(baseX + reach * NearAlong, centre - sweep * NearAcross),
            new Point(baseX + reach * FarAlong, centre - sweep * FarAcross),
            new Point(tipX, centre),
            true));
        tail.Segments.Add(new BezierSegment(
            new Point(baseX + reach * FarAlong, centre + sweep * FarAcross),
            new Point(baseX + reach * NearAlong, centre + sweep * NearAcross),
            new Point(baseX, centre + sweep),
            true));

        // Overlap the body a little so the union has no seam.
        tail.Segments.Add(new LineSegment(new Point(baseX - reach * 0.08, centre + sweep), true));
        tail.Segments.Add(new LineSegment(new Point(baseX - reach * 0.08, centre - sweep), true));

        var geometry = Geometry.Combine(bodyGeometry, new PathGeometry(new[] { tail }), GeometryCombineMode.Union, null);
        geometry.Freeze();
        return geometry;
    }
}

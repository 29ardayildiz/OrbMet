using System.Windows;
using System.Windows.Media;
using OrbMet.Desktop.Infrastructure;

namespace OrbMet.Desktop.Controls;

/// <summary>
/// The outline of the strip docked against a screen edge (design.md 4.2).
/// The body has soft superellipse corners on the side facing the screen, and
/// at each end a concave flare sweeps it into the edge it is docked to, so the
/// strip reads as growing out of the edge rather than floating beside it.
/// Built facing right (docked to the right edge) and mirrored for the left.
/// Adapted from Pulse's DockBerthShape (Apache 2.0, see THIRD_PARTY_NOTICES.md).
/// </summary>
internal static class DockBerth
{
    /// <summary>Exponent of the corner curve; 4 is a squircle, softer than a circle's 2.</summary>
    private const double SquircleExponent = 4;
    private const int CornerSamples = 48;

    /// <summary>Bezier handle length for the flare, as a share of its size.</summary>
    private const double FlareHandle = 0.55;

    public static Geometry Build(double width, double height, bool dockedRight = true)
    {
        var w = width;
        var h = height;
        var f = Math.Min(PanelMetrics.FlareHeight, h / 2);
        var r = Math.Max(Math.Min(PanelMetrics.DockedCornerRadius, Math.Min(w, (h - f * 2) / 2)), 0);
        var fw = Math.Max(Math.Min(PanelMetrics.FlareWidth, w - r), 0);
        const double k = FlareHandle;

        var figure = new PathFigure { StartPoint = new Point(r, f), IsClosed = true, IsFilled = true };
        var segments = figure.Segments;

        segments.Add(new LineSegment(new Point(w - fw, f), true));
        segments.Add(new BezierSegment(
            new Point(w - fw * (1 - k), f),
            new Point(w, f * k),
            new Point(w, 0),
            true));
        segments.Add(new LineSegment(new Point(w, h), true));
        segments.Add(new BezierSegment(
            new Point(w, h - f * k),
            new Point(w - fw * (1 - k), h - f),
            new Point(w - fw, h - f),
            true));
        segments.Add(new LineSegment(new Point(r, h - f), true));
        AddCorner(segments, new Point(r, h - f - r), r, new Vector(0, 1), new Vector(-1, 0));
        segments.Add(new LineSegment(new Point(0, f + r), true));
        AddCorner(segments, new Point(r, f + r), r, new Vector(-1, 0), new Vector(0, -1));

        var geometry = new PathGeometry(new[] { figure });
        if (!dockedRight)
        {
            geometry.Transform = new MatrixTransform(-1, 0, 0, 1, w, 0);
        }

        geometry.Freeze();
        return geometry;
    }

    /// <summary>A quarter superellipse from <paramref name="start"/> to <paramref name="end"/> around the centre.</summary>
    private static void AddCorner(PathSegmentCollection segments, Point center, double radius, Vector start, Vector end)
    {
        if (radius <= 0)
        {
            return;
        }

        for (var step = 1; step <= CornerSamples; step++)
        {
            var t = (double)step / CornerSamples * (Math.PI / 2);
            var along = Math.Pow(Math.Cos(t), 2 / SquircleExponent);
            var across = Math.Pow(Math.Sin(t), 2 / SquircleExponent);

            segments.Add(new LineSegment(
                new Point(
                    center.X + radius * (start.X * along + end.X * across),
                    center.Y + radius * (start.Y * along + end.Y * across)),
                true));
        }
    }
}

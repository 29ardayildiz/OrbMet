using System.Windows;
using System.Windows.Media;
using OrbMet.Core.Placement;
using OrbMet.Desktop.Infrastructure;

namespace OrbMet.Desktop.Controls;

/// <summary>Where one slot sits in the strip, and whether its percentage is beside the ring or under it.</summary>
internal sealed record SlotFrame(Rect Bounds, bool PercentBeside);

/// <summary>
/// The strip's drawing for one dock: its size, the black outline, each slot's
/// frame, and the arms a card opens from. A capsule has one arm, the whole
/// strip; an L has a horizontal and a vertical one.
/// </summary>
internal sealed record PanelPlan(Size Size, Geometry Outline, IReadOnlyList<SlotFrame> Slots, Rect HorizontalArm, Rect VerticalArm);

/// <summary>
/// The three outlines of design.md 4.2: a vertical capsule on a side or
/// floating, a horizontal capsule along the top or bottom, and an L in a
/// corner with the first provider on the horizontal arm and the rest on the
/// vertical one. The L is laid out for the top-right corner and mirrored.
/// </summary>
internal static class PanelOutline
{
    public static PanelPlan For(PanelDock dock, int slots)
    {
        var count = Math.Max(slots, 1);
        return PanelLayout.ShapeOf(dock) switch
        {
            PanelShape.Horizontal => Horizontal(count),
            PanelShape.Corner when count >= 2 => Corner(count, PanelLayout.IsLeft(dock), PanelLayout.IsBottom(dock)),
            _ => Vertical(count),
        };
    }

    private static PanelPlan Vertical(int count)
    {
        var t = PanelMetrics.StripWidth;
        var size = new Size(t, PanelMetrics.RailHeight(count));
        var frames = Enumerable.Range(0, count)
            .Select(i => new SlotFrame(
                new Rect(0, PanelMetrics.EndPadding + i * (PanelMetrics.SlotHeight + PanelMetrics.SlotSpacing), t, PanelMetrics.SlotHeight),
                PercentBeside: false))
            .ToList();
        return new PanelPlan(size, Capsule(size), frames, Rect.Empty, new Rect(size));
    }

    private static PanelPlan Horizontal(int count)
    {
        var t = PanelMetrics.StripWidth;
        var size = new Size(PanelMetrics.RailLength(count), t);
        var top = (t - PanelMetrics.RingDiameter) / 2;
        var frames = Enumerable.Range(0, count)
            .Select(i => new SlotFrame(
                new Rect(PanelMetrics.BesideEndPadding + i * (PanelMetrics.BesideSlotWidth + PanelMetrics.BesideSlotSpacing), top, PanelMetrics.BesideSlotWidth, PanelMetrics.RingDiameter),
                PercentBeside: true))
            .ToList();
        return new PanelPlan(size, Capsule(size), frames, new Rect(size), Rect.Empty);
    }

    /// <summary>
    /// The L for the top-right corner, then mirrored: a horizontal arm along
    /// the top holding the first slot, the corner block, and a vertical arm
    /// down the side holding the rest.
    /// </summary>
    private static PanelPlan Corner(int count, bool left, bool bottom)
    {
        var t = PanelMetrics.StripWidth;
        var w = PanelMetrics.BesideEndPadding + PanelMetrics.BesideSlotWidth + PanelMetrics.CornerArmGap + t;
        var h = t + PanelMetrics.CornerArmGap + (count - 1) * PanelMetrics.SlotHeight + (count - 2) * PanelMetrics.SlotSpacing + PanelMetrics.EndPadding;
        var size = new Size(w, h);

        var frames = new List<SlotFrame>
        {
            new(new Rect(PanelMetrics.BesideEndPadding, (t - PanelMetrics.RingDiameter) / 2, PanelMetrics.BesideSlotWidth, PanelMetrics.RingDiameter), PercentBeside: true),
        };
        for (var k = 1; k < count; k++)
        {
            var y = t + PanelMetrics.CornerArmGap + (k - 1) * (PanelMetrics.SlotHeight + PanelMetrics.SlotSpacing);
            frames.Add(new SlotFrame(new Rect(w - t, y, t, PanelMetrics.SlotHeight), PercentBeside: false));
        }

        var horizontalArm = new Rect(0, 0, w, t);
        var verticalArm = new Rect(w - t, 0, t, h);

        var outline = TopRightL(w, h, t);
        if (left || bottom)
        {
            // Mirror the drawing and every frame across the strip's own box.
            var flip = new MatrixTransform(left ? -1 : 1, 0, 0, bottom ? -1 : 1, left ? w : 0, bottom ? h : 0);
            outline = Transformed(outline, flip);
            frames = frames.Select(f => f with { Bounds = Mirror(f.Bounds, size, left, bottom) }).ToList();
            horizontalArm = Mirror(horizontalArm, size, left, bottom);
            verticalArm = Mirror(verticalArm, size, left, bottom);
        }

        return new PanelPlan(size, outline, frames, horizontalArm, verticalArm);
    }

    private static Geometry Capsule(Size size)
    {
        var radius = Math.Min(size.Width, size.Height) / 2;
        var capsule = new RectangleGeometry(new Rect(size), radius, radius);
        capsule.Freeze();
        return capsule;
    }

    /// <summary>
    /// Clockwise from the horizontal arm's free end: its rounded end, the
    /// outer corner, the vertical arm's rounded end, then the soft inner corner.
    /// </summary>
    private static Geometry TopRightL(double w, double h, double t)
    {
        var r = t / 2;
        var inner = PanelMetrics.CornerInnerRadius;
        var figure = new PathFigure { StartPoint = new Point(r, 0), IsClosed = true, IsFilled = true };
        figure.Segments.Add(new LineSegment(new Point(w - r, 0), true));
        figure.Segments.Add(new ArcSegment(new Point(w, r), new Size(r, r), 0, false, SweepDirection.Clockwise, true));
        figure.Segments.Add(new LineSegment(new Point(w, h - r), true));
        figure.Segments.Add(new ArcSegment(new Point(w - t, h - r), new Size(r, r), 0, false, SweepDirection.Clockwise, true));
        figure.Segments.Add(new LineSegment(new Point(w - t, t + inner), true));
        figure.Segments.Add(new ArcSegment(new Point(w - t - inner, t), new Size(inner, inner), 0, false, SweepDirection.Counterclockwise, true));
        figure.Segments.Add(new LineSegment(new Point(r, t), true));
        figure.Segments.Add(new ArcSegment(new Point(r, 0), new Size(r, r), 0, false, SweepDirection.Clockwise, true));

        var path = new PathGeometry();
        path.Figures.Add(figure);
        path.Freeze();
        return path;
    }

    private static Geometry Transformed(Geometry geometry, Transform transform)
    {
        var flattened = geometry.CloneCurrentValue();
        flattened.Transform = transform;
        var result = PathGeometry.CreateFromGeometry(flattened);
        result.Freeze();
        return result;
    }

    private static Rect Mirror(Rect rect, Size size, bool horizontally, bool vertically) => new(
        horizontally ? size.Width - rect.Right : rect.X,
        vertically ? size.Height - rect.Bottom : rect.Y,
        rect.Width,
        rect.Height);
}

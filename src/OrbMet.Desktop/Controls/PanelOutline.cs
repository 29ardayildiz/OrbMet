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
/// corner with the first providers on the horizontal arm and those that have
/// turned the corner on the vertical one. The L is laid out for the top-right
/// corner and mirrored.
/// </summary>
internal static class PanelOutline
{
    /// <param name="reversed">
    /// The slots run along the outline last to first: bottom to top,
    /// right to left, or in an L from the vertical arm's end to the horizontal's.
    /// </param>
    /// <param name="down">In a corner, the rings on the side arm (<see cref="PanelLayout.CornerDown"/>).</param>
    public static PanelPlan For(PanelDock dock, int slots, bool reversed = false, int down = 0)
    {
        var count = Math.Max(slots, 1);
        var plan = PanelLayout.ShapeOf(dock) switch
        {
            PanelShape.Horizontal => Horizontal(count),
            PanelShape.Corner when count >= 2 => Corner(count, PanelLayout.CornerDown(count, down), PanelLayout.IsLeft(dock), PanelLayout.IsBottom(dock)),
            _ => Vertical(count),
        };
        return reversed ? plan with { Slots = plan.Slots.Reverse().ToList() } : plan;
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
    /// the top holding the first slots, the corner block, and a vertical arm
    /// down the side holding the last <paramref name="down"/>, the ones that
    /// have turned the corner (design.md 4.2).
    /// </summary>
    private static PanelPlan Corner(int count, int down, bool left, bool bottom)
    {
        var t = PanelMetrics.StripWidth;
        var across = count - down;
        var w = PanelMetrics.BesideEndPadding + across * PanelMetrics.BesideSlotWidth + (across - 1) * PanelMetrics.BesideSlotSpacing
            + PanelMetrics.CornerArmGap + t;
        var h = t + PanelMetrics.CornerArmGap + down * PanelMetrics.SlotHeight + (down - 1) * PanelMetrics.SlotSpacing + PanelMetrics.EndPadding;
        var size = new Size(w, h);

        var frames = new List<SlotFrame>();
        for (var k = 0; k < across; k++)
        {
            var x = PanelMetrics.BesideEndPadding + k * (PanelMetrics.BesideSlotWidth + PanelMetrics.BesideSlotSpacing);
            frames.Add(new SlotFrame(new Rect(x, (t - PanelMetrics.RingDiameter) / 2, PanelMetrics.BesideSlotWidth, PanelMetrics.RingDiameter), PercentBeside: true));
        }

        for (var k = 0; k < down; k++)
        {
            var y = t + PanelMetrics.CornerArmGap + k * (PanelMetrics.SlotHeight + PanelMetrics.SlotSpacing);
            frames.Add(new SlotFrame(new Rect(w - t, y, t, PanelMetrics.SlotHeight), PercentBeside: false));
        }

        var horizontalArm = new Rect(0, 0, w, t);
        var verticalArm = new Rect(w - t, 0, t, h);

        if (left || bottom)
        {
            // Mirror every frame and both arms across the strip's own box.
            frames = frames.Select(f => f with { Bounds = Mirror(f.Bounds, size, left, bottom) }).ToList();
            horizontalArm = Mirror(horizontalArm, size, left, bottom);
            verticalArm = Mirror(verticalArm, size, left, bottom);
        }

        return new PanelPlan(size, Bars(horizontalArm, verticalArm), frames, horizontalArm, verticalArm);
    }

    private static Geometry Capsule(Size size)
    {
        var radius = Math.Min(size.Width, size.Height) / 2;
        var capsule = new RectangleGeometry(new Rect(size), radius, radius);
        capsule.Freeze();
        return capsule;
    }

    /// <summary>
    /// The strip drawn as its two arms, each a capsule, joined by a soft
    /// concave corner where one runs past the other. This draws the L and
    /// every shape the strip passes through while it changes (design.md 4.2);
    /// an empty arm is left out.
    /// </summary>
    public static Geometry Bars(Rect horizontal, Rect vertical)
    {
        var group = new GeometryGroup { FillRule = FillRule.Nonzero };
        foreach (var bar in new[] { horizontal, vertical })
        {
            if (!bar.IsEmpty)
            {
                var radius = Math.Min(bar.Width, bar.Height) / 2;
                group.Children.Add(new RectangleGeometry(bar, radius, radius));
            }
        }

        if (Fillet(horizontal, vertical) is { } fillet)
        {
            group.Children.Add(fillet);
        }

        group.Freeze();
        return group;
    }

    /// <summary>
    /// The concave corner's fill between the arms, growing with how far each
    /// runs past the other so it is zero while one still sits inside the other.
    /// </summary>
    private static Geometry? Fillet(Rect horizontal, Rect vertical)
    {
        if (horizontal.IsEmpty || vertical.IsEmpty)
        {
            return null;
        }

        // Which way the horizontal arm runs past the vertical one, and the vertical past the horizontal.
        var (sx, cornerX, reachX) = horizontal.Left < vertical.Left
            ? (-1, vertical.Left, vertical.Left - horizontal.Left)
            : (1, vertical.Right, horizontal.Right - vertical.Right);
        var (sy, cornerY, reachY) = vertical.Bottom > horizontal.Bottom
            ? (1, horizontal.Bottom, vertical.Bottom - horizontal.Bottom)
            : (-1, horizontal.Top, horizontal.Top - vertical.Top);

        // Only where both arms' edges are straight: an arm's last half
        // thickness is its rounded end, which the fill would poke out of.
        var radius = Math.Min(
            PanelMetrics.CornerInnerRadius,
            Math.Min(reachX - horizontal.Height / 2, reachY - vertical.Width / 2));
        if (radius <= 0.5)
        {
            return null;
        }

        var figure = new PathFigure { StartPoint = new Point(cornerX, cornerY), IsClosed = true, IsFilled = true };
        figure.Segments.Add(new LineSegment(new Point(cornerX + sx * radius, cornerY), true));
        figure.Segments.Add(new ArcSegment(
            new Point(cornerX, cornerY + sy * radius),
            new Size(radius, radius),
            0,
            false,
            sx * sy < 0 ? SweepDirection.Clockwise : SweepDirection.Counterclockwise,
            true));

        var path = new PathGeometry();
        path.Figures.Add(figure);
        return path;
    }

    private static Rect Mirror(Rect rect, Size size, bool horizontally, bool vertically) => new(
        horizontally ? size.Width - rect.Right : rect.X,
        vertically ? size.Height - rect.Bottom : rect.Y,
        rect.Width,
        rect.Height);
}

/// <summary>
/// The strip on screen at one moment: its two arms and each slot's frame,
/// with how far the percentage has moved beside the ring (0 under, 1 beside).
/// A change of outline is drawn as a run of poses between the old one and the
/// new (design.md 4.2).
/// </summary>
internal sealed record StripPose(Rect Horizontal, Rect Vertical, IReadOnlyList<(Rect Frame, double Beside)> Slots)
{
    public static StripPose Of(PanelPlan plan, Point origin)
    {
        var shift = new Vector(origin.X, origin.Y);
        return new StripPose(
            Moved(plan.HorizontalArm, shift),
            Moved(plan.VerticalArm, shift),
            plan.Slots.Select(s => (Moved(s.Bounds, shift), s.PercentBeside ? 1.0 : 0.0)).ToList());
    }

    public Rect Bounds => Rect.Union(Horizontal, Vertical);

    /// <summary>Each slot's ring centre, in slot order.</summary>
    public IReadOnlyList<(double X, double Y)> Centres() => Slots
        .Select(s => s.Beside > 0.5
            ? (s.Frame.X + PanelMetrics.RingDiameter / 2, s.Frame.Y + s.Frame.Height / 2)
            : (s.Frame.X + s.Frame.Width / 2, s.Frame.Y + PanelMetrics.RingDiameter / 2))
        .ToList();

    /// <summary>
    /// The pose a fraction <paramref name="t"/> of the way from one to the
    /// other. An arm only one of them has grows out of, or shrinks into, the
    /// end of the other arm nearest to it, so the change reads as the strip
    /// stretching rather than fading.
    /// </summary>
    public static StripPose Between(StripPose from, StripPose to, double t)
    {
        var fromH = from.Horizontal.IsEmpty ? Bud(from.Vertical, to.Horizontal, vertical: true) : from.Horizontal;
        var toH = to.Horizontal.IsEmpty ? Bud(to.Vertical, from.Horizontal, vertical: true) : to.Horizontal;
        var fromV = from.Vertical.IsEmpty ? Bud(from.Horizontal, to.Vertical, vertical: false) : from.Vertical;
        var toV = to.Vertical.IsEmpty ? Bud(to.Horizontal, from.Vertical, vertical: false) : to.Vertical;

        var slots = to.Slots
            .Select((target, i) => i < from.Slots.Count
                ? (Lerp(from.Slots[i].Frame, target.Frame, t), from.Slots[i].Beside + (target.Beside - from.Slots[i].Beside) * t)
                : target)
            .ToList();

        return new StripPose(Lerp(fromH, toH, t), Lerp(fromV, toV, t), slots);
    }

    public StripPose Offset(Vector by) => new(
        Moved(Horizontal, by),
        Moved(Vertical, by),
        Slots.Select(s => (Moved(s.Frame, by), s.Beside)).ToList());

    /// <summary>
    /// A square the arm's thickness inside <paramref name="arm"/>, at the
    /// spot along it closest to <paramref name="toward"/>: where a missing arm
    /// grows from. Empty when neither pose has that arm.
    /// </summary>
    private static Rect Bud(Rect arm, Rect toward, bool vertical)
    {
        if (arm.IsEmpty || toward.IsEmpty)
        {
            return Rect.Empty;
        }

        return vertical
            ? new Rect(arm.X, Math.Clamp(toward.Y, arm.Top, Math.Max(arm.Bottom - arm.Width, arm.Top)), arm.Width, arm.Width)
            : new Rect(Math.Clamp(toward.X, arm.Left, Math.Max(arm.Right - arm.Height, arm.Left)), arm.Y, arm.Height, arm.Height);
    }

    private static Rect Lerp(Rect a, Rect b, double t)
    {
        if (a.IsEmpty || b.IsEmpty)
        {
            return t < 1 ? a : b;
        }

        return new Rect(
            a.X + (b.X - a.X) * t,
            a.Y + (b.Y - a.Y) * t,
            a.Width + (b.Width - a.Width) * t,
            a.Height + (b.Height - a.Height) * t);
    }

    private static Rect Moved(Rect rect, Vector by) => rect.IsEmpty ? rect : Rect.Offset(rect, by);
}

namespace OrbMet.Core.Placement;

/// <summary>
/// Where the strip lives (design.md 4.2): against a side (vertical capsule),
/// against the top or bottom (horizontal capsule), in a corner (an L along
/// both edges), or free (vertical capsule).
/// </summary>
public enum PanelDock
{
    Right,
    Left,
    Floating,
    Top,
    Bottom,
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight,
}

/// <summary>The outline a dock is drawn with.</summary>
public enum PanelShape
{
    Vertical,
    Horizontal,
    Corner,
}

/// <summary>An axis-aligned box in screen DIPs, y growing downwards.</summary>
public readonly record struct Box(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;

    public double Bottom => Y + Height;

    public double CenterX => X + Width / 2;

    public double CenterY => Y + Height / 2;
}

/// <summary>
/// The strip's remembered place. The ratios are the strip's position within
/// one display's work area, so it keeps its place on that display whatever
/// the display's size. The strip is stored, never the window.
/// </summary>
/// <param name="HorizontalRatio">0 = left, 1 = right; used while floating or along the top/bottom.</param>
/// <param name="VerticalRatio">0 = top, 1 = bottom; used while floating or along a side.</param>
/// <param name="DisplayId">The display it was left on, or null for the primary.</param>
public sealed record PanelPosition(PanelDock Dock, double HorizontalRatio, double VerticalRatio, string? DisplayId)
{
    /// <summary>First run: docked right, vertically centred, on the primary display.</summary>
    public static PanelPosition Default { get; } = new(PanelDock.Right, 1, 0.5, null);
}

/// <summary>
/// Placement arithmetic from design.md sections 4.2 and 9: which edge or
/// corner a drag fuses to, where the strip goes for a stored position, and
/// the ratios that record a drop. The strip's size for each dock comes from
/// the caller, since only the desktop knows its drawing.
/// </summary>
public static class PanelLayout
{
    /// <summary>
    /// How close the pointer carrying the strip has to come to an edge before
    /// the strip fuses to it: the strip's 64 DIP thickness, the 8 DIP inset
    /// and some slack, so a strip already on an edge stays there while it is
    /// slid along it. Near two edges at once it fuses to the corner.
    /// </summary>
    public const double EdgeReach = 96;

    /// <summary>A floating strip stays at least this far from the sides when restored.</summary>
    public const double DockDistance = 32;

    /// <summary>
    /// A docked strip stands this far inside the work area's edges rather
    /// than touching them; the gap passes clicks through.
    /// </summary>
    public const double EdgeInset = 8;

    public static PanelShape ShapeOf(PanelDock dock) => dock switch
    {
        PanelDock.Top or PanelDock.Bottom => PanelShape.Horizontal,
        PanelDock.TopLeft or PanelDock.TopRight or PanelDock.BottomLeft or PanelDock.BottomRight => PanelShape.Corner,
        _ => PanelShape.Vertical,
    };

    public static bool IsLeft(PanelDock dock) => dock is PanelDock.Left or PanelDock.TopLeft or PanelDock.BottomLeft;

    public static bool IsRight(PanelDock dock) => dock is PanelDock.Right or PanelDock.TopRight or PanelDock.BottomRight;

    public static bool IsTop(PanelDock dock) => dock is PanelDock.Top or PanelDock.TopLeft or PanelDock.TopRight;

    public static bool IsBottom(PanelDock dock) => dock is PanelDock.Bottom or PanelDock.BottomLeft or PanelDock.BottomRight;

    /// <summary>
    /// What a strip carried by a pointer at (<paramref name="pointerX"/>,
    /// <paramref name="pointerY"/>) fuses to. Decided during the drag, not on
    /// release, from the pointer rather than the strip's outline, which
    /// changes shape with the answer.
    /// </summary>
    public static PanelDock DockFor(double pointerX, double pointerY, Box workArea)
    {
        var left = pointerX - workArea.X <= EdgeReach;
        var right = !left && workArea.Right - pointerX <= EdgeReach;
        var top = pointerY - workArea.Y <= EdgeReach;
        var bottom = !top && workArea.Bottom - pointerY <= EdgeReach;

        return (left, right, top, bottom) switch
        {
            (true, _, true, _) => PanelDock.TopLeft,
            (true, _, _, true) => PanelDock.BottomLeft,
            (_, true, true, _) => PanelDock.TopRight,
            (_, true, _, true) => PanelDock.BottomRight,
            (true, _, _, _) => PanelDock.Left,
            (_, true, _, _) => PanelDock.Right,
            (_, _, true, _) => PanelDock.Top,
            (_, _, _, true) => PanelDock.Bottom,
            _ => PanelDock.Floating,
        };
    }

    /// <summary>The left edge of a strip docked to a side of a work area.</summary>
    public static double DockedX(PanelDock side, Box workArea, double railWidth) => IsLeft(side)
        ? workArea.X + EdgeInset
        : workArea.Right - railWidth - EdgeInset;

    /// <summary>The top edge of a strip docked to the top or bottom of a work area.</summary>
    public static double DockedY(PanelDock side, Box workArea, double railHeight) => IsTop(side)
        ? workArea.Y + EdgeInset
        : workArea.Bottom - railHeight - EdgeInset;

    /// <summary>
    /// Where a strip being dragged goes for a wanted top-left: pinned to its
    /// edges when docked, otherwise inside the work area. Along an edge it
    /// slides freely but keeps the inset at both ends.
    /// </summary>
    public static (double X, double Y) DragOrigin(PanelDock dock, double wantedX, double wantedY, Box workArea, double railWidth, double railHeight)
    {
        var (x, y) = dock == PanelDock.Floating
            ? Clamp(wantedX, wantedY, workArea, railWidth, railHeight)
            : ClampInset(wantedX, wantedY, workArea, railWidth, railHeight);

        if (IsLeft(dock) || IsRight(dock))
        {
            x = DockedX(dock, workArea, railWidth);
        }

        if (IsTop(dock) || IsBottom(dock))
        {
            y = DockedY(dock, workArea, railHeight);
        }

        return (x, y);
    }

    /// <summary>The strip's top-left corner for a position inside a work area.</summary>
    public static (double X, double Y) RailOrigin(PanelPosition position, Box workArea, double railWidth, double railHeight)
    {
        ArgumentNullException.ThrowIfNull(position);

        if (position.Dock == PanelDock.Floating)
        {
            // Floating means not touching: a stored ratio that would put the
            // strip flush against a side is pulled back off it.
            var x = Math.Min(
                Math.Max(workArea.X + Unit(position.HorizontalRatio) * Math.Max(workArea.Width - railWidth, 0), workArea.X + DockDistance),
                Math.Max(workArea.Right - railWidth - DockDistance, workArea.X + DockDistance));
            var y = workArea.Y + Unit(position.VerticalRatio) * Math.Max(workArea.Height - railHeight, 0);
            return (x, y);
        }

        var (minX, spanX) = InsetRange(workArea.X, workArea.Width, railWidth);
        var (minY, spanY) = InsetRange(workArea.Y, workArea.Height, railHeight);
        return DragOrigin(
            position.Dock,
            minX + Unit(position.HorizontalRatio) * spanX,
            minY + Unit(position.VerticalRatio) * spanY,
            workArea,
            railWidth,
            railHeight);
    }

    /// <summary>
    /// The ratios that would put the strip's top-left at a spot in a work area.
    /// Docked, they are measured inside the inset so they round-trip through
    /// <see cref="RailOrigin"/>.
    /// </summary>
    public static (double Horizontal, double Vertical) Ratios(PanelDock dock, double x, double y, Box workArea, double railWidth, double railHeight)
    {
        if (dock == PanelDock.Floating)
        {
            return (
                Ratio(x - workArea.X, Math.Max(workArea.Width - railWidth, 0)),
                Ratio(y - workArea.Y, Math.Max(workArea.Height - railHeight, 0)));
        }

        var (minX, spanX) = InsetRange(workArea.X, workArea.Width, railWidth);
        var (minY, spanY) = InsetRange(workArea.Y, workArea.Height, railHeight);
        return (Ratio(x - minX, spanX), Ratio(y - minY, spanY));
    }

    /// <summary>Keeps a top-left inside the work area for a strip of the given size.</summary>
    public static (double X, double Y) Clamp(double x, double y, Box workArea, double railWidth, double railHeight) => (
        Math.Min(Math.Max(x, workArea.X), Math.Max(workArea.Right - railWidth, workArea.X)),
        Math.Min(Math.Max(y, workArea.Y), Math.Max(workArea.Bottom - railHeight, workArea.Y)));

    private static (double X, double Y) ClampInset(double x, double y, Box workArea, double railWidth, double railHeight)
    {
        var (minX, spanX) = InsetRange(workArea.X, workArea.Width, railWidth);
        var (minY, spanY) = InsetRange(workArea.Y, workArea.Height, railHeight);
        return (Math.Clamp(x, minX, minX + spanX), Math.Clamp(y, minY, minY + spanY));
    }

    private static (double Min, double Span) InsetRange(double start, double length, double size) =>
        (start + EdgeInset, Math.Max(length - size - 2 * EdgeInset, 0));

    private static double Ratio(double offset, double span) => span > 0 ? Unit(offset / span) : 0.5;

    private static double Unit(double value) => double.IsFinite(value) ? Math.Clamp(value, 0, 1) : 0.5;
}

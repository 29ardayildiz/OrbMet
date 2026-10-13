namespace OrbMet.Core.Placement;

/// <summary>Where the strip lives: fused to a side of the screen, or free.</summary>
public enum PanelDock
{
    Right,
    Left,
    Floating,
}

/// <summary>An axis-aligned box in screen DIPs, y growing downwards.</summary>
public readonly record struct Box(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;

    public double Bottom => Y + Height;

    public double CenterX => X + Width / 2;
}

/// <summary>
/// The strip's remembered place. The ratios are the strip's position within
/// one display's work area, so it keeps its place on that display whatever
/// the display's size. The strip is stored, never the window.
/// </summary>
/// <param name="HorizontalRatio">0 = left edge, 1 = right edge; used only while floating.</param>
/// <param name="VerticalRatio">0 = top, 1 = bottom.</param>
/// <param name="DisplayId">The display it was left on, or null for the primary.</param>
public sealed record PanelPosition(PanelDock Dock, double HorizontalRatio, double VerticalRatio, string? DisplayId)
{
    /// <summary>First run: docked right, vertically centred, on the primary display.</summary>
    public static PanelPosition Default { get; } = new(PanelDock.Right, 1, 0.5, null);
}

/// <summary>
/// Placement arithmetic from design.md sections 4.2 and 9, after Pulse's
/// PanelPlacement: where the strip goes for a stored position, which side a
/// drag fuses to, and the ratios that record a drop.
/// </summary>
public static class PanelLayout
{
    /// <summary>
    /// How close the strip has to come to a side before it fuses to it. Easy
    /// to hit on purpose, tight enough that parking near an edge still works.
    /// A floating strip also stays at least this far from both sides.
    /// </summary>
    public const double DockDistance = 32;

    /// <summary>The strip's top-left corner for a position inside a work area.</summary>
    public static (double X, double Y) RailOrigin(PanelPosition position, Box workArea, double railWidth, double railHeight)
    {
        ArgumentNullException.ThrowIfNull(position);

        var x = position.Dock switch
        {
            PanelDock.Left => workArea.X,
            PanelDock.Right => workArea.Right - railWidth,

            // Floating means not touching: a stored ratio that would put the
            // strip flush against a side is pulled back off it.
            _ => Math.Min(
                Math.Max(workArea.X + Unit(position.HorizontalRatio) * Math.Max(workArea.Width - railWidth, 0), workArea.X + DockDistance),
                Math.Max(workArea.Right - railWidth - DockDistance, workArea.X + DockDistance)),
        };

        var y = workArea.Y + Unit(position.VerticalRatio) * Math.Max(workArea.Height - railHeight, 0);
        return (x, y);
    }

    /// <summary>
    /// Which side a strip being carried to <paramref name="wantedX"/> fuses
    /// to. Decided during the drag, not on release.
    /// </summary>
    public static PanelDock DockFor(double wantedX, double railWidth, Box workArea)
    {
        if (wantedX - workArea.X <= DockDistance)
        {
            return PanelDock.Left;
        }

        return workArea.Right - (wantedX + railWidth) <= DockDistance ? PanelDock.Right : PanelDock.Floating;
    }

    /// <summary>The ratios that would put the strip's top-left at a spot in a work area.</summary>
    public static (double Horizontal, double Vertical) Ratios(double x, double y, Box workArea, double railWidth, double railHeight)
    {
        var across = Math.Max(workArea.Width - railWidth, 0);
        var down = Math.Max(workArea.Height - railHeight, 0);
        return (
            across > 0 ? Unit((x - workArea.X) / across) : 0.5,
            down > 0 ? Unit((y - workArea.Y) / down) : 0.5);
    }

    /// <summary>Keeps a top-left inside the work area for a strip of the given size.</summary>
    public static (double X, double Y) Clamp(double x, double y, Box workArea, double railWidth, double railHeight) => (
        Math.Min(Math.Max(x, workArea.X), Math.Max(workArea.Right - railWidth, workArea.X)),
        Math.Min(Math.Max(y, workArea.Y), Math.Max(workArea.Bottom - railHeight, workArea.Y)));

    private static double Unit(double value) => double.IsFinite(value) ? Math.Clamp(value, 0, 1) : 0.5;
}

namespace OrbMet.Desktop.Infrastructure;

/// <summary>
/// The panel's measurements in DIPs, kept in one place so drawing and
/// placement never disagree. Values follow design.md sections 4 and 7; the
/// ring and spacing come from Pulse's standard size.
/// </summary>
public static class PanelMetrics
{
    public const double StripWidth = 64;
    public const double RingDiameter = 36;
    public const double RingThickness = 4;
    public const double RingToText = 6;
    public const double PercentHeight = 16;
    public const double SlotHeight = RingDiameter + RingToText + PercentHeight;
    public const double SlotSpacing = 30;

    /// <summary>From each end of the capsule to its nearest slot.</summary>
    public const double EndPadding = 22;

    /// <summary>A small move before a press counts as a drag, so a click stays a click.</summary>
    public const double DragThreshold = 3;

    /// <summary>The strip's length for a number of slots; docked and floating are the same capsule.</summary>
    public static double RailHeight(int slots)
    {
        var count = Math.Max(slots, 0);
        return 2 * EndPadding + count * SlotHeight + Math.Max(count - 1, 0) * SlotSpacing;
    }

    /// <summary>Gap between the ring and the logo's backing disc, and the logo's share of that disc.</summary>
    public const double RingCentreGap = 4;
    public const double MarkScale = 0.8;

    public const double WorkAreaMargin = 8;

    public const double CardWidth = 250;
    public const double CardPadding = 18;

    /// <summary>The ring's outer radius, so the card and the rings share one curve.</summary>
    public const double CardCornerRadius = (RingDiameter + RingThickness) / 2;

    /// <summary>The same radius for XAML, which cannot convert a double to a CornerRadius.</summary>
    public static readonly System.Windows.CornerRadius CardCornerRadiusValue = new(CardCornerRadius);

    /// <summary>From the card to the strip.</summary>
    public const double CardGap = 8;

    /// <summary>How often the pointer is sampled while the card is up (design.md 8.1).</summary>
    public static readonly TimeSpan PointerSampleInterval = TimeSpan.FromMilliseconds(150);
}

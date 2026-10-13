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

    /// <summary>The vertical capsule's length for a number of slots (sides and floating).</summary>
    public static double RailHeight(int slots)
    {
        var count = Math.Max(slots, 0);
        return 2 * EndPadding + count * SlotHeight + Math.Max(count - 1, 0) * SlotSpacing;
    }

    /// <summary>
    /// Along the top or bottom, and on the L's horizontal arm, the percentage
    /// sits beside the ring: ring, 6 DIP, room for "%100".
    /// </summary>
    public const double BesideSlotWidth = RingDiameter + RingToText + 34;

    /// <summary>
    /// From each end of the horizontal capsule to its nearest ring: the ring
    /// then sits concentric with the capsule's rounded end.
    /// </summary>
    public const double BesideEndPadding = (StripWidth - RingDiameter) / 2;

    public const double BesideSlotSpacing = 22;

    /// <summary>The horizontal capsule's length for a number of slots (top and bottom).</summary>
    public static double RailLength(int slots)
    {
        var count = Math.Max(slots, 0);
        return 2 * BesideEndPadding + count * BesideSlotWidth + Math.Max(count - 1, 0) * BesideSlotSpacing;
    }

    /// <summary>In the L, between each arm's slot and the corner block.</summary>
    public const double CornerArmGap = 10;

    /// <summary>The soft concave corner where the L's arms meet.</summary>
    public const double CornerInnerRadius = 18;

    /// <summary>Gap between the ring and the logo's backing disc, and the logo's share of that disc.</summary>
    public const double RingCentreGap = 4;
    public const double MarkScale = 0.8;

    public const double WorkAreaMargin = 8;

    /// <summary>
    /// Clear room the panel window keeps around the largest outline, so a
    /// change of outline can stretch inside it (design.md 8.2). Transparent,
    /// so the pointer passes through it.
    /// </summary>
    public const double WindowRoom = 220;

    /// <summary>
    /// How far the window may move against its drawing in one frame. The
    /// drawing reaches the screen a frame after the window moves, so this is
    /// also how far the strip can appear off its place for that frame.
    /// </summary>
    public const double WindowCreep = 2;

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

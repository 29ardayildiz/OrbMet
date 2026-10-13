namespace OrbMet.Desktop.Infrastructure;

/// <summary>
/// The panel's measurements in DIPs, kept in one place so drawing and
/// placement never disagree. Values follow design.md sections 4 and 7, which
/// take them from Pulse's standard size.
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

    /// <summary>From the strip's top to its first slot when docked, flare included.</summary>
    public const double DockedEndPadding = 46;

    /// <summary>Outer corners of the docked strip: radius of a 4th-order superellipse.</summary>
    public const double DockedCornerRadius = 26;

    /// <summary>The concave sweep joining each end of the docked strip to the screen edge.</summary>
    public const double FlareHeight = 24;
    public const double FlareWidth = 38;

    /// <summary>Gap between the ring and the logo's backing disc, and the logo's share of that disc.</summary>
    public const double RingCentreGap = 4;
    public const double MarkScale = 0.8;

    public const double WorkAreaMargin = 8;

    /// <summary>The card's body; the window adds the tail's width beside it.</summary>
    public const double CardWidth = 250;
    public const double CardTailWidth = 20;
    public const double CardTailHeight = 40;
    public const double CardWindowWidth = CardWidth + CardTailWidth;
    public const double CardPadding = 18;

    /// <summary>The ring's outer radius, so the card and the rings share one curve.</summary>
    public const double CardCornerRadius = (RingDiameter + RingThickness) / 2;

    /// <summary>From the tail's tip to the strip.</summary>
    public const double CardGap = 8;

    /// <summary>How often the pointer is sampled while the card is up (design.md 8.1).</summary>
    public static readonly TimeSpan PointerSampleInterval = TimeSpan.FromMilliseconds(150);
}

namespace OrbMet.Desktop.Infrastructure;

/// <summary>
/// The panel's measurements in DIPs, kept in one place so drawing and
/// placement never disagree. Values follow design.md section 4.
/// </summary>
public static class PanelMetrics
{
    public const double StripWidth = 76;
    public const double RingDiameter = 40;
    public const double SlotHeight = 70;
    public const double WorkAreaMargin = 8;
    public const double CardWidth = 304;
    public const double CardGap = 8;

    /// <summary>Hover delays from design.md section 8.</summary>
    public static readonly TimeSpan CardOpenDelay = TimeSpan.FromMilliseconds(180);
    public static readonly TimeSpan CardCloseDelay = TimeSpan.FromMilliseconds(300);
}

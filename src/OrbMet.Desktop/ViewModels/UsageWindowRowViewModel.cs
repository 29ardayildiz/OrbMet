namespace OrbMet.Desktop.ViewModels;

/// <summary>
/// One reported quota window in the detail card. Built fresh on each update,
/// so it carries no state of its own.
/// </summary>
public sealed class UsageWindowRowViewModel
{
    public required string Title { get; init; }

    /// <summary>Raw provider value for the bar, or null when missing or over.</summary>
    public double? Percent { get; init; }

    public required string PercentText { get; init; }

    public bool IsExhausted { get; init; }

    public required string ResetText { get; init; }

    /// <summary>The reset has passed: the old value is hidden until new data arrives.</summary>
    public bool IsExpired { get; init; }

    /// <summary>True for the window the strip's ring is showing.</summary>
    public bool IsRingWindow { get; init; }

    public required string AccessibleText { get; init; }
}

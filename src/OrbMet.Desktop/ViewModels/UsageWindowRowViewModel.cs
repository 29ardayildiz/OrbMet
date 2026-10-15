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

    /// <summary>"6% used"; empty once the window's reset has passed.</summary>
    public required string UsedText { get; init; }

    /// <summary>"Forecast: lasts until the reset"; null when the forecast is off or nothing may be said.</summary>
    public string? ForecastText { get; init; }

    /// <summary>The provider's colour for the bar below the warning thresholds.</summary>
    public System.Windows.Media.Brush? GoodBrush { get; init; }

    public bool IsExhausted { get; init; }

    /// <summary>Spent colour on the figure: blocked, or at or past 100%.</summary>
    public bool IsSpent { get; init; }

    public required string ResetText { get; init; }

    /// <summary>The reset has passed: the old value is hidden until new data arrives.</summary>
    public bool IsExpired { get; init; }

    public required string AccessibleText { get; init; }
}

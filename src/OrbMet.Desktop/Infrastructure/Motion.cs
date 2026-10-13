using System.Windows;
using System.Windows.Media.Animation;

namespace OrbMet.Desktop.Infrastructure;

/// <summary>
/// The panel's transitions (design.md 6.1, 8.1, 13), all short and ease-out,
/// with no overshoot. Pulse uses springs damped to 0.84-0.86, which settle
/// with no visible bounce, so a plain ease-out over the same time matches.
/// </summary>
internal static class Motion
{
    /// <summary>The ring's arc moving from its old value to a new one.</summary>
    public static readonly Duration Arc = new(TimeSpan.FromSeconds(0.5));

    /// <summary>The card's content when it changes to another provider.</summary>
    public static readonly Duration CardContent = new(TimeSpan.FromSeconds(0.1));

    /// <summary>The card sliding along the strip to another ring.</summary>
    public static readonly Duration CardMove = new(TimeSpan.FromSeconds(0.28));

    public static readonly IEasingFunction EaseOut = CreateEaseOut();

    /// <summary>
    /// False when Windows' "Animation effects" is off; every transition is
    /// then instant.
    /// </summary>
    public static bool Enabled => SystemParameters.ClientAreaAnimation;

    public static DoubleAnimation To(double value, Duration duration) => new(value, duration)
    {
        EasingFunction = EaseOut,
    };

    private static IEasingFunction CreateEaseOut()
    {
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        ease.Freeze();
        return ease;
    }
}
